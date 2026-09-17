namespace NovaWallet.Application.Interfaces;

/// <summary>
/// Transaction boundary for a use case. WalletService begins a transaction,
/// does its reads/locks/writes through Wallets, then commits — or rolls back
/// on any failure. Implemented in Infrastructure against a real EF Core
/// transaction; Application never touches ADO.NET/EF types directly.
/// </summary>
public interface IUnitOfWork
{
    IWalletRepository Wallets { get; }

    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
