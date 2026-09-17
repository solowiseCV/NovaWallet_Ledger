namespace NovaWallet.Domain.Enums;

/// <summary>What to do with a repeat request bearing a given Idempotency-Key.</summary>
public enum IdempotencyResolution
{
    /// <summary>Same key, same payload, already finished — hand back the stored result.</summary>
    Replay,
    /// <summary>Same key, different payload — reject; keys are not for reuse across requests.</summary>
    Conflict,
    /// <summary>Same key, still being processed by another request right now — ask the caller to retry.</summary>
    InProgress
}
