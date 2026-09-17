using NovaWallet.Domain.Enums;

namespace NovaWallet.Domain.Entities;

/// <summary>
/// Tracks Idempotency-Key usage for the transfer endpoint. The database's unique
/// constraint on Key is what actually prevents double-processing under concurrent
/// replay (see IWalletRepository.TryAddIdempotencyRecordAsync) — this object just
/// decides, given a record that already exists, what should happen next.
///
/// Deliberately doesn't know about TransferResponse or any Application DTO —
/// ResponseBody is an opaque, already-serialized string. Domain must not depend
/// on Application; deserializing it back into a typed response is the caller's job.
/// </summary>
public class IdempotencyRecord
{
    public string Key { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public IdempotencyStatus Status { get; private set; }
    public int ResponseStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private IdempotencyRecord() { } // EF Core

    public static IdempotencyRecord CreateProcessing(string key, string requestHash, DateTimeOffset now) => new()
    {
        Key = key,
        RequestHash = requestHash,
        Status = IdempotencyStatus.Processing,
        CreatedAtUtc = now
    };

    public void Complete(int responseStatusCode, string responseBody)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
    }

    /// <summary>Pure decision, no I/O: given the hash of an incoming request that
    /// reused this key, what should the caller do?</summary>
    public IdempotencyResolution Resolve(string incomingRequestHash)
    {
        if (RequestHash != incomingRequestHash)
            return IdempotencyResolution.Conflict;

        return Status == IdempotencyStatus.Processing
            ? IdempotencyResolution.InProgress
            : IdempotencyResolution.Replay;
    }
}
