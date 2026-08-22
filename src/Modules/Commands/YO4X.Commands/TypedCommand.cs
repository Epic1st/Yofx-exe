using System.Collections.Frozen;
using YO4X.BuildingBlocks;

namespace YO4X.Commands;

public enum CommandType
{
    RequestUserReauthentication,
    DisableCloudUse,
    DeleteCredentialReference,
    CloseOnly,
    StopAfterFlat,
    RevokeLease,
    ReplaceWorker,
    BlockNewExposure,
    BlockNewDeployments,
    QuarantineGatewayArtifact,
    ExtendContainment,
    ReleaseContainment,
    PromoteGatewayArtifact,
    RollbackGatewayRelease,
    RevokeGatewayArtifact,
    RevokeAccessAssignment,
    RevokeAdminSession
}

public enum CommandStatus
{
    Requested,
    PolicyChecking,
    WaitingApproval,
    Approved,
    Scheduled,
    Dispatching,
    Propagating,
    Reconciling,
    Succeeded,
    Cancelled,
    Rejected,
    Expired,
    Partial,
    Failed,
    Unknown,
    CompensationRequested,
    Compensating,
    Compensated,
    CompensationPartial,
    CompensationFailed
}

public enum CompensationOutcome
{
    Compensated,
    Partial,
    Failed
}

public sealed class TypedCommand : VersionedAggregate
{
    private readonly List<CommandTarget> targets = [];
    private readonly FrozenSet<CommandType> allowedCompensationTypes;

