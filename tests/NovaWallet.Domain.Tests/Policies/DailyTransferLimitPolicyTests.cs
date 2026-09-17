using FluentAssertions;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Domain.Policies;
using Xunit;

namespace NovaWallet.Domain.Tests.Policies;

public class DailyTransferLimitPolicyTests
{
    [Fact]
    public void EnsureWithinLimit_Allows_WhenWellUnderLimit()
    {
        var act = () => DailyTransferLimitPolicy.EnsureWithinLimit(alreadySentTodayKobo: 0, amountKobo: 10_000);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureWithinLimit_Allows_WhenExactlyAtLimit()
    {
        var act = () => DailyTransferLimitPolicy.EnsureWithinLimit(
            alreadySentTodayKobo: 0, amountKobo: DailyTransferLimitPolicy.LimitKobo);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureWithinLimit_Throws_WhenOneKoboOverLimit()
    {
        var act = () => DailyTransferLimitPolicy.EnsureWithinLimit(
            alreadySentTodayKobo: 0, amountKobo: DailyTransferLimitPolicy.LimitKobo + 1);
        act.Should().Throw<DailyLimitExceededException>();
    }

    [Fact]
    public void EnsureWithinLimit_Throws_WhenCombinedWithPriorTransfersExceedsLimit()
    {
        var act = () => DailyTransferLimitPolicy.EnsureWithinLimit(
            alreadySentTodayKobo: DailyTransferLimitPolicy.LimitKobo - 5_000, amountKobo: 10_000);
        act.Should().Throw<DailyLimitExceededException>();
    }
}
