using NovaWallet.Application.Interfaces;

namespace NovaWallet.Application.Tests.Fakes;

public class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
}
