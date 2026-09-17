using FluentAssertions;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Domain.Tests.Entities;

/// <summary>
/// No database, no mocks, no async waiting on anything — these run in
/// milliseconds and pin down the one invariant the whole service exists to
/// protect: a wallet can never go negative.
/// </summary>
public class WalletTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsAtZeroBalance()
    {
        var wallet = Wallet.Create("cust-001", Now);

        wallet.BalanceKobo.Should().Be(0);
        wallet.Currency.Should().Be("NGN");
        wallet.CustomerId.Should().Be("cust-001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsBlankCustomerId(string? customerId)
    {
        var act = () => Wallet.Create(customerId!, Now);
        act.Should().Throw<InvalidTransferException>();
    }

    [Fact]
    public void Credit_IncreasesBalance()
    {
        var wallet = Wallet.Create("cust-001", Now);

        wallet.Credit(10_000);

        wallet.BalanceKobo.Should().Be(10_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_RejectsNonPositiveAmounts(long amount)
    {
        var wallet = Wallet.Create("cust-001", Now);

        var act = () => wallet.Credit(amount);

        act.Should().Throw<InvalidTransferException>();
    }

    [Fact]
    public void Debit_DecreasesBalance_WhenSufficientFunds()
    {
        var wallet = Wallet.Create("cust-001", Now);
        wallet.Credit(50_000);

        wallet.Debit(20_000);

        wallet.BalanceKobo.Should().Be(30_000);
    }

    [Fact]
    public void Debit_NeverAllowsNegativeBalance()
    {
        var wallet = Wallet.Create("cust-001", Now);
        wallet.Credit(10_000);

        var act = () => wallet.Debit(10_001);

        act.Should().Throw<InsufficientFundsException>();
        wallet.BalanceKobo.Should().Be(10_000); // unchanged — the failed debit didn't partially apply
    }

    [Fact]
    public void Debit_ExactBalance_LeavesZero()
    {
        var wallet = Wallet.Create("cust-001", Now);
        wallet.Credit(10_000);

        wallet.Debit(10_000);

        wallet.BalanceKobo.Should().Be(0);
    }

    [Fact]
    public void EnsureOwnedBy_Throws_WhenCallerIsNotTheOwner()
    {
        var wallet = Wallet.Create("cust-001", Now);

        var act = () => wallet.EnsureOwnedBy("someone-else");

        act.Should().Throw<ForbiddenOperationException>();
    }

    [Fact]
    public void EnsureOwnedBy_Succeeds_WhenCallerIsTheOwner()
    {
        var wallet = Wallet.Create("cust-001", Now);

        var act = () => wallet.EnsureOwnedBy("cust-001");

        act.Should().NotThrow();
    }
}
