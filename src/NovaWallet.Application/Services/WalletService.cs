using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Domain.Policies;

namespace NovaWallet.Application.Services;

/// <summary>
/// Orchestrates the wallet use cases. This class knows the *shape* of each
/// workflow — begin transaction, lock, check, mutate, persist, commit — but
/// every actual business rule lives in the Domain (Wallet.Credit/Debit,
/// DailyTransferLimitPolicy, IdempotencyRecord.Resolve). If you swapped
/// Postgres for something else entirely, only IWalletRepository's
/// implementation would change — nothing here.
/// </summary>
public class WalletService : IWalletService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public WalletService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<WalletResponse> CreateWalletAsync(string customerId, CancellationToken ct)
    {
        var wallet = Wallet.Create(customerId, _clock.UtcNow);

        await _uow.Wallets.AddAsync(wallet, ct);
        await _uow.Wallets.AddAuditLogAsync(
            AuditLogEntry.Create(wallet.Id, "CREATE_WALLET", null, 0, 0, customerId, null, _clock.UtcNow), ct);

        await _uow.SaveChangesAsync(ct);
        return ToWalletResponse(wallet);
    }

    public async Task<WalletResponse> GetWalletAsync(Guid walletId, CancellationToken ct)
    {
        var wallet = await _uow.Wallets.GetReadOnlyAsync(walletId, ct)
            ?? throw new WalletNotFoundException(walletId);
        return ToWalletResponse(wallet);
    }

    public async Task<WalletResponse> CreditWalletAsync(Guid walletId, long amountKobo, string? description, string actorId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.Wallets.LockAsync(walletId, ct);
            var wallet = await _uow.Wallets.GetAsync(walletId, ct)
                ?? throw new WalletNotFoundException(walletId);

            var before = wallet.BalanceKobo;
            wallet.Credit(amountKobo);

            await _uow.Wallets.AddTransactionAsync(
                LedgerTransaction.Create(wallet.Id, TransactionType.Credit, amountKobo, wallet.BalanceKobo,
                    null, null, description ?? "Inbound NIP credit", _clock.UtcNow), ct);

            await _uow.Wallets.AddAuditLogAsync(
                AuditLogEntry.Create(wallet.Id, "CREDIT", amountKobo, before, wallet.BalanceKobo, actorId, null, _clock.UtcNow), ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitAsync(ct);
            return ToWalletResponse(wallet);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, string actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidTransferException("Idempotency-Key header is required.");
        if (request.FromWalletId == request.ToWalletId)
            throw new InvalidTransferException("fromWalletId and toWalletId must differ.");

        var requestHash = ComputeRequestHash(request);

        await _uow.BeginTransactionAsync(ct);
        try
        {
            var existing = await _uow.Wallets.GetIdempotencyRecordAsync(idempotencyKey, ct);
            if (existing is not null)
            {
                // No write has happened yet in this transaction — nothing to undo,
                // just release the transaction before replaying.
                await _uow.RollbackAsync(ct);
                return Replay(existing, requestHash);
            }

            var record = IdempotencyRecord.CreateProcessing(idempotencyKey, requestHash, _clock.UtcNow);
            var inserted = await _uow.Wallets.TryAddIdempotencyRecordAsync(record, ct);
            if (!inserted)
            {
                // Postgres aborts the *entire* transaction after a failed statement
                // (the unique-constraint violation behind TryAddIdempotencyRecordAsync
                // returning false) — every further statement on this transaction would
                // fail until it's rolled back, so roll back BEFORE re-reading.
                await _uow.RollbackAsync(ct);
                var winner = await _uow.Wallets.GetIdempotencyRecordAsync(idempotencyKey, ct)
                    ?? throw new IdempotencyRequestInProgressException();
                return Replay(winner, requestHash);
            }

            await _uow.Wallets.LockOrderedAsync(request.FromWalletId, request.ToWalletId, ct);

            var from = await _uow.Wallets.GetAsync(request.FromWalletId, ct)
                ?? throw new WalletNotFoundException(request.FromWalletId);
            var to = await _uow.Wallets.GetAsync(request.ToWalletId, ct)
                ?? throw new WalletNotFoundException(request.ToWalletId);

            from.EnsureOwnedBy(actorId);

            var (dayStartUtc, dayEndUtc) = WatClock.GetDayBoundsUtc(_clock.UtcNow);
            var alreadySentToday = await _uow.Wallets.GetOutboundTotalTodayAsync(from.Id, dayStartUtc, dayEndUtc, ct);
            DailyTransferLimitPolicy.EnsureWithinLimit(alreadySentToday, request.AmountKobo);

            var fromBefore = from.BalanceKobo;
            var toBefore = to.BalanceKobo;

            from.Debit(request.AmountKobo);
            to.Credit(request.AmountKobo);

            var now = _clock.UtcNow;
            var transferGroupId = Guid.NewGuid();

            await _uow.Wallets.AddTransactionAsync(
                LedgerTransaction.Create(from.Id, TransactionType.TransferOut, request.AmountKobo, from.BalanceKobo,
                    to.Id, idempotencyKey, request.Description, now), ct);
            await _uow.Wallets.AddTransactionAsync(
                LedgerTransaction.Create(to.Id, TransactionType.TransferIn, request.AmountKobo, to.BalanceKobo,
                    from.Id, idempotencyKey, request.Description, now), ct);

            var auditMetadata = JsonSerializer.Serialize(new { idempotencyKey, transferGroupId });
            await _uow.Wallets.AddAuditLogAsync(
                AuditLogEntry.Create(from.Id, "TRANSFER_DEBIT", request.AmountKobo, fromBefore, from.BalanceKobo, actorId, auditMetadata, now), ct);
            await _uow.Wallets.AddAuditLogAsync(
                AuditLogEntry.Create(to.Id, "TRANSFER_CREDIT", request.AmountKobo, toBefore, to.BalanceKobo, actorId, auditMetadata, now), ct);

            var response = new TransferResponse(transferGroupId, from.Id, to.Id, request.AmountKobo,
                from.BalanceKobo, to.BalanceKobo, now);

            record.Complete(200, JsonSerializer.Serialize(response));

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitAsync(ct);
            return response;
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PagedResult<TransactionResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 20;

        if (!await _uow.Wallets.ExistsAsync(walletId, ct))
            throw new WalletNotFoundException(walletId);

        var (items, total) = await _uow.Wallets.GetStatementAsync(walletId, page, pageSize, ct);

        var mapped = items
            .Select(t => new TransactionResponse(t.Id, t.Type.ToString(), t.AmountKobo, t.BalanceAfterKobo, t.CounterpartyWalletId, t.Description, t.CreatedAtUtc))
            .ToList();

        return new PagedResult<TransactionResponse>(mapped, page, pageSize, total);
    }

    private static TransferResponse Replay(IdempotencyRecord existing, string requestHash) => existing.Resolve(requestHash) switch
    {
        IdempotencyResolution.Conflict => throw new IdempotencyKeyConflictException(),
        IdempotencyResolution.InProgress => throw new IdempotencyRequestInProgressException(),
        _ => JsonSerializer.Deserialize<TransferResponse>(existing.ResponseBody!)!
    };

    private static string ComputeRequestHash(TransferRequest r)
    {
        var raw = $"{r.FromWalletId}|{r.ToWalletId}|{r.AmountKobo}|{r.Description}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static WalletResponse ToWalletResponse(Wallet w) =>
        new(w.Id, w.CustomerId, w.BalanceKobo, w.Currency, w.CreatedAtUtc);
}
