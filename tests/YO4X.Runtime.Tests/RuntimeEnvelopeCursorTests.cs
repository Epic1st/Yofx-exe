using YO4X.Runtime.Contracts;
using YO4X.RuntimeOperations;

namespace YO4X.Runtime.Tests;

public sealed class RuntimeEnvelopeCursorTests
{
    private static readonly Guid DeploymentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkerId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StrictSequenceIsAcceptedAndDuplicateIsNotReprocessed()
    {
        var cursor = new RuntimeEnvelopeCursor(DeploymentId, WorkerId, generation: 7);
        Guid eventId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        RuntimeEnvelope<string> first = Envelope(eventId, generation: 7, sequence: 1);

        RuntimeEnvelopeValidation accepted = cursor.ValidateAndRecord(first);
        RuntimeEnvelopeValidation duplicate = cursor.ValidateAndRecord(first);
        RuntimeEnvelopeValidation second = cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000002"), generation: 7, sequence: 2));

        Assert.True(accepted.IsAccepted);
        Assert.True(duplicate.IsDuplicate);
        Assert.True(second.IsAccepted);
        Assert.Equal(2, cursor.LastAcceptedSequence);
    }

    [Fact]
    public void SequenceGapIsRejectedWithoutAdvancingCursor()
    {
        var cursor = new RuntimeEnvelopeCursor(DeploymentId, WorkerId, generation: 3);

        RuntimeEnvelopeValidation gap = cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000003"), generation: 3, sequence: 2));
        RuntimeEnvelopeValidation first = cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000004"), generation: 3, sequence: 1));

        Assert.Equal(RuntimeEnvelopeDecision.SequenceGap, gap.Decision);
        Assert.Equal(1, gap.ExpectedSequence);
        Assert.True(first.IsAccepted);
    }

    [Fact]
    public void OldGenerationIsFencedAfterExplicitGenerationActivation()
    {
        var cursor = new RuntimeEnvelopeCursor(DeploymentId, WorkerId, generation: 4);
        cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000005"), generation: 4, sequence: 1));

        cursor.ActivateGeneration(5);
        RuntimeEnvelopeValidation oldGeneration = cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000006"), generation: 4, sequence: 2));
        RuntimeEnvelopeValidation newGeneration = cursor.ValidateAndRecord(
            Envelope(Guid.Parse("30000000-0000-0000-0000-000000000007"), generation: 5, sequence: 1));

        Assert.Equal(RuntimeEnvelopeDecision.FencedGeneration, oldGeneration.Decision);
        Assert.True(newGeneration.IsAccepted);
    }

    [Fact]
    public void UnsupportedContractVersionIsRejected()
    {
        var cursor = new RuntimeEnvelopeCursor(DeploymentId, WorkerId, generation: 1);
        RuntimeEnvelope<string> envelope = Envelope(
            Guid.Parse("30000000-0000-0000-0000-000000000008"),
            generation: 1,
            sequence: 1) with
        {
            ContractVersion = 99
        };

        RuntimeEnvelopeValidation result = cursor.ValidateAndRecord(envelope);

        Assert.Equal(RuntimeEnvelopeDecision.UnsupportedVersion, result.Decision);
        Assert.Equal(0, cursor.LastAcceptedSequence);
    }

    [Fact]
    public void EnvelopeFromDifferentWorkerIsRejected()
    {
        var cursor = new RuntimeEnvelopeCursor(DeploymentId, WorkerId, generation: 1);
        RuntimeEnvelope<string> envelope = Envelope(
            Guid.Parse("30000000-0000-0000-0000-000000000009"),
            generation: 1,
            sequence: 1) with
        {
            WorkerInstanceId = Guid.Parse("20000000-0000-0000-0000-000000000099")
        };

        RuntimeEnvelopeValidation result = cursor.ValidateAndRecord(envelope);

        Assert.Equal(RuntimeEnvelopeDecision.WrongWorker, result.Decision);
        Assert.Equal(0, cursor.LastAcceptedSequence);
    }

    private static RuntimeEnvelope<string> Envelope(Guid eventId, long generation, long sequence) =>
        new(
            RuntimeContractVersions.EnvelopeV1,
            DeploymentId,
            WorkerId,
            generation,
            sequence,
            eventId,
            Now,
            null,
            "payload");
}
