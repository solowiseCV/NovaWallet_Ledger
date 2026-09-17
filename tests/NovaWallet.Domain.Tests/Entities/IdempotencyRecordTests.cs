using FluentAssertions;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using Xunit;

namespace NovaWallet.Domain.Tests.Entities;

public class IdempotencyRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateProcessing_StartsInProcessingStatus()
    {
        var record = IdempotencyRecord.CreateProcessing("key-1", "hash-1", Now);

        record.Status.Should().Be(IdempotencyStatus.Processing);
        record.ResponseBody.Should().BeNull();
    }

    [Fact]
    public void Resolve_ReturnsInProgress_WhenStillProcessingAndHashMatches()
    {
        var record = IdempotencyRecord.CreateProcessing("key-1", "hash-1", Now);

        record.Resolve("hash-1").Should().Be(IdempotencyResolution.InProgress);
    }

    [Fact]
    public void Resolve_ReturnsReplay_WhenCompletedAndHashMatches()
    {
        var record = IdempotencyRecord.CreateProcessing("key-1", "hash-1", Now);
        record.Complete(200, "{\"ok\":true}");

        record.Resolve("hash-1").Should().Be(IdempotencyResolution.Replay);
    }

    [Fact]
    public void Resolve_ReturnsConflict_WhenHashDiffers_RegardlessOfStatus()
    {
        var record = IdempotencyRecord.CreateProcessing("key-1", "hash-1", Now);
        record.Complete(200, "{\"ok\":true}");

        record.Resolve("a-completely-different-hash").Should().Be(IdempotencyResolution.Conflict);
    }

    [Fact]
    public void Complete_StoresResponseForReplay()
    {
        var record = IdempotencyRecord.CreateProcessing("key-1", "hash-1", Now);

        record.Complete(200, "{\"amount\":100}");

        record.Status.Should().Be(IdempotencyStatus.Completed);
        record.ResponseStatusCode.Should().Be(200);
        record.ResponseBody.Should().Be("{\"amount\":100}");
    }
}
