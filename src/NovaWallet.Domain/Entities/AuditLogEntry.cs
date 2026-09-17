namespace NovaWallet.Domain.Entities;

/// <summary>
/// Append-only, immutable record of every balance mutation, kept separate from
/// the transaction table so it can serve as a tamper-evident compliance trail.
/// No mutating methods are exposed here at all — combined with the EF Core
/// interceptor in Infrastructure that rejects UPDATE/DELETE against this table,
/// immutability is enforced at both the object-model and persistence levels.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; private set; }
    public Guid WalletId { get; private set; }
    public string Action { get; private set; } = default!;
    public long? AmountKobo { get; private set; }
    public long BalanceBeforeKobo { get; private set; }
    public long BalanceAfterKobo { get; private set; }
    public string? ActorId { get; private set; }
    public string? Metadata { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    private AuditLogEntry() { } // EF Core

    public static AuditLogEntry Create(
        Guid walletId,
        string action,
        long? amountKobo,
        long balanceBeforeKobo,
        long balanceAfterKobo,
        string? actorId,
        string? metadata,
        DateTimeOffset now) => new()
        {
            WalletId = walletId,
            Action = action,
            AmountKobo = amountKobo,
            BalanceBeforeKobo = balanceBeforeKobo,
            BalanceAfterKobo = balanceAfterKobo,
            ActorId = actorId,
            Metadata = metadata,
            TimestampUtc = now
        };
}
