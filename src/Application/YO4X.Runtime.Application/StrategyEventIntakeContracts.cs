using System.Text.Json;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Application;

public sealed record StrategyEventReference
{
    public StrategyEventReference(
        Guid deploymentId,
        Guid workerInstanceId,
        long generation,
        long sequence,
        Guid eventId,
        StrategyEventKind eventKind,
        int eventContractVersion,
        string eventSha256,
        long snapshotSequence,
        int snapshotContractVersion,
        string snapshotSha256)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new ArgumentException("A deployment identifier is required.", nameof(deploymentId));
        }

        if (workerInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A worker identifier is required.", nameof(workerInstanceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An event identifier is required.", nameof(eventId));
        }

        if (!Enum.IsDefined(eventKind))
        {
            throw new ArgumentOutOfRangeException(nameof(eventKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventContractVersion);
        StrategyEvidencePrimitives.RequireDigest(eventSha256, nameof(eventSha256));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotContractVersion);
        StrategyEvidencePrimitives.RequireDigest(snapshotSha256, nameof(snapshotSha256));

        DeploymentId = deploymentId;
        WorkerInstanceId = workerInstanceId;
        Generation = generation;
        Sequence = sequence;
        EventId = eventId;
        EventKind = eventKind;
        EventContractVersion = eventContractVersion;
        EventSha256 = eventSha256;
        SnapshotSequence = snapshotSequence;
        SnapshotContractVersion = snapshotContractVersion;
        SnapshotSha256 = snapshotSha256;
    }

    public Guid DeploymentId { get; }

    public Guid WorkerInstanceId { get; }

    public long Generation { get; }

    public long Sequence { get; }

    public Guid EventId { get; }

    public StrategyEventKind EventKind { get; }

    public int EventContractVersion { get; }

    public string EventSha256 { get; }

    public long SnapshotSequence { get; }

    public int SnapshotContractVersion { get; }

    public string SnapshotSha256 { get; }
}

/// <summary>
/// Canonical, immutable input that must be durably persisted before a strategy
/// event may be claimed for evaluation.
/// </summary>
public sealed class StrategyEventInputEvidence
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private StrategyEventInputEvidence(
        StrategyEventReference reference,
        RuntimeEnvelope<StrategyEvent> envelope,
        StrategySnapshot snapshot,
        string eventJson,
        string snapshotJson)
    {
        Reference = reference;
        Envelope = envelope;
        Snapshot = snapshot;
        EventJson = eventJson;
        SnapshotJson = snapshotJson;
    }

    public StrategyEventReference Reference { get; }

    public RuntimeEnvelope<StrategyEvent> Envelope { get; }

    public StrategySnapshot Snapshot { get; }

    public string EventJson { get; }

    public string SnapshotJson { get; }

    public static StrategyEventInputEvidence Create(
        RuntimeEnvelope<StrategyEvent> envelope,
        StrategySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Payload);
        ArgumentNullException.ThrowIfNull(snapshot);
        StrategyEventEvidenceValidator.RequireCanonical(envelope, snapshot);

        string eventJson = CanonicalJson.Serialize(envelope);
        string snapshotJson = CanonicalJson.Serialize(snapshot);
        RequireSupportedDocumentSize(
            eventJson,
            StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize,
            "Event evidence",
            nameof(envelope));
        RequireSupportedDocumentSize(
            snapshotJson,
            StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize,
            "Snapshot evidence",
            nameof(snapshot));
        var reference = new StrategyEventReference(
            envelope.DeploymentId,
            envelope.WorkerInstanceId,
            envelope.Generation,
            envelope.Sequence,
            envelope.EventId,
            envelope.Payload.Kind,
            envelope.Payload.ContractVersion,
            StrategyEvidencePrimitives.Sha256Text(eventJson),
            snapshot.Sequence,
            snapshot.ContractVersion,
            StrategyEvidencePrimitives.Sha256Text(snapshotJson));
        return new StrategyEventInputEvidence(
            reference,
            envelope,
            snapshot,
            eventJson,
            snapshotJson);
    }

    /// <summary>
    /// Rehydrates only exact canonical bytes previously accepted by
    /// <see cref="Create"/>. Infrastructure should use this method instead of
    /// maintaining a second JSON interpretation of runtime inputs.
    /// </summary>
    public static StrategyEventInputEvidence Restore(
        string eventJson,
        string eventSha256,
        string snapshotJson,
        string snapshotSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        StrategyEvidencePrimitives.RequireDigest(eventSha256, nameof(eventSha256));
        StrategyEvidencePrimitives.RequireDigest(snapshotSha256, nameof(snapshotSha256));
        RequireSupportedDocumentSize(
            eventJson,
            StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize,
            "Event evidence",
            nameof(eventJson));
        RequireSupportedDocumentSize(
            snapshotJson,
            StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize,
            "Snapshot evidence",
            nameof(snapshotJson));

        RuntimeEnvelope<StrategyEvent> envelope;
        SnapshotStorage snapshotStorage;
        try
        {
            envelope = JsonSerializer.Deserialize<RuntimeEnvelope<StrategyEvent>>(
                    eventJson,
                    SerializerOptions)
                ?? throw new ArgumentException("Event evidence is empty.", nameof(eventJson));
            snapshotStorage = JsonSerializer.Deserialize<SnapshotStorage>(
                    snapshotJson,
                    SerializerOptions)
                ?? throw new ArgumentException("Snapshot evidence is empty.", nameof(snapshotJson));
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            JsonException or
            NotSupportedException or
            OverflowException)
        {
            throw new ArgumentException(
                "Runtime input evidence is not valid JSON.",
                nameof(eventJson),
                exception);
        }

        if (snapshotStorage.ContractVersion != RuntimeContractVersions.StrategySnapshotV1
            || snapshotStorage.Account is null
            || snapshotStorage.Quotes is null
            || snapshotStorage.Positions is null
            || snapshotStorage.PendingOrders is null)
        {
            throw new ArgumentException(
                "Snapshot evidence is incomplete or unsupported.",
                nameof(snapshotJson));
        }

        StrategySnapshot snapshot = StrategySnapshot.Create(
            snapshotStorage.Sequence,
            snapshotStorage.AsOfUtc,
            snapshotStorage.DeterministicNowUtc,
            snapshotStorage.Account,
            snapshotStorage.Quotes,
            snapshotStorage.Positions,
            snapshotStorage.PendingOrders);
        StrategyEventInputEvidence restored = Create(envelope, snapshot);
        if (!StrategyEvidencePrimitives.FixedTimeEquals(eventJson, restored.EventJson)
            || !StrategyEvidencePrimitives.FixedTimeEquals(snapshotJson, restored.SnapshotJson)
            || !StrategyEvidencePrimitives.FixedTimeEquals(
                eventSha256,
                restored.Reference.EventSha256)
            || !StrategyEvidencePrimitives.FixedTimeEquals(
                snapshotSha256,
                restored.Reference.SnapshotSha256))
        {
            throw new ArgumentException(
                "Runtime input evidence is not exact canonical evidence.",
                nameof(eventJson));
        }

        return restored;
    }

    private static void RequireSupportedDocumentSize(
        string canonicalJson,
        Func<string?, bool> isSupported,
        string evidenceKind,
        string parameterName)
    {
        if (!isSupported(canonicalJson))
        {
            throw new ArgumentException(
                $"{evidenceKind} exceeds the supported byte bounds.",
                parameterName);
        }
    }

    private sealed record SnapshotStorage(
        int ContractVersion,
        long Sequence,
        DateTimeOffset AsOfUtc,
        DateTimeOffset DeterministicNowUtc,
        StrategyAccountSnapshot Account,
        IReadOnlyList<StrategyQuoteSnapshot> Quotes,
        IReadOnlyList<StrategyPositionSnapshot> Positions,
        IReadOnlyList<StrategyPendingOrderSnapshot> PendingOrders);
}

