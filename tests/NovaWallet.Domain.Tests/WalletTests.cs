using FluentAssertions;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Domain.Services;
using Xunit;

namespace NovaWallet.Domain.Tests;

public class WalletTests
{
    [Fact]
    public void CreditAndDebit_MutateBalanceThroughDomainMethods()
    {
        var wallet = Wallet.Create("customer-1", DateTimeOffset.UtcNow);

        wallet.Credit(20_000);
        wallet.Debit(7_500);

        wallet.BalanceKobo.Should().Be(12_500);
    }

    [Fact]
    public void Debit_WhenFundsAreInsufficient_Throws()
    {
        var wallet = Wallet.Create("customer-1", DateTimeOffset.UtcNow);

        var act = () => wallet.Debit(1);

        act.Should().Throw<InsufficientFundsException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_WhenAmountIsNotPositive_Throws(long amountKobo)
    {
        var wallet = Wallet.Create("customer-1", DateTimeOffset.UtcNow);

        var act = () => wallet.Credit(amountKobo);

        act.Should().Throw<InvalidTransferException>();
    }

    [Fact]
    public void DailyLimit_WhenExceeded_Throws()
    {
        var act = () => WalletTransferPolicy.EnsureWithinDailyOutboundLimit(
            WalletTransferPolicy.DailyOutboundLimitKobo, 1);

        act.Should().Throw<DailyLimitExceededException>();
    }
}
