using NovaWallet.Domain.Enums;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// Tracks Idempotency-Key usage for the transfer endpoint. A unique constraint
/// on Key is what actually prevents double-processing under concurrent replay —
/// not an existence check, which would be a TOCTOU race (see AI_USAGE.md).
/// </summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = default!;
    public string RequestHash { get; set; } = default!;
    public IdempotencyStatus Status { get; set; }
    public int ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
