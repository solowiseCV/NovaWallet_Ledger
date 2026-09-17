using FluentAssertions;
using NovaWallet.Domain.Policies;
using Xunit;

namespace NovaWallet.Domain.Tests.Policies;

public class WatClockTests
{
    [Fact]
    public void GetDayBoundsUtc_ReturnsExactly24Hours()
    {
        var now = new DateTimeOffset(2026, 9, 16, 10, 30, 0, TimeSpan.Zero);

        var (start, end) = WatClock.GetDayBoundsUtc(now);

        (end - start).Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void GetDayBoundsUtc_UsesWatMidnight_NotUtcMidnight()
    {
        // 23:30 UTC on the 15th is already 00:30 WAT on the 16th (UTC+1) —
        // the WAT day has already turned over even though the UTC day hasn't.
        var lateUtc = new DateTimeOffset(2026, 9, 15, 23, 30, 0, TimeSpan.Zero);

        var (start, _) = WatClock.GetDayBoundsUtc(lateUtc);

        // WAT midnight on the 16th is 23:00 UTC on the 15th.
        start.Should().Be(new DateTimeOffset(2026, 9, 15, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetDayBoundsUtc_InstantJustBeforeAndAfterBoundary_FallInDifferentDays()
    {
        var justBeforeMidnightWat = new DateTimeOffset(2026, 9, 15, 22, 59, 59, TimeSpan.Zero); // 23:59:59 WAT on 15th
        var justAfterMidnightWat = new DateTimeOffset(2026, 9, 15, 23, 0, 1, TimeSpan.Zero);     // 00:00:01 WAT on 16th

        var boundsBefore = WatClock.GetDayBoundsUtc(justBeforeMidnightWat);
        var boundsAfter = WatClock.GetDayBoundsUtc(justAfterMidnightWat);

        boundsBefore.StartUtc.Should().NotBe(boundsAfter.StartUtc);
        justBeforeMidnightWat.Should().BeLessThan(boundsBefore.EndUtc);
        justAfterMidnightWat.Should().BeGreaterThanOrEqualTo(boundsAfter.StartUtc);
    }
}
