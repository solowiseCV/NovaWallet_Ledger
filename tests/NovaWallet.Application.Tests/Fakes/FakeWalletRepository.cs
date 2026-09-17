using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;

namespace NovaWallet.Application.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for IWalletRepository. Deliberately a fake, not a mock:
/// these tests care about the resulting state (balances, ledger rows) after a use
/// case runs, not about which methods were called in which order, so a small
/// object that actually behaves like a repository is a better fit than recording
/// and verifying call expectations would be. It doesn't attempt real row-locking —
/// there's nothing concurrent in-process here. Real lock behaviour against
/// Postgres is proven in NovaWallet.Infrastructure.Tests, where it matters.
/// </summary>
public class FakeWalletRepository : IWalletRepository
{
    private readonly Dictionary<Guid, Wallet> _wallets = new();
    private readonly List<LedgerTransaction> _transactions = new();
    private readonly List<AuditLogEntry> _auditLogs = new();
    private readonly Dictionary<string, IdempotencyRecord> _idempotencyRecords = new();

    public IReadOnlyList<LedgerTransaction> Transactions => _transactions;
    public IReadOnlyList<AuditLogEntry> AuditLogs => _auditLogs;

    public Task AddAsync(Wallet wallet, CancellationToken ct)
    {
        _wallets[wallet.Id] = wallet;
        return Task.CompletedTask;
    }

    public Task<Wallet?> GetAsync(Guid walletId, CancellationToken ct) =>
        Task.FromResult(_wallets.GetValueOrDefault(walletId));

    public Task<Wallet?> GetReadOnlyAsync(Guid walletId, CancellationToken ct) =>
        Task.FromResult(_wallets.GetValueOrDefault(walletId));

    public Task<bool> ExistsAsync(Guid walletId, CancellationToken ct) =>
        Task.FromResult(_wallets.ContainsKey(walletId));

    public Task LockAsync(Guid walletId, CancellationToken ct) => Task.CompletedTask;

    public Task LockOrderedAsync(Guid walletId1, Guid walletId2, CancellationToken ct) => Task.CompletedTask;

    public Task<long> GetOutboundTotalTodayAsync(Guid walletId, DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, CancellationToken ct)
    {
        var total = _transactions
            .Where(t => t.WalletId == walletId
                        && t.Type == TransactionType.TransferOut
                        && t.CreatedAtUtc >= dayStartUtc
                        && t.CreatedAtUtc < dayEndUtc)
            .Sum(t => t.AmountKobo);

        return Task.FromResult(total);
    }

    public Task AddTransactionAsync(LedgerTransaction transaction, CancellationToken ct)
    {
        _transactions.Add(transaction);
        return Task.CompletedTask;
    }

    public Task AddAuditLogAsync(AuditLogEntry entry, CancellationToken ct)
    {
        _auditLogs.Add(entry);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<LedgerTransaction> Items, long Total)> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        var all = _transactions.Where(t => t.WalletId == walletId).OrderByDescending(t => t.CreatedAtUtc).ToList();
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<LedgerTransaction>)pageItems, (long)all.Count));
    }

    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct) =>
        Task.FromResult(_idempotencyRecords.GetValueOrDefault(key));

    public Task<bool> TryAddIdempotencyRecordAsync(IdempotencyRecord record, CancellationToken ct)
    {
        if (_idempotencyRecords.ContainsKey(record.Key))
            return Task.FromResult(false);

        _idempotencyRecords[record.Key] = record;
        return Task.FromResult(true);
    }
}
