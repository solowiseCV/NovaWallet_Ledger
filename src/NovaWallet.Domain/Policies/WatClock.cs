namespace NovaWallet.Domain.Policies;

/// <summary>
/// Pure helper for WAT (Africa/Lagos, UTC+1, no DST) day boundaries. The daily
/// transfer limit resets at local midnight, not UTC midnight, so this converts
/// "now" (in UTC) into the [start, end) UTC instant range for "today" in WAT.
/// A pure function of its input — no IClock dependency, no ambient time access —
/// so it's testable with fixed instants covering the WAT/UTC date-boundary edge.
/// </summary>
public static class WatClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetDayBoundsUtc(DateTimeOffset nowUtc)
    {
        var nowWat = nowUtc.ToOffset(Offset);
        var startWat = new DateTimeOffset(nowWat.Year, nowWat.Month, nowWat.Day, 0, 0, 0, Offset);
        return (startWat.ToUniversalTime(), startWat.AddDays(1).ToUniversalTime());
    }
}
