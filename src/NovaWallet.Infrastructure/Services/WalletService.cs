using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Domain.Services;

namespace NovaWallet.Infrastructure.Services;

/// <summary>
/// Core ledger logic. Concurrency safety strategy:
///   1. Every balance-mutating operation runs inside a single DB transaction.
///   2. Before reading a wallet's balance, we take a row-level lock with
///      "SELECT ... FOR UPDATE". Concurrent requests against the same wallet
///      therefore serialize at the database, not in application memory —
///      this holds even across multiple app instances/pods, unlike an
///      in-process lock (e.g. `lock` or SemaphoreSlim) would.
///   3. Transfers lock both wallets in a fixed order (ascending Guid) to
///      prevent deadlocks between two transfers that reference the same
///      pair of wallets in opposite directions.
///   4. The balance check and the write happen inside that same locked
///      transaction, so "check-then-act" cannot race.
/// </summary>
public class WalletService : IWalletService
{
    private readonly NovaWalletDbContext _db;
    private readonly IDateTimeProvider _clock;

    private static readonly TimeSpan WatOffset = TimeSpan.FromHours(1); // Africa/Lagos — no DST

    public WalletService(NovaWalletDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<WalletResponse> CreateWalletAsync(string customerId, CancellationToken ct)
    {
        var wallet = Wallet.Create(customerId, _clock.UtcNow);

        _db.Wallets.Add(wallet);
        _db.AuditLogs.Add(new AuditLogEntry
        {
            WalletId = wallet.Id,
            Action = "CREATE_WALLET",
            BalanceBeforeKobo = 0,
            BalanceAfterKobo = 0,
            ActorId = customerId,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return ToWalletResponse(wallet);
    }

    public async Task<WalletResponse> GetWalletAsync(Guid walletId, CancellationToken ct)
    {
        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId, ct)
            ?? throw new WalletNotFoundException(walletId);
        return ToWalletResponse(wallet);
    }

    public async Task<WalletResponse> CreditWalletAsync(Guid walletId, long amountKobo, string? description, string actorId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Row lock: any concurrent credit/debit against this wallet queues behind us.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM wallets WHERE id = {walletId} FOR UPDATE", ct);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId, ct)
            ?? throw new WalletNotFoundException(walletId);

        var before = wallet.BalanceKobo;
        wallet.Credit(amountKobo);

        _db.LedgerTransactions.Add(new LedgerTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            AmountKobo = amountKobo,
            BalanceAfterKobo = wallet.BalanceKobo,
            Description = description ?? "Inbound NIP credit",
            CreatedAtUtc = _clock.UtcNow
        });

        _db.AuditLogs.Add(new AuditLogEntry
        {
            WalletId = wallet.Id,
            Action = "CREDIT",
            AmountKobo = amountKobo,
            BalanceBeforeKobo = before,
            BalanceAfterKobo = wallet.BalanceKobo,
            ActorId = actorId,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToWalletResponse(wallet);
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, string actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidTransferException("Idempotency-Key header is required.");
        WalletTransferPolicy.EnsurePositiveAmount(request.AmountKobo);
        WalletTransferPolicy.EnsureDistinctWallets(request.FromWalletId, request.ToWalletId);

        var requestHash = ComputeRequestHash(request);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // --- Idempotency guard -------------------------------------------------
        // We rely on the unique constraint on IdempotencyRecord.Key, not an
        // existence check, to close the race where two requests with the same
        // key arrive at (almost) the same instant. See AI_USAGE.md for why.
        var existing = await _db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey, ct);

        if (existing is not null)
        {
            await tx.RollbackAsync(ct);
            return ReplayOrThrow(existing, requestHash);
        }

        var record = new IdempotencyRecord
        {
            Key = idempotencyKey,
            RequestHash = requestHash,
            Status = IdempotencyStatus.Processing,
            CreatedAtUtc = _clock.UtcNow
        };

        try
        {
            _db.IdempotencyRecords.Add(record);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the insert race to a concurrent request using the same key.
            await tx.RollbackAsync(ct);
            var raceWinner = await _db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey, ct);
            if (raceWinner is null) throw;
            return ReplayOrThrow(raceWinner, requestHash);
        }

        // --- Lock both wallets in a fixed (ascending) order to avoid deadlocks --
        var orderedIds = new[] { request.FromWalletId, request.ToWalletId }.OrderBy(g => g).ToArray();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM wallets WHERE id = ANY({orderedIds}) ORDER BY id FOR UPDATE", ct);

        var from = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == request.FromWalletId, ct)
            ?? throw new WalletNotFoundException(request.FromWalletId);
        var to = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == request.ToWalletId, ct)
            ?? throw new WalletNotFoundException(request.ToWalletId);

        from.EnsureOwnedBy(actorId);

