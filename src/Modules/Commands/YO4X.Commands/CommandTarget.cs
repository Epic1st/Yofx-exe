using YO4X.BuildingBlocks;

namespace YO4X.Commands;

public enum CommandTargetStatus
{
    PendingDispatch,
    Dispatched,
    Delivered,
    Acknowledged,
    Applied,
    Reconciling,
    Reconciled,
    NotApplicable,
    Unreachable,
    Failed,
    Unknown
}

public enum TargetTerminalProof
{
    Applied,
    Reconciled
}

public sealed record CommandTargetDefinition
{
    public CommandTargetDefinition(
        Guid targetId,
        Guid resourceId,
        string resourceType,
        long resourceVersion,
        TargetTerminalProof requiredProof,
        bool required = true,
        Guid? workerId = null,
        long? generation = null)
    {
        if (targetId == Guid.Empty || resourceId == Guid.Empty)
        {
            throw new DomainException(
                "COMMAND_TARGET_ID_EMPTY",
                "Command target and resource identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        if (resourceVersion < 0 || generation < 0)
        {
            throw new DomainException(
                "COMMAND_TARGET_VERSION_INVALID",
                "Resource versions and worker generations cannot be negative.");
        }

        if (!Enum.IsDefined(requiredProof))
        {
            throw new DomainException(
                "COMMAND_TARGET_PROOF_UNKNOWN",
                "The required target proof is unknown.");
        }

        TargetId = targetId;
        ResourceId = resourceId;
        ResourceType = resourceType.Trim();
        ResourceVersion = resourceVersion;
        RequiredProof = requiredProof;
        Required = required;
        WorkerId = workerId;
        Generation = generation;
    }

    public Guid TargetId { get; }

    public Guid ResourceId { get; }

    public string ResourceType { get; }

    public long ResourceVersion { get; }

    public TargetTerminalProof RequiredProof { get; }

    public bool Required { get; }

    public Guid? WorkerId { get; }

    public long? Generation { get; }
}

public sealed class CommandTarget
{
    internal CommandTarget(CommandTargetDefinition definition, DateTimeOffset createdAt)
    {
        Id = definition.TargetId;
        ResourceId = definition.ResourceId;
        ResourceType = definition.ResourceType;
        ResourceVersion = definition.ResourceVersion;
        RequiredProof = definition.RequiredProof;
        Required = definition.Required;
        WorkerId = definition.WorkerId;
        Generation = definition.Generation;
        Status = CommandTargetStatus.PendingDispatch;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }

    public Guid ResourceId { get; }

    public string ResourceType { get; }

    public long ResourceVersion { get; }

    public TargetTerminalProof RequiredProof { get; }

    public bool Required { get; }

    public Guid? WorkerId { get; }

    public long? Generation { get; }

    public CommandTargetStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public DateTimeOffset? ReconciledAt { get; private set; }

    public string? ObservedResult { get; private set; }

    public string? BrokerEvidenceReference { get; private set; }

    public string? LastErrorCode { get; private set; }

    public bool HasBeenDispatched => DispatchedAt is not null;

    public bool HasReachedRequiredProof => Status == CommandTargetStatus.NotApplicable
        || RequiredProof == TargetTerminalProof.Applied
            && Status is CommandTargetStatus.Applied or CommandTargetStatus.Reconciled
        || RequiredProof == TargetTerminalProof.Reconciled
            && Status == CommandTargetStatus.Reconciled;

    public bool IsFailure => Status is
        CommandTargetStatus.Unreachable or
        CommandTargetStatus.Failed;

    internal void Dispatch(DateTimeOffset now)
    {
        EnsureState(CommandTargetStatus.PendingDispatch);
        Attempts = checked(Attempts + 1);
        DispatchedAt = now.ToUniversalTime();
        LastErrorCode = null;
        Status = CommandTargetStatus.Dispatched;
        Touch(now);
    }

    internal void MarkDelivered(DateTimeOffset now)
    {
        EnsureState(CommandTargetStatus.Dispatched);
        DeliveredAt = now.ToUniversalTime();
        Status = CommandTargetStatus.Delivered;
        Touch(now);
    }

    internal void MarkAcknowledged(DateTimeOffset now)
    {
        EnsureState(CommandTargetStatus.Delivered);
        AcknowledgedAt = now.ToUniversalTime();
        Status = CommandTargetStatus.Acknowledged;
        Touch(now);
    }

    internal void MarkApplied(string? observedResult, DateTimeOffset now)
    {
        EnsureState(CommandTargetStatus.Acknowledged);
        AppliedAt = now.ToUniversalTime();
        ObservedResult = NormalizeOptional(observedResult);
        Status = CommandTargetStatus.Applied;
        Touch(now);
    }

    internal void BeginReconciliation(DateTimeOffset now)
    {
        EnsureState(
            CommandTargetStatus.Applied,
            CommandTargetStatus.Unknown,
            CommandTargetStatus.Unreachable);
        Status = CommandTargetStatus.Reconciling;
        Touch(now);
    }

    internal void MarkReconciled(
        string observedResult,
        string brokerEvidenceReference,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observedResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerEvidenceReference);
        EnsureState(CommandTargetStatus.Reconciling);
        ObservedResult = observedResult.Trim();
        BrokerEvidenceReference = brokerEvidenceReference.Trim();
        ReconciledAt = now.ToUniversalTime();
        LastErrorCode = null;
        Status = CommandTargetStatus.Reconciled;
        Touch(now);
    }

    internal void MarkNotApplicable(string observedResult, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observedResult);
        EnsureState(CommandTargetStatus.PendingDispatch);
        ObservedResult = observedResult.Trim();
        Status = CommandTargetStatus.NotApplicable;
        Touch(now);
    }

    internal void MarkUnreachable(string errorCode, DateTimeOffset now)
    {
        SetFailure(CommandTargetStatus.Unreachable, errorCode, now);
    }

    internal void MarkFailed(string errorCode, DateTimeOffset now)
    {
        SetFailure(CommandTargetStatus.Failed, errorCode, now);
    }

    internal void MarkUnknown(string errorCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        EnsureState(
            CommandTargetStatus.Dispatched,
            CommandTargetStatus.Delivered,
            CommandTargetStatus.Acknowledged,
            CommandTargetStatus.Applied,
            CommandTargetStatus.Reconciling);
        LastErrorCode = errorCode.Trim();
        Status = CommandTargetStatus.Unknown;
        Touch(now);
    }

    private void SetFailure(
        CommandTargetStatus status,
        string errorCode,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        EnsureState(
            CommandTargetStatus.PendingDispatch,
            CommandTargetStatus.Dispatched,
            CommandTargetStatus.Delivered,
            CommandTargetStatus.Acknowledged,
            CommandTargetStatus.Applied,
            CommandTargetStatus.Reconciling);
        LastErrorCode = errorCode.Trim();
        Status = status;
        Touch(now);
    }

    private void EnsureState(params CommandTargetStatus[] expected)
    {
        if (!expected.Contains(Status))
        {
            throw new DomainException(
                "COMMAND_TARGET_TRANSITION_INVALID",
                $"Target {Id} cannot transition from {Status} in this operation.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Touch(DateTimeOffset now) => UpdatedAt = now.ToUniversalTime();
}
