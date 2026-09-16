using Microsoft.EntityFrameworkCore;
using NovaWallet.Infrastructure;

namespace NovaWallet.Infrastructure.Tests.Fixtures;

internal static class TestDbContextFactory
{
    public static NovaWalletDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var context = new NovaWalletDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
