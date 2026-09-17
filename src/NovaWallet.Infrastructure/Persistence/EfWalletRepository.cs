using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;

namespace NovaWallet.Infrastructure.Persistence;

/// <summary>
/// EF Core / Postgres implementation of IWalletRepository. This is the only
/// place in the whole solution that knows "FOR UPDATE" or a unique-constraint
/// violation exist — Application code just sees LockAsync() and a bool.
/// </summary>
public class EfWalletRepository : IWalletRepository
{
    private readonly NovaWalletDbContext _db;

    public EfWalletRepository(NovaWalletDbContext db) => _db = db;

    public async Task AddAsync(Wallet wallet, CancellationToken ct) =>
        await _db.Wallets.AddAsync(wallet, ct);

    public Task<Wallet?> GetAsync(Guid walletId, CancellationToken ct) =>
        _db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId, ct);

    public Task<Wallet?> GetReadOnlyAsync(Guid walletId, CancellationToken ct) =>
        _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId, ct);

    public Task<bool> ExistsAsync(Guid walletId, CancellationToken ct) =>
        _db.Wallets.AnyAsync(w => w.Id == walletId, ct);

    public Task LockAsync(Guid walletId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM wallets WHERE id = {walletId} FOR UPDATE", ct);

    public Task LockOrderedAsync(Guid walletId1, Guid walletId2, CancellationToken ct)
    {
        var ordered = new[] { walletId1, walletId2 }.OrderBy(g => g).ToArray();
        return _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM wallets WHERE id = ANY({ordered}) ORDER BY id FOR UPDATE", ct);
    }

    public async Task<long> GetOutboundTotalTodayAsync(Guid walletId, DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, CancellationToken ct)
    {
        var total = await _db.LedgerTransactions
            .Where(t => t.WalletId == walletId
                        && t.Type == TransactionType.TransferOut
                        && t.CreatedAtUtc >= dayStartUtc
                        && t.CreatedAtUtc < dayEndUtc)
            .SumAsync(t => (long?)t.AmountKobo, ct);

        return total ?? 0;
    }

    public async Task AddTransactionAsync(LedgerTransaction transaction, CancellationToken ct) =>
        await _db.LedgerTransactions.AddAsync(transaction, ct);

    public async Task AddAuditLogAsync(AuditLogEntry entry, CancellationToken ct) =>
        await _db.AuditLogs.AddAsync(entry, ct);

    public async Task<(IReadOnlyList<LedgerTransaction> Items, long Total)> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.LedgerTransactions.AsNoTracking().Where(t => t.WalletId == walletId);
        var total = await query.LongCountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct) =>
        _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key, ct);

    public async Task<bool> TryAddIdempotencyRecordAsync(IdempotencyRecord record, CancellationToken ct)
    {
        try
        {
            await _db.IdempotencyRecords.AddAsync(record, ct);
            // Flush immediately (rather than waiting for the caller's later
            // SaveChanges) so the unique-constraint check happens right here and
            // we can translate a violation into `false` instead of leaking a
            // DbUpdateException into Application code.
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }
}
