using NovaWallet.Application.Interfaces;

namespace NovaWallet.Application.Tests.Fakes;

public class FakeUnitOfWork : IUnitOfWork
{
    public FakeUnitOfWork(FakeWalletRepository wallets) => Wallets = wallets;

    public IWalletRepository Wallets { get; }

    public int BeginCount { get; private set; }
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    public Task BeginTransactionAsync(CancellationToken ct) { BeginCount++; return Task.CompletedTask; }
    public Task CommitAsync(CancellationToken ct) { CommitCount++; return Task.CompletedTask; }
    public Task RollbackAsync(CancellationToken ct) { RollbackCount++; return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
