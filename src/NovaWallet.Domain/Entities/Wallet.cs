using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// A customer's NovaWallet balance. Balance is always in kobo (1 NGN = 100 kobo),
/// stored as a signed 64-bit integer — never a float/double — to avoid rounding
/// drift in the money path.
///
/// All setters are private: a Wallet can only change through Credit/Debit, which
/// enforce the "never negative" invariant here, in the one place that can't be
/// bypassed — not in a service class that happens to remember to check first.
/// </summary>
public class Wallet
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public long BalanceKobo { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // EF Core materializes via this parameterless constructor + property setters
    // (reflection ignores the "private" access modifier), so this is safe even
    // though application code can't call it directly.
    private Wallet() { }

    public static Wallet Create(string customerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new InvalidTransferException("customerId is required.");

        return new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            BalanceKobo = 0,
            Currency = "NGN",
            CreatedAtUtc = now
        };
    }

    /// <summary>Increases the balance. Throws if amountKobo isn't a positive integer.</summary>
    public void Credit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new InvalidTransferException("amountKobo must be a positive integer number of kobo.");

        BalanceKobo = checked(BalanceKobo + amountKobo);
    }

    /// <summary>Decreases the balance. Throws if amountKobo is invalid or exceeds
    /// the current balance — this is the one method in the whole codebase that
    /// can make BalanceKobo negative, and it refuses to.</summary>
    public void Debit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new InvalidTransferException("amountKobo must be a positive integer number of kobo.");

        if (BalanceKobo < amountKobo)
            throw new InsufficientFundsException();

        BalanceKobo -= amountKobo;
    }

    /// <summary>Guards actions that only the wallet's own customer may perform.</summary>
    public void EnsureOwnedBy(string customerId)
    {
        if (!string.Equals(CustomerId, customerId, StringComparison.Ordinal))
            throw new ForbiddenOperationException("You may only transfer funds out of your own wallet.");
    }
}
