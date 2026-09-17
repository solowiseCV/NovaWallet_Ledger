using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

/// <summary>
/// Persistence port for wallets, transactions, audit entries, and idempotency
/// records. Application code (WalletService) depends only on this interface —
/// it has no idea Postgres, EF Core, or "SELECT ... FOR UPDATE" exist. All of
/// that is Infrastructure's problem, hidden behind LockAsync/LockOrderedAsync.
/// </summary>
public interface IWalletRepository
{
    Task AddAsync(Wallet wallet, CancellationToken ct);

    /// <summary>Tracked read — use before mutating (Credit/Debit) so changes are
    /// picked up by the surrounding IUnitOfWork.SaveChangesAsync.</summary>
    Task<Wallet?> GetAsync(Guid walletId, CancellationToken ct);

    /// <summary>Untracked read — use for read-only queries (balance lookups, etc.).</summary>
    Task<Wallet?> GetReadOnlyAsync(Guid walletId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid walletId, CancellationToken ct);

    /// <summary>Takes a row-level lock on a single wallet. Must be called inside
    /// an active unit-of-work transaction.</summary>
    Task LockAsync(Guid walletId, CancellationToken ct);

    /// <summary>Locks both wallets in a fixed (ascending id) order, regardless of
    /// the order the ids are supplied in, so two transfers referencing the same
    /// pair of wallets in opposite directions can never deadlock each other.</summary>
    Task LockOrderedAsync(Guid walletId1, Guid walletId2, CancellationToken ct);

    Task<long> GetOutboundTotalTodayAsync(Guid walletId, DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, CancellationToken ct);

    Task AddTransactionAsync(LedgerTransaction transaction, CancellationToken ct);

    Task AddAuditLogAsync(AuditLogEntry entry, CancellationToken ct);

    Task<(IReadOnlyList<LedgerTransaction> Items, long Total)> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct);

    Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct);

    /// <summary>Attempts to insert a new idempotency record. Returns false — rather
    /// than throwing — if another request already holds this key, translating the
    /// database's unique-constraint violation into a plain signal the caller can
    /// branch on without needing to know it came from a constraint at all.</summary>
    Task<bool> TryAddIdempotencyRecordAsync(IdempotencyRecord record, CancellationToken ct);
}
