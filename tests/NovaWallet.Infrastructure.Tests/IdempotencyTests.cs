using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Services;
using NovaWallet.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NovaWallet.Infrastructure.Tests;

[Collection("Postgres collection")]
public class IdempotencyTests
{
    private readonly PostgresContainerFixture _fixture;

    public IdempotencyTests(PostgresContainerFixture fixture) => _fixture = fixture;

    private WalletService CreateService() =>
        new(TestDbContextFactory.Create(_fixture.ConnectionString), new SystemDateTimeProvider());

    [Fact]
    public async Task ReplayingSameKey_DoesNotDoubleProcess()
    {
        var svc = CreateService();
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
        var svc = CreateService();
        var a = await svc.CreateWalletAsync($"idem-c-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"idem-d-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 100_000, null, a.CustomerId, default);

        var key = Guid.NewGuid().ToString();
        await svc.TransferAsync(new TransferRequest(a.WalletId, b.WalletId, 10_000, null), key, a.CustomerId, default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 20_000, null), key, a.CustomerId, default);

        await act.Should().ThrowAsync<IdempotencyKeyConflictException>();
    }
}
