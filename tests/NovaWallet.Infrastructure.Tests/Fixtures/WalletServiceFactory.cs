using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Application.Services;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.Infrastructure.Services;

namespace NovaWallet.Infrastructure.Tests.Fixtures;

/// <summary>
/// Wires up the real Infrastructure implementations (EfWalletRepository,
/// EfUnitOfWork) against a real Postgres connection string, exactly the way
/// Program.cs does it — so these tests prove the actual DI-wired stack works,
/// not just the WalletService logic in isolation (Application.Tests already
/// covers that with a fake).
/// </summary>
internal static class WalletServiceFactory
{
    public static (WalletService Service, NovaWalletDbContext Db) Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var db = new NovaWalletDbContext(options);
        db.Database.EnsureCreated();

        IWalletRepository repository = new EfWalletRepository(db);
        IUnitOfWork uow = new EfUnitOfWork(db, repository);
        IDateTimeProvider clock = new SystemDateTimeProvider();

        return (new WalletService(uow, clock), db);
    }
}
