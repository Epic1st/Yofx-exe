using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Application;

public sealed record ClaimedStrategyEvent(
    StrategyEventReference Reference,
    Guid ClaimToken,
    DateTimeOffset AuthorityNowUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    RuntimeEnvelope<StrategyEvent> Envelope,
    StrategySnapshot Snapshot,
    StrategyState PriorState,
    string EventJson,
    string SnapshotJson,
    string PriorStateJson,
    string PriorStateSha256,
    bool Replayed);

public enum StrategyEventClaimDisposition
{
    NoWork = 0,
    Claimed = 1,
    AlreadyCommitted = 2
}

public sealed class StrategyEventClaimResult
{
    private StrategyEventClaimResult(
        StrategyEventClaimDisposition disposition,
        string code,
        ClaimedStrategyEvent? claim,
        StrategyEventCommitReceipt? receipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Disposition = disposition;
        Code = code;
        Claim = claim;
        Receipt = receipt;
    }

    public StrategyEventClaimDisposition Disposition { get; }

    public string Code { get; }

    public ClaimedStrategyEvent? Claim { get; }

    public StrategyEventCommitReceipt? Receipt { get; }

    public static StrategyEventClaimResult NoWork(string code = "strategy_event_no_work") =>
        new(StrategyEventClaimDisposition.NoWork, code, null, null);

    public static StrategyEventClaimResult Claimed(ClaimedStrategyEvent claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return new StrategyEventClaimResult(
            StrategyEventClaimDisposition.Claimed,
            "strategy_event_claimed",
            claim,
            null);
    }

    public static StrategyEventClaimResult AlreadyCommitted(StrategyEventCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new StrategyEventClaimResult(
            StrategyEventClaimDisposition.AlreadyCommitted,
            "strategy_event_already_committed",
            null,
            receipt);
    }
}

public sealed record StrategyHostEvaluationRequest(
    Guid TenantId,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    long Sequence,
    Guid EventId,
    StrategyEvent Event,
    StrategySnapshot Snapshot,
    StrategyState PriorState,
    string EventSha256,
    string SnapshotSha256,
    string PriorStateSha256);

/// <summary>
/// A request/response transport to an isolated StrategyHost. Implementations
/// must not run untrusted strategy code in the caller process and must tear
/// down the isolated workload when cancellation is requested.
/// </summary>
public interface IStrategyHostClient
{
    Task<StrategyResult?> EvaluateAsync(
        StrategyHostEvaluationRequest request,
        CancellationToken cancellationToken);
}

public sealed record StrategyActionOutboxDocument(
    int ContractVersion,
    Guid TenantId,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    long EventSequence,
    Guid EventId,
    long StateVersion,
    int ActionOrdinal,
    Guid ActionId,
    string IdempotencyKey,
    RequestedActionKind ActionKind,
    RequestedExposureHint ExposureHint,
    string ActionSha256);

public sealed record StrategyCommittedActionDocument(
    int Ordinal,
    Guid ActionId,
    string IdempotencyKey,
    RequestedActionKind Kind,
    RequestedExposureHint ExposureHint,
    string Symbol,
    long MarketDataSequence,
    string ActionJson,
    string ActionSha256,
    Guid OutboxMessageId,
    string OutboxTopic,
    string OutboxPayloadJson,
    string OutboxPayloadSha256);

public sealed record StrategyEventCommitDocument(
    int ContractVersion,
    Guid CommitId,
    Guid ClaimToken,
    Guid TenantId,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    long EventSequence,
    Guid EventId,
    StrategyEventKind EventKind,
    int EventContractVersion,
    string EventJson,
    string EventSha256,
    long SnapshotSequence,
    int SnapshotContractVersion,
    string SnapshotJson,
    string SnapshotSha256,
    long PriorStateVersion,
    string PriorStateJson,
    string PriorStateSha256,
    long NextStateVersion,
    string NextStateJson,
    string NextStateSha256,
    string ResultJson,
    string ResultSha256,
    int StateBytes,
    int CombinedActionBytes,
    IReadOnlyList<StrategyCommittedActionDocument> Actions,
    DateTimeOffset ClaimAuthorityNowUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    DateTimeOffset PreparedAtUtc);

