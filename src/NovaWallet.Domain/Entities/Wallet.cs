using NovaWallet.Domain.Exceptions;
using NovaWallet.Domain.Services;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// A customer's NovaWallet balance. Balance is always in kobo (1 NGN = 100 kobo)
/// and stored as a signed 64-bit integer — never a float/double — to avoid
/// rounding drift in the money path.
/// </summary>
public class Wallet
{
    private Wallet() { }

    private Wallet(Guid id, string customerId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        BalanceKobo = 0;
        Currency = "NGN";
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public long BalanceKobo { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public List<LedgerTransaction> Transactions { get; set; } = new();

    public static Wallet Create(string customerId, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new InvalidTransferException("customerId is required.");

        return new Wallet(Guid.NewGuid(), customerId.Trim(), createdAtUtc);
    }

    public void Credit(long amountKobo)
    {
        WalletTransferPolicy.EnsurePositiveAmount(amountKobo);
        BalanceKobo = checked(BalanceKobo + amountKobo);
    }

    public void Debit(long amountKobo)
    {
        WalletTransferPolicy.EnsurePositiveAmount(amountKobo);
        if (BalanceKobo < amountKobo)
            throw new InsufficientFundsException();

        BalanceKobo -= amountKobo;
    }

    public void EnsureOwnedBy(string actorId)
    {
        if (!string.Equals(CustomerId, actorId, StringComparison.Ordinal))
            throw new ForbiddenOperationException("You may only transfer funds out of your own wallet.");
    }
}