        var (watDayStartUtc, watDayEndUtc) = GetWatDayBoundsUtc(_clock.UtcNow);

        var alreadySentTodayKobo = await _db.LedgerTransactions
            .Where(t => t.WalletId == from.Id
                        && t.Type == TransactionType.TransferOut
                        && t.CreatedAtUtc >= watDayStartUtc
                        && t.CreatedAtUtc < watDayEndUtc)
            .SumAsync(t => (long?)t.AmountKobo, ct) ?? 0;

        WalletTransferPolicy.EnsureWithinDailyOutboundLimit(alreadySentTodayKobo, request.AmountKobo);

        var fromBefore = from.BalanceKobo;
        var toBefore = to.BalanceKobo;

        from.Debit(request.AmountKobo);
        to.Credit(request.AmountKobo);

        var now = _clock.UtcNow;
        var transferGroupId = Guid.NewGuid();

        _db.LedgerTransactions.Add(new LedgerTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = from.Id,
            Type = TransactionType.TransferOut,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = from.BalanceKobo,
            CounterpartyWalletId = to.Id,
            IdempotencyKey = idempotencyKey,
            Description = request.Description,
            CreatedAtUtc = now
        });
        _db.LedgerTransactions.Add(new LedgerTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = to.Id,
            Type = TransactionType.TransferIn,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = to.BalanceKobo,
            CounterpartyWalletId = from.Id,
            IdempotencyKey = idempotencyKey,
            Description = request.Description,
            CreatedAtUtc = now
        });

        var auditMetadata = JsonSerializer.Serialize(new { idempotencyKey, transferGroupId });

        _db.AuditLogs.Add(new AuditLogEntry
        {
            WalletId = from.Id, Action = "TRANSFER_DEBIT", AmountKobo = request.AmountKobo,
            BalanceBeforeKobo = fromBefore, BalanceAfterKobo = from.BalanceKobo,
            ActorId = actorId, Metadata = auditMetadata, TimestampUtc = now
        });
        _db.AuditLogs.Add(new AuditLogEntry
        {
            WalletId = to.Id, Action = "TRANSFER_CREDIT", AmountKobo = request.AmountKobo,
            BalanceBeforeKobo = toBefore, BalanceAfterKobo = to.BalanceKobo,
            ActorId = actorId, Metadata = auditMetadata, TimestampUtc = now
        });

        var response = new TransferResponse(
            transferGroupId, from.Id, to.Id, request.AmountKobo,
            from.BalanceKobo, to.BalanceKobo, now);

        // `record` is already tracked (added earlier in this method), so mutating
        // its properties is enough — EF Core will include the final values in the
        // same INSERT when we SaveChanges below. Calling Update() here would be
        // redundant and risks confusing the tracker's Added state.
        record.Status = IdempotencyStatus.Completed;
        record.ResponseStatusCode = 200;
        record.ResponseBody = JsonSerializer.Serialize(response);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return response;
    }

    public async Task<PagedResult<TransactionResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 20;

        var walletExists = await _db.Wallets.AnyAsync(w => w.Id == walletId, ct);
        if (!walletExists) throw new WalletNotFoundException(walletId);

        var query = _db.LedgerTransactions.AsNoTracking().Where(t => t.WalletId == walletId);
        var total = await query.LongCountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionResponse(
                t.Id, t.Type.ToString(), t.AmountKobo, t.BalanceAfterKobo,
                t.CounterpartyWalletId, t.Description, t.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<TransactionResponse>(items, page, pageSize, total);
    }

    private static TransferResponse ReplayOrThrow(IdempotencyRecord existing, string requestHash)
    {
        if (existing.RequestHash != requestHash)
            throw new IdempotencyKeyConflictException();

        if (existing.Status == IdempotencyStatus.Processing)
            throw new IdempotencyRequestInProgressException();

        return JsonSerializer.Deserialize<TransferResponse>(existing.ResponseBody!)!;
    }

    private static string ComputeRequestHash(TransferRequest r)
    {
        var raw = $"{r.FromWalletId}|{r.ToWalletId}|{r.AmountKobo}|{r.Description}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Returns the [start, end) UTC instant range for "today" in WAT (UTC+1, no DST).</summary>
    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetWatDayBoundsUtc(DateTimeOffset nowUtc)
    {
        var nowWat = nowUtc.ToOffset(WatOffset);
        var startWat = new DateTimeOffset(nowWat.Year, nowWat.Month, nowWat.Day, 0, 0, 0, WatOffset);
        return (startWat.ToUniversalTime(), startWat.AddDays(1).ToUniversalTime());
    }

    private static WalletResponse ToWalletResponse(Wallet w) =>
        new(w.Id, w.CustomerId, w.BalanceKobo, w.Currency, w.CreatedAtUtc);
}
