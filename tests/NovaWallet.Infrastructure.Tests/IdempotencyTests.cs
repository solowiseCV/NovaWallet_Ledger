using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NovaWallet.Infrastructure.Tests;

/// <summary>
/// Proves the unique-constraint-backed idempotency mechanism actually works
/// against real Postgres — Application.Tests proves the branching logic against
/// a fake, this proves the database-level guarantee it depends on is real.
/// </summary>
[Collection("Postgres collection")]
public class IdempotencyTests
{
    private readonly PostgresContainerFixture _fixture;

    public IdempotencyTests(PostgresContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ReplayingSameKey_DoesNotDoubleProcess()
    {
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
        var a = await svc.CreateWalletAsync($"idem-a-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"idem-b-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 100_000, null, a.CustomerId, default);

        var key = Guid.NewGuid().ToString();
        var request = new TransferRequest(a.WalletId, b.WalletId, 10_000, "rent");

        var first = await svc.TransferAsync(request, key, a.CustomerId, default);
        var second = await svc.TransferAsync(request, key, a.CustomerId, default);

        first.Should().BeEquivalentTo(second);

        var balance = await svc.GetWalletAsync(a.WalletId, default);
        balance.BalanceKobo.Should().Be(90_000); // debited exactly once, not twice
    }

    [Fact]
    public async Task ReusingKey_WithDifferentPayload_IsRejected()
    {
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
        var a = await svc.CreateWalletAsync($"idem-c-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"idem-d-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 100_000, null, a.CustomerId, default);

        var key = Guid.NewGuid().ToString();
        await svc.TransferAsync(new TransferRequest(a.WalletId, b.WalletId, 10_000, null), key, a.CustomerId, default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 20_000, null), key, a.CustomerId, default);

        await act.Should().ThrowAsync<IdempotencyKeyConflictException>();
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_DebitExactlyOnce()
    {
        // The scenario the unique constraint specifically exists for: several
        // requests with the SAME key and SAME payload racing each other.
        // A racer that arrives while another is still mid-flight legitimately
        // gets IdempotencyRequestInProgressException (409) rather than guessing —
        // that's a correct outcome, not a test failure. What must hold regardless
        // is: every racer that *does* get a result gets the same one, and the
        // wallet is debited exactly once no matter how many racers there were.
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
        var a = await svc.CreateWalletAsync($"idem-race-a-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"idem-race-b-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 100_000, null, a.CustomerId, default);

        var key = Guid.NewGuid().ToString();
        var request = new TransferRequest(a.WalletId, b.WalletId, 10_000, "race");

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            var (racerSvc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
            try
            {
                return (Response: (TransferResponse?)await racerSvc.TransferAsync(request, key, a.CustomerId, default), TimedOut: false);
            }
            catch (IdempotencyRequestInProgressException)
            {
                return (Response: (TransferResponse?)null, TimedOut: true);
            }
        });

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Where(r => r.Response is not null).Select(r => r.Response!).ToList();

        succeeded.Should().NotBeEmpty();
        succeeded.Should().AllSatisfy(r => r.Should().BeEquivalentTo(succeeded[0]));

        var balance = await svc.GetWalletAsync(a.WalletId, default);
        balance.BalanceKobo.Should().Be(90_000); // debited exactly once, no matter how many racers hit it
    }
}
