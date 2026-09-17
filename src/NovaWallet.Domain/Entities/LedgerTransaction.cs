using NovaWallet.Domain.Enums;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// A single posted movement against a wallet — the queryable statement.
/// Immutable once created (private setters, no mutating methods): a ledger
/// line is a historical fact, not something that gets edited later.
/// </summary>
public class LedgerTransaction
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public TransactionType Type { get; private set; }
    public long AmountKobo { get; private set; }
    public long BalanceAfterKobo { get; private set; }
    public Guid? CounterpartyWalletId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private LedgerTransaction() { } // EF Core

    public static LedgerTransaction Create(
        Guid walletId,
        TransactionType type,
        long amountKobo,
        long balanceAfterKobo,
        Guid? counterpartyWalletId,
        string? idempotencyKey,
        string? description,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            Type = type,
            AmountKobo = amountKobo,
            BalanceAfterKobo = balanceAfterKobo,
            CounterpartyWalletId = counterpartyWalletId,
            IdempotencyKey = idempotencyKey,
            Description = description,
            CreatedAtUtc = now
        };
}
