namespace NovaWallet.Domain.Entities;

/// <summary>
/// Append-only, immutable record of every balance mutation, kept separate from
/// the transaction table so it can serve as a tamper-evident compliance trail.
/// The DbContext enforces immutability: any attempt to UPDATE or DELETE a row
/// throws (see AppendOnlyAuditInterceptor).
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public Guid WalletId { get; set; }
    public string Action { get; set; } = default!;
    public long? AmountKobo { get; set; }
    public long BalanceBeforeKobo { get; set; }
    public long BalanceAfterKobo { get; set; }
    public string? ActorId { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