public sealed record StrategyEventIntakeReceipt(
    StrategyEventReference Reference,
    string EventJson,
    string SnapshotJson,
    DateTimeOffset PersistedAtUtc,
    bool Replayed);

public interface IStrategyEventIntakeStore
{
    Task<StrategyEventIntakeReceipt> PersistAsync(
        TenantExecutionContext context,
        StrategyEventInputEvidence input,
        CancellationToken cancellationToken);
}

public enum StrategyEventIntakeOutcome
{
    Persisted = 0,
    AlreadyPersisted = 1,
    DurableRecoveryRequired = 2,
    InvalidReceipt = 3
}

public sealed record StrategyEventIntakeResult(
    StrategyEventIntakeOutcome Outcome,
    string Code,
    StrategyEventReference Reference)
{
    public bool IsDurable => Outcome is
        StrategyEventIntakeOutcome.Persisted or
        StrategyEventIntakeOutcome.AlreadyPersisted;
}

public sealed class StrategyEventIntakeCoordinator
{
    private readonly IStrategyEventIntakeStore store;

    public StrategyEventIntakeCoordinator(IStrategyEventIntakeStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<StrategyEventIntakeResult> PersistAsync(
        TenantExecutionContext context,
        RuntimeEnvelope<StrategyEvent> envelope,
        StrategySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        StrategyEventInputEvidence input = StrategyEventInputEvidence.Create(envelope, snapshot);

        StrategyEventIntakeReceipt receipt;
        try
        {
            receipt = await store.PersistAsync(context, input, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new StrategyEventIntakeResult(
                StrategyEventIntakeOutcome.DurableRecoveryRequired,
                "strategy_event_intake_outcome_ambiguous",
                input.Reference);
        }
        catch (Exception)
        {
            return new StrategyEventIntakeResult(
                StrategyEventIntakeOutcome.DurableRecoveryRequired,
                "strategy_event_intake_store_failed",
                input.Reference);
        }

        if (!StrategyEventReceiptValidator.IsExactIntake(input, receipt))
        {
            return new StrategyEventIntakeResult(
                StrategyEventIntakeOutcome.InvalidReceipt,
                "strategy_event_intake_receipt_invalid",
                input.Reference);
        }

        return new StrategyEventIntakeResult(
            receipt.Replayed
                ? StrategyEventIntakeOutcome.AlreadyPersisted
                : StrategyEventIntakeOutcome.Persisted,
            receipt.Replayed
                ? "strategy_event_already_persisted"
                : "strategy_event_persisted",
            input.Reference);
    }
}
