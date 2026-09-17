using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaWallet.Api.Tests.Fakes;
using NovaWallet.Application.Interfaces;
using NovaWallet.Infrastructure;

namespace NovaWallet.Api.Tests.Fixtures;

/// <summary>
/// Boots the real API pipeline (real middleware, real JWT auth, real Swagger,
/// real Problem Details mapping) but swaps out the two dependencies that would
/// otherwise require a live Postgres instance:
///   - NovaWalletDbContext -> EF Core's InMemory provider, purely so
///     Program.cs's startup-time `EnsureCreated()` succeeds. No test here
///     asserts anything about persistence; that's Infrastructure.Tests' job.
///   - IWalletService -> StubWalletService, so each test can dictate exactly
///     what the "business logic" returns or throws and assert on how the API
///     layer (routing, status codes, Problem Details shape) reacts to it.
/// </summary>
public class NovaWalletApiFactory : WebApplicationFactory<Program>
{
    public StubWalletService WalletServiceStub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["Jwt:Issuer"] = "novawallet-mock-issuer",
                ["Jwt:Audience"] = "novawallet-api",
                ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-characters!!"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<NovaWalletDbContext>>();
            services.AddDbContext<NovaWalletDbContext>(opt => opt.UseInMemoryDatabase("novawallet-api-tests"));

            services.RemoveAll<IWalletService>();
            services.AddSingleton<IWalletService>(WalletServiceStub);
        });
    }
}
