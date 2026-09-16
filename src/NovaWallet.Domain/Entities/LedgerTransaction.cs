using NovaWallet.Domain.Enums;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// A single posted movement against a wallet. This is the queryable transaction
/// table (statement). It is distinct from AuditLogEntry, which is the append-only
/// compliance trail judges/auditors query separately.
/// </summary>
public class LedgerTransaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public TransactionType Type { get; set; }
    public long AmountKobo { get; set; }
    public long BalanceAfterKobo { get; set; }
    public Guid? CounterpartyWalletId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
