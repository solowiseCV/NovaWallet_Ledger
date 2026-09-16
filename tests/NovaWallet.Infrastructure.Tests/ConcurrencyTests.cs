using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Services;
using NovaWallet.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NovaWallet.Infrastructure.Tests;

/// <summary>
/// The test the panel specifically asked for: fires many concurrent transfer
/// requests against the SAME source wallet and asserts the balance never goes
/// negative and exactly as many transfers succeed as the starting balance allows
/// (no double-spend, no lost update).
/// </summary>
[Collection("Postgres collection")]
public class ConcurrencyTests
{
    private readonly PostgresContainerFixture _fixture;

    public ConcurrencyTests(PostgresContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ConcurrentTransfers_NeverAllowNegativeBalance_OrDoubleSpend()
    {
        var setupDb = TestDbContextFactory.Create(_fixture.ConnectionString);
        var setupSvc = new WalletService(setupDb, new SystemDateTimeProvider());

        var source = await setupSvc.CreateWalletAsync($"conc-src-{Guid.NewGuid()}", default);
        var sink = await setupSvc.CreateWalletAsync($"conc-sink-{Guid.NewGuid()}", default);

        const long startingBalance = 100_000; // NGN 1,000.00
        const long transferAmount = 10_000;   // NGN 100.00
        const int attempts = 20;              // 20 x NGN 100 against a NGN 1,000 balance -> exactly 10 should succeed

        await setupSvc.CreditWalletAsync(source.WalletId, startingBalance, null, source.CustomerId, default);

        var tasks = Enumerable.Range(0, attempts).Select(async _ =>
        {
            await using var db = TestDbContextFactory.Create(_fixture.ConnectionString);
            var svc = new WalletService(db, new SystemDateTimeProvider());
            try
            {
                await svc.TransferAsync(
                    new TransferRequest(source.WalletId, sink.WalletId, transferAmount, "concurrent test"),
                    Guid.NewGuid().ToString(),
                    source.CustomerId,
                    default);
                return true;
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);

        successCount.Should().Be((int)(startingBalance / transferAmount));

        var finalSource = await setupSvc.GetWalletAsync(source.WalletId, default);
        var finalSink = await setupSvc.GetWalletAsync(sink.WalletId, default);

        finalSource.BalanceKobo.Should().BeGreaterThanOrEqualTo(0);
        finalSource.BalanceKobo.Should().Be(startingBalance - successCount * transferAmount);
        finalSink.BalanceKobo.Should().Be(successCount * transferAmount);
    }
}
