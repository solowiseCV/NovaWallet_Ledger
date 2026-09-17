using FluentAssertions;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Services;
using NovaWallet.Application.Tests.Fakes;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Application.Tests;

/// <summary>
/// Tests WalletService's orchestration logic — the sequencing of lock, check,
/// mutate, persist — against an in-memory fake repository. No database, no
/// Postgres-specific behaviour is exercised here (that's Infrastructure.Tests'
/// job); this project answers a different question: given a repository that
/// behaves correctly, does the use-case logic do the right thing?
/// </summary>
public class WalletServiceTests
{
    private static (WalletService Service, FakeWalletRepository Repo, FakeUnitOfWork Uow, FakeDateTimeProvider Clock) CreateSut()
    {
        var repo = new FakeWalletRepository();
        var uow = new FakeUnitOfWork(repo);
        var clock = new FakeDateTimeProvider();
        return (new WalletService(uow, clock), repo, uow, clock);
    }

    [Fact]
    public async Task CreateWallet_StartsAtZeroBalance_AndWritesAnAuditEntry()
    {
        var (service, repo, _, _) = CreateSut();

        var wallet = await service.CreateWalletAsync("cust-001", default);

        wallet.BalanceKobo.Should().Be(0);
        repo.AuditLogs.Should().ContainSingle(a => a.Action == "CREATE_WALLET" && a.WalletId == wallet.WalletId);
    }

    [Fact]
    public async Task CreditWallet_IncreasesBalance_WithinATransaction()
    {
        var (service, _, uow, _) = CreateSut();
        var wallet = await service.CreateWalletAsync("cust-001", default);

        var updated = await service.CreditWalletAsync(wallet.WalletId, 10_000, "salary", "cust-001", default);

        updated.BalanceKobo.Should().Be(10_000);
        uow.CommitCount.Should().Be(1);
        uow.RollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task Transfer_MovesFundsBetweenWallets()
    {
        var (service, _, _, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-a", default);
        var b = await service.CreateWalletAsync("cust-b", default);
        await service.CreditWalletAsync(a.WalletId, 50_000, null, "cust-a", default);

        var result = await service.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 20_000, "rent"), Guid.NewGuid().ToString(), "cust-a", default);

        result.FromWalletBalanceAfterKobo.Should().Be(30_000);
        result.ToWalletBalanceAfterKobo.Should().Be(20_000);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_RollsBackAndThrows()
    {
        var (service, _, uow, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-c", default);
        var b = await service.CreateWalletAsync("cust-d", default);

        var act = async () => await service.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 1_000, null), Guid.NewGuid().ToString(), "cust-c", default);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        uow.RollbackCount.Should().Be(1);
        uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Transfer_ExceedingDailyLimit_Throws()
    {
        var (service, _, _, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-e", default);
        var b = await service.CreateWalletAsync("cust-f", default);
        await service.CreditWalletAsync(a.WalletId, 1_000_000_000, null, "cust-e", default);

        var act = async () => await service.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 50_000_001, null), Guid.NewGuid().ToString(), "cust-e", default);

        await act.Should().ThrowAsync<DailyLimitExceededException>();
    }

    [Fact]
    public async Task Transfer_ByNonOwner_IsForbidden()
    {
        var (service, _, _, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-g", default);
        var b = await service.CreateWalletAsync("cust-h", default);
        await service.CreditWalletAsync(a.WalletId, 50_000, null, "cust-g", default);

        var act = async () => await service.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 1_000, null), Guid.NewGuid().ToString(), "someone-else", default);

        await act.Should().ThrowAsync<ForbiddenOperationException>();
    }

    [Fact]
    public async Task Transfer_ReplayingSameKey_ReturnsSameResult_WithoutDoubleDebiting()
    {
        var (service, _, _, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-i", default);
        var b = await service.CreateWalletAsync("cust-j", default);
        await service.CreditWalletAsync(a.WalletId, 100_000, null, "cust-i", default);

        var key = Guid.NewGuid().ToString();
        var request = new TransferRequest(a.WalletId, b.WalletId, 10_000, "rent");

        var first = await service.TransferAsync(request, key, "cust-i", default);
        var second = await service.TransferAsync(request, key, "cust-i", default);

        first.Should().BeEquivalentTo(second);

        var balance = await service.GetWalletAsync(a.WalletId, default);
        balance.BalanceKobo.Should().Be(90_000);
    }

    [Fact]
    public async Task Transfer_ReusingKeyWithDifferentPayload_IsRejected()
    {
        var (service, _, _, _) = CreateSut();
        var a = await service.CreateWalletAsync("cust-k", default);
        var b = await service.CreateWalletAsync("cust-l", default);
        await service.CreditWalletAsync(a.WalletId, 100_000, null, "cust-k", default);

        var key = Guid.NewGuid().ToString();
        await service.TransferAsync(new TransferRequest(a.WalletId, b.WalletId, 10_000, null), key, "cust-k", default);

        var act = async () => await service.TransferAsync(
            new TransferRequest(a.WalletId, b.WalletId, 20_000, null), key, "cust-k", default);

        await act.Should().ThrowAsync<IdempotencyKeyConflictException>();
    }

    [Fact]
    public async Task GetStatement_UnknownWallet_Throws()
    {
        var (service, _, _, _) = CreateSut();

        var act = async () => await service.GetStatementAsync(Guid.NewGuid(), 1, 20, default);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }
}