public sealed class StrategyEventCommitEvidence
{
    internal StrategyEventCommitEvidence(
        StrategyEventCommitDocument document,
        string canonicalJson,
        string sha256)
    {
        Document = document;
        CanonicalJson = canonicalJson;
        Sha256 = sha256;
    }

    public StrategyEventCommitDocument Document { get; }

    public string CanonicalJson { get; }

    public string Sha256 { get; }

    public static StrategyEventCommitEvidence Restore(string canonicalJson, string sha256) =>
        StrategyEventCommitEvidenceFactory.Restore(canonicalJson, sha256);
}

public sealed record StrategyEventCommitRequest(
    ClaimedStrategyEvent Claim,
    BoundedStrategyResult Result,
    StrategyEventCommitEvidence Evidence);

public sealed record StrategyEventCommitReceipt(
    StrategyEventCommitEvidence Evidence,
    DateTimeOffset RecordedAtUtc,
    bool Replayed);

public interface IStrategyEventTransactionStore
{
    Task<StrategyEventClaimResult> ClaimAsync(
        TenantExecutionContext context,
        StrategyEventReference reference,
        Guid claimToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically records event consumption, the next state version, every
    /// requested-action intent, and its risk-evaluation outbox message. An
    /// exact retry of the same evidence must return a replayed receipt; a
    /// conflicting retry must fail closed.
    /// </summary>
    Task<StrategyEventCommitReceipt> CommitAsync(
        TenantExecutionContext context,
        StrategyEventCommitRequest request,
        CancellationToken cancellationToken);
}

public interface IStrategyRuntimeIdentifierSource
{
    Guid NewId();
}

public sealed class UuidV7StrategyRuntimeIdentifierSource : IStrategyRuntimeIdentifierSource
{
    public Guid NewId() => Guid.CreateVersion7();
}

public sealed class StrategyEventProcessingOptions
{
    public required StrategyResultBounds ResultBounds { get; init; }

    public int CommitAcknowledgementRecoveryAttempts { get; init; } = 1;

    /// <summary>
    /// Bounds in-flight transport invocations, including timed-out invocations
    /// whose untrusted client has not honored cancellation. The coordinator
    /// fails closed instead of creating additional worker tasks after this
    /// limit is reached.
    /// </summary>
    public int MaximumConcurrentHostEvaluations { get; init; } = 1;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ResultBounds);
        if (ResultBounds.MaximumStateBytes is < 1
            or > StrategyDurableEvidenceLimits.MaximumStateBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ResultBounds),
                "The state result bound exceeds the durable store limit.");
        }

        if (ResultBounds.MaximumActionCount is < 0
            or > StrategyDurableEvidenceLimits.MaximumActionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ResultBounds),
                "The action-count result bound exceeds the durable store limit.");
        }

        if (ResultBounds.MaximumCombinedActionBytes is < 2
            or > StrategyDurableEvidenceLimits.MaximumCombinedActionBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ResultBounds),
                "The combined-action result bound must fit the durable JSON array limits.");
        }

        if (ResultBounds.MaximumWallTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ResultBounds),
                "The strategy wall-time bound must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(CommitAcknowledgementRecoveryAttempts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(CommitAcknowledgementRecoveryAttempts, 3);
        if (MaximumConcurrentHostEvaluations is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConcurrentHostEvaluations),
                "The host-evaluation concurrency bound must be between one and 32.");
        }
    }
}

public enum StrategyEventProcessingOutcome
{
    NoWork = 0,
    Committed = 1,
    AlreadyCommitted = 2,
    ClaimRecoveryRequired = 3,
    InvalidClaim = 4,
    EvaluationFaulted = 5,
    EvaluationTimedOut = 6,
    EvaluationCancelled = 7,
    InvalidResult = 8,
    CommitRecoveryRequired = 9,
    InvalidCommitReceipt = 10
}

public sealed record StrategyEventProcessingResult(
    StrategyEventProcessingOutcome Outcome,
    string Code,
    StrategyEventReference Reference,
    StrategyResultValidationCode? ValidationCode = null,
    StrategyEventCommitReceipt? Receipt = null)
{
    public bool IsCommitted => Outcome is
        StrategyEventProcessingOutcome.Committed or
        StrategyEventProcessingOutcome.AlreadyCommitted;
}
