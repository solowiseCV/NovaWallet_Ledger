using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly NovaWalletDbContext _db;
    private IDbContextTransaction? _transaction;

    public EfUnitOfWork(NovaWalletDbContext db, IWalletRepository walletRepository)
    {
        _db = db;
        Wallets = walletRepository;
    }

    public IWalletRepository Wallets { get; }

    public async Task BeginTransactionAsync(CancellationToken ct) =>
        _transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
