using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NovaWallet.Infrastructure.Tests;

/// <summary>
/// Confirms the real DI-wired stack (EfWalletRepository + EfUnitOfWork + a real
/// Postgres transaction) produces the same outcomes Application.Tests already
/// proved against the fake — i.e. that the Infrastructure implementation
/// actually satisfies the IWalletRepository/IUnitOfWork contracts.
/// </summary>
[Collection("Postgres collection")]
public class TransferOrchestrationTests
{
    private readonly PostgresContainerFixture _fixture;

    public TransferOrchestrationTests(PostgresContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Transfer_MovesFundsBetweenWallets()
    {
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
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
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
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
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
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
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
        var a = await svc.CreateWalletAsync($"cust-g-{Guid.NewGuid()}", default);
        var b = await svc.CreateWalletAsync($"cust-h-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 50_000, null, a.CustomerId, default);

        var act = async () => await svc.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 1_000, null),
            Guid.NewGuid().ToString(), "someone-else", default);

        await act.Should().ThrowAsync<ForbiddenOperationException>();
    }

    [Fact]
    public async Task Statement_ReturnsPagedTransactions_NewestFirst()
    {
        var (svc, _) = WalletServiceFactory.Create(_fixture.ConnectionString);
        var a = await svc.CreateWalletAsync($"cust-m-{Guid.NewGuid()}", default);
        await svc.CreditWalletAsync(a.WalletId, 10_000, "first", a.CustomerId, default);
        await svc.CreditWalletAsync(a.WalletId, 20_000, "second", a.CustomerId, default);

        var page = await svc.GetStatementAsync(a.WalletId, page: 1, pageSize: 20, default);

        page.TotalCount.Should().Be(2);
        page.Items.First().Description.Should().Be("second"); // newest first
    }
}
