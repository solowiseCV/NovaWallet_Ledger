using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Services;
using NovaWallet.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NovaWallet.Infrastructure.Tests;

[Collection("Postgres collection")]
public class WalletServiceTests
{
    private readonly PostgresContainerFixture _fixture;

    public WalletServiceTests(PostgresContainerFixture fixture) => _fixture = fixture;

    private WalletService CreateService() =>
        new(TestDbContextFactory.Create(_fixture.ConnectionString), new SystemDateTimeProvider());

    [Fact]
    public async Task CreateWallet_StartsAtZeroBalance()
    {
        var svc = CreateService();

        var wallet = await svc.CreateWalletAsync($"cust-{Guid.NewGuid()}", default);

        wallet.BalanceKobo.Should().Be(0);
        wallet.Currency.Should().Be("NGN");
    }

    [Fact]
    public async Task CreditWallet_IncreasesBalance()
    {
        var svc = CreateService();
        var wallet = await svc.CreateWalletAsync($"cust-{Guid.NewGuid()}", default);

        var updated = await svc.CreditWalletAsync(wallet.WalletId, 10_000, "test credit", wallet.CustomerId, default);

        updated.BalanceKobo.Should().Be(10_000);
    }

    [Fact]
    public async Task Transfer_MovesFundsBetweenWallets()
    {
        var svc = CreateService();
        var a = await svc.CreateWalletAsync($"cust-a-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"cust-b-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 50_000, null, a.CustomerId, default);

        var result = await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 20_000, "rent"),
            Guid.NewGuid().ToString(), a.CustomerId, default);

        result.FromWalletBalanceAfterKobo.Should().Be(30_000);
        result.ToWalletBalanceAfterKobo.Should().Be(20_000);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Throws()
    {
        var svc = CreateService();
        var a = await svc.CreateWalletAsync($"cust-c-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"cust-d-{Guid.NewGuid()}", default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 1_000, null),
            Guid.NewGuid().ToString(), a.CustomerId, default);

        await act.Should().ThrowAsync<InsufficientFundsException>();
    }

    [Fact]
    public async Task Transfer_ExceedingDailyLimit_Throws()
    {
        var svc = CreateService();
        var a = await svc.CreateWalletAsync($"cust-e-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"cust-f-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 1_000_000_000, null, a.CustomerId, default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 50_000_001, null),
            Guid.NewGuid().ToString(), a.CustomerId, default);

        await act.Should().ThrowAsync<DailyLimitExceededException>();
    }

    [Fact]
    public async Task Transfer_ByNonOwner_IsForbidden()
    {
        var svc = CreateService();
        var a = await svc.CreateWalletAsync($"cust-g-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"cust-h-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 50_000, null, a.CustomerId, default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 1_000, null),
            Guid.NewGuid().ToString(), "someone-else", default);

        await act.Should().ThrowAsync<ForbiddenOperationException>();
    }
}
