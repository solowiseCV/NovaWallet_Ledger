namespace NovaWallet.Application.Interfaces;

/// <summary>Abstraction over the system clock so time-dependent logic (e.g. the
/// daily limit reset at midnight WAT) is testable without sleeping real time.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