    private TypedCommand(
        Guid id,
        CommandType commandType,
        Guid requesterId,
        Guid? tenantId,
        string payloadDigest,
        string reason,
        string? ticketReference,
        ImpactPreview approvedImpactPreview,
        IEnumerable<CommandType> compensationTypes,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        if (requesterId == Guid.Empty)
        {
            throw new DomainException(
                "COMMAND_REQUESTER_ID_EMPTY",
                "A command requester identifier cannot be empty.");
        }

        if (!Enum.IsDefined(commandType))
        {
            throw new DomainException("COMMAND_TYPE_UNKNOWN", "The command type is unknown.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(approvedImpactPreview);
        ArgumentNullException.ThrowIfNull(compensationTypes);

        CommandType = commandType;
        RequesterId = requesterId;
        TenantId = tenantId;
        PayloadDigest = payloadDigest.Trim();
        Reason = reason.Trim();
        TicketReference = NormalizeOptional(ticketReference);
        ApprovedImpactPreview = approvedImpactPreview;
        allowedCompensationTypes = compensationTypes.ToFrozenSet();
        if (allowedCompensationTypes.Any(type => !Enum.IsDefined(type)))
        {
            throw new DomainException(
                "COMPENSATION_TYPE_UNKNOWN",
                "An allowed compensation command type is unknown.");
        }

        Status = CommandStatus.Requested;
    }

    public CommandType CommandType { get; }

    public Guid RequesterId { get; }

    public Guid? TenantId { get; }

    public string PayloadDigest { get; }

    public string Reason { get; }

    public string? TicketReference { get; }

    public ImpactPreview ApprovedImpactPreview { get; }

    public ImpactPreview? DispatchImpactPreview { get; private set; }

    public CommandStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public Guid? CompensationCommandId { get; private set; }

    public IReadOnlyList<CommandTarget> Targets => targets.AsReadOnly();

    public IReadOnlySet<CommandType> AllowedCompensationTypes => allowedCompensationTypes;

    public bool IsCompensable => allowedCompensationTypes.Count > 0;

    public static TypedCommand Request<TPayload>(
        Guid id,
        CommandType commandType,
        Guid requesterId,
        Guid? tenantId,
        TPayload payload,
        string reason,
        string? ticketReference,
        ImpactPreview approvedImpactPreview,
        IEnumerable<CommandType>? allowedCompensationTypes,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string payloadDigest = CanonicalJson.Sha256(new
        {
            CommandType = commandType.ToString(),
            Payload = payload
        });

        return new TypedCommand(
            id,
            commandType,
            requesterId,
            tenantId,
            payloadDigest,
            reason,
            ticketReference,
            approvedImpactPreview,
            allowedCompensationTypes ?? Array.Empty<CommandType>(),
            createdAt);
    }

    public void BeginPolicyCheck(DateTimeOffset now) =>
        Transition(CommandStatus.Requested, CommandStatus.PolicyChecking, now);

    public void RequireApproval(Guid approvalRequestId, DateTimeOffset now)
    {
        if (approvalRequestId == Guid.Empty)
        {
            throw new DomainException(
                "COMMAND_APPROVAL_ID_EMPTY",
                "An approval request identifier cannot be empty.");
        }

        EnsureStatus(CommandStatus.PolicyChecking);
        ApprovalRequestId = approvalRequestId;
        Status = CommandStatus.WaitingApproval;
        RecordChange(now);
    }

    public void RecordApproval(Guid approvalRequestId, DateTimeOffset now)
    {
        EnsureStatus(CommandStatus.WaitingApproval);
        if (ApprovalRequestId != approvalRequestId)
        {
            throw new DomainException(
                "COMMAND_APPROVAL_BINDING_MISMATCH",
                "The approval does not belong to this command.");
        }

        Status = CommandStatus.Approved;
        RecordChange(now);
    }

    public void ApproveWithoutAdditionalApproval(DateTimeOffset now) =>
        Transition(CommandStatus.PolicyChecking, CommandStatus.Approved, now);

    public void Schedule(DateTimeOffset now) =>
        Transition(CommandStatus.Approved, CommandStatus.Scheduled, now);

    public void BeginDispatch(
        ImpactPreview currentImpactPreview,
        IEnumerable<CommandTargetDefinition> targetDefinitions,
        DateTimeOffset now)
    {
        EnsureStatus(CommandStatus.Scheduled);
        ArgumentNullException.ThrowIfNull(currentImpactPreview);
        ArgumentNullException.ThrowIfNull(targetDefinitions);
        ApprovedImpactPreview.EnsureDispatchableAgainst(currentImpactPreview, now);

        CommandTargetDefinition[] definitions = targetDefinitions.ToArray();
        if (definitions.Length == 0 || definitions.All(definition => !definition.Required))
        {
            throw new DomainException(
                "COMMAND_REQUIRED_TARGET_MISSING",
                "A dispatched command must have at least one required target.");
        }

        if (definitions.Length != currentImpactPreview.TargetCount)
        {
            throw new DomainException(
                "COMMAND_TARGET_COUNT_MISMATCH",
                "Frozen command targets do not match the approved impact count.");
        }

        if (definitions.Select(definition => definition.TargetId).Distinct().Count()
            != definitions.Length
            || definitions.Select(definition => definition.ResourceId).Distinct().Count()
            != definitions.Length)
        {
            throw new DomainException(
                "COMMAND_TARGET_DUPLICATE",
                "Frozen command targets must have unique target and resource identifiers.");
        }

        if (currentImpactPreview.ResolvedTargets.Count > 0)
        {
            var expected = currentImpactPreview.ResolvedTargets
                .Select(target => (target.ResourceId, target.ResourceVersion))
                .OrderBy(target => target.ResourceId)
                .ToArray();
            var actual = definitions
                .Select(target => (target.ResourceId, target.ResourceVersion))
                .OrderBy(target => target.ResourceId)
                .ToArray();

            if (!expected.SequenceEqual(actual))
            {
                throw new DomainException(
                    "COMMAND_TARGET_SNAPSHOT_MISMATCH",
                    "Frozen command targets do not match the revalidated resource snapshot.");
            }
        }

        targets.AddRange(definitions.Select(definition => new CommandTarget(definition, now)));
        DispatchImpactPreview = currentImpactPreview;
        Status = CommandStatus.Dispatching;
        RecordChange(now);
    }

    public void DispatchTarget(Guid targetId, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.Dispatch(now), now);

    public void RecordTargetDelivered(Guid targetId, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkDelivered(now), now);

    public void RecordTargetAcknowledged(Guid targetId, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkAcknowledged(now), now);

    public void RecordTargetApplied(Guid targetId, string? observedResult, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkApplied(observedResult, now), now);

    public void BeginTargetReconciliation(Guid targetId, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.BeginReconciliation(now), now);

    public void RecordTargetReconciled(
        Guid targetId,
        string observedResult,
        string brokerEvidenceReference,
        DateTimeOffset now) =>
        ApplyTargetChange(
            targetId,
            target => target.MarkReconciled(observedResult, brokerEvidenceReference, now),
            now);

    public void RecordTargetNotApplicable(Guid targetId, string result, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkNotApplicable(result, now), now);

    public void RecordTargetUnreachable(Guid targetId, string errorCode, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkUnreachable(errorCode, now), now);

    public void RecordTargetFailed(Guid targetId, string errorCode, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkFailed(errorCode, now), now);

    public void RecordTargetUnknown(Guid targetId, string errorCode, DateTimeOffset now) =>
        ApplyTargetChange(targetId, target => target.MarkUnknown(errorCode, now), now);

    public void Cancel(DateTimeOffset now)
    {
        if (targets.Any(target => target.HasBeenDispatched))
        {
            throw new DomainException(
                "COMMAND_ALREADY_DISPATCHED",
                "A dispatched command cannot be cancelled; request an allowed compensation instead.");
        }

        EnsureStatus(
            CommandStatus.Requested,
            CommandStatus.PolicyChecking,
            CommandStatus.WaitingApproval,
            CommandStatus.Approved,
            CommandStatus.Scheduled,
            CommandStatus.Dispatching);
        Status = CommandStatus.Cancelled;
        RecordChange(now);
    }

    public void Reject(DateTimeOffset now)
    {
        EnsureNoTargetDispatched();
        EnsureStatus(CommandStatus.PolicyChecking, CommandStatus.WaitingApproval);
        Status = CommandStatus.Rejected;
        RecordChange(now);
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureNoTargetDispatched();
        EnsureStatus(
            CommandStatus.Requested,
            CommandStatus.PolicyChecking,
            CommandStatus.WaitingApproval,
            CommandStatus.Approved,
            CommandStatus.Scheduled);
        Status = CommandStatus.Expired;
        RecordChange(now);
    }

    public void RequestCompensation(
        Guid compensationCommandId,
        CommandType compensationType,
        DateTimeOffset now)
    {
        if (compensationCommandId == Guid.Empty || compensationCommandId == Id)
        {
            throw new DomainException(
                "COMPENSATION_COMMAND_ID_INVALID",
                "A compensation must be a distinct immutable command.");
        }

        if (!targets.Any(target => target.HasBeenDispatched))
        {
            throw new DomainException(
                "COMPENSATION_NOT_DISPATCHED",
                "Use cancellation before any target is dispatched.");
        }

        if (!allowedCompensationTypes.Contains(compensationType))
        {
            throw new DomainException(
                "COMMAND_NON_COMPENSABLE",
                "The requested compensation type is not allowed for this command.");
        }

        EnsureStatus(
            CommandStatus.Propagating,
            CommandStatus.Reconciling,
            CommandStatus.Succeeded,
            CommandStatus.Partial,
            CommandStatus.Failed,
            CommandStatus.Unknown);
        CompensationCommandId = compensationCommandId;
        Status = CommandStatus.CompensationRequested;
        RecordChange(now);
    }

    public void BeginCompensating(Guid compensationCommandId, DateTimeOffset now)
    {
        EnsureCompensationCommand(compensationCommandId);
        Transition(CommandStatus.CompensationRequested, CommandStatus.Compensating, now);
    }

    public void CompleteCompensation(
        Guid compensationCommandId,
        CompensationOutcome outcome,
        DateTimeOffset now)
    {
        EnsureCompensationCommand(compensationCommandId);
        EnsureStatus(CommandStatus.Compensating);
        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException(
                "COMPENSATION_OUTCOME_UNKNOWN",
                "The compensation outcome is unknown.");
        }

        Status = outcome switch
        {
            CompensationOutcome.Compensated => CommandStatus.Compensated,
            CompensationOutcome.Partial => CommandStatus.CompensationPartial,
            CompensationOutcome.Failed => CommandStatus.CompensationFailed,
            _ => throw new DomainException(
                "COMPENSATION_OUTCOME_UNKNOWN",
                "The compensation outcome is unknown.")
        };
        RecordChange(now);
    }

    private void ApplyTargetChange(
        Guid targetId,
        Action<CommandTarget> change,
        DateTimeOffset now)
    {
        CommandTarget target = targets.SingleOrDefault(candidate => candidate.Id == targetId)
            ?? throw new DomainException(
                "COMMAND_TARGET_NOT_FOUND",
                "The command target was not found.");
        change(target);
        if (!IsInCompensationLifecycle(Status))
        {
            Status = DeriveExecutionStatus();
        }

        RecordChange(now);
    }

    private CommandStatus DeriveExecutionStatus()
    {
        CommandTarget[] requiredTargets = targets.Where(target => target.Required).ToArray();
        if (requiredTargets.Any(target => target.Status == CommandTargetStatus.Unknown))
        {
            return CommandStatus.Unknown;
        }

        if (requiredTargets.All(target => target.HasReachedRequiredProof))
        {
            return CommandStatus.Succeeded;
        }

        bool anyFailure = requiredTargets.Any(target => target.IsFailure);
        bool allFailure = requiredTargets.All(target => target.IsFailure);
        if (allFailure)
        {
            return CommandStatus.Failed;
        }

        if (anyFailure)
        {
            return CommandStatus.Partial;
        }

        bool requiresReconciliation = requiredTargets.Any(target =>
            target.Status == CommandTargetStatus.Reconciling
            || target.RequiredProof == TargetTerminalProof.Reconciled
                && target.Status == CommandTargetStatus.Applied);

        return requiresReconciliation
            ? CommandStatus.Reconciling
            : CommandStatus.Propagating;
    }

    private void EnsureCompensationCommand(Guid compensationCommandId)
    {
        if (CompensationCommandId != compensationCommandId)
        {
            throw new DomainException(
                "COMPENSATION_COMMAND_MISMATCH",
                "The compensation command is not linked to this original command.");
        }
    }

    private void EnsureNoTargetDispatched()
    {
        if (targets.Any(target => target.HasBeenDispatched))
        {
            throw new DomainException(
                "COMMAND_ALREADY_DISPATCHED",
                "The operation is not permitted after target dispatch.");
        }
    }

    private void Transition(CommandStatus expected, CommandStatus next, DateTimeOffset now)
    {
        EnsureStatus(expected);
        Status = next;
        RecordChange(now);
    }

    private void EnsureStatus(params CommandStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                "COMMAND_STATE_TRANSITION_INVALID",
                $"Command cannot transition from {Status} in this operation.");
        }
    }

    private static bool IsInCompensationLifecycle(CommandStatus status) => status is
        CommandStatus.CompensationRequested or
        CommandStatus.Compensating or
        CommandStatus.Compensated or
        CommandStatus.CompensationPartial or
        CommandStatus.CompensationFailed;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
