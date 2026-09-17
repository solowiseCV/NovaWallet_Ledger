using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Domain.Policies;

/// <summary>
/// The daily outbound transfer limit rule. Deliberately takes the already-computed
/// "sent so far today" total as a parameter rather than querying anything itself —
/// that keeps it a pure function, trivially unit-testable with no database, no
/// mocks, and no async. Fetching that total is Infrastructure's job.
/// </summary>
public static class DailyTransferLimitPolicy
{
    public const long LimitKobo = 50_000_000; // NGN 500,000.00

    public static void EnsureWithinLimit(long alreadySentTodayKobo, long amountKobo)
    {
        if (alreadySentTodayKobo + amountKobo > LimitKobo)
            throw new DailyLimitExceededException(LimitKobo);
    }
}
