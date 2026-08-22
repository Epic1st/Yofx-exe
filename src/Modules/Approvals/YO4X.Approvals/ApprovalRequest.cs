using YO4X.BuildingBlocks;

namespace YO4X.Approvals;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Invalidated
}

public enum ApprovalDecisionType
{
    Approve,
    Reject
}

public enum ApprovalAssuranceLevel
{
    Unknown = 0,
    Password = 1,
    MultiFactor = 2,
    PhishingResistant = 3
}

public sealed record ApprovalRequirement
{
    public ApprovalRequirement(
        int requiredIndependentApprovals,
        ApprovalAssuranceLevel minimumAssurance,
        bool managedDeviceRequired,
        TimeSpan maximumSessionAge)
    {
        if (requiredIndependentApprovals is < 1 or > 10)
        {
            throw new DomainException(
                "APPROVAL_QUORUM_INVALID",
                "The independent approval quorum must be between one and ten.");
        }

        if (!Enum.IsDefined(minimumAssurance))
        {
            throw new DomainException(
                "APPROVAL_ASSURANCE_UNKNOWN",
                "The minimum approval assurance is unknown.");
        }

        if (maximumSessionAge <= TimeSpan.Zero)
        {
            throw new DomainException(
                "APPROVAL_SESSION_AGE_INVALID",
                "Maximum approval session age must be positive.");
        }

        RequiredIndependentApprovals = requiredIndependentApprovals;
        MinimumAssurance = minimumAssurance;
        ManagedDeviceRequired = managedDeviceRequired;
        MaximumSessionAge = maximumSessionAge;
    }

    public int RequiredIndependentApprovals { get; }

    public ApprovalAssuranceLevel MinimumAssurance { get; }

    public bool ManagedDeviceRequired { get; }

    public TimeSpan MaximumSessionAge { get; }
}

public sealed record ApprovalActorContext
{
    public ApprovalActorContext(
        Guid actorId,
        ApprovalAssuranceLevel assurance,
        bool managedDevice,
        DateTimeOffset authenticatedAt)
    {
        if (actorId == Guid.Empty)
        {
            throw new DomainException(
                "APPROVAL_ACTOR_ID_EMPTY",
                "An approver identifier cannot be empty.");
        }

        if (!Enum.IsDefined(assurance))
        {
            throw new DomainException(
                "APPROVAL_ASSURANCE_UNKNOWN",
                "The approver assurance is unknown.");
        }

        ActorId = actorId;
        Assurance = assurance;
        ManagedDevice = managedDevice;
        AuthenticatedAt = authenticatedAt.ToUniversalTime();
    }

    public Guid ActorId { get; }

    public ApprovalAssuranceLevel Assurance { get; }

    public bool ManagedDevice { get; }

    public DateTimeOffset AuthenticatedAt { get; }
}

public sealed record ApprovalDecision(
    Guid DecisionId,
    Guid ActorId,
    ApprovalDecisionType Decision,
    ApprovalAssuranceLevel Assurance,
    bool ManagedDevice,
    string BindingDigest,
    string Reason,
    DateTimeOffset DecidedAt);

public sealed record ApprovalValidationResult(bool IsValid, string? FailureCode)
{
    public static ApprovalValidationResult Valid { get; } = new(true, null);

    public static ApprovalValidationResult Invalid(string code) => new(false, code);
}

public sealed class ApprovalRequest : VersionedAggregate
{
    private readonly List<ApprovalDecision> decisions = [];

    private ApprovalRequest(
        Guid id,
        ApprovalBinding binding,
        ApprovalRequirement requirement,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        if (binding.ExpiresAt <= createdAt)
        {
            throw new DomainException(
                "APPROVAL_EXPIRY_INVALID",
                "An approval request must expire after it is created.");
        }

        Binding = binding;
        Requirement = requirement;
        Status = ApprovalStatus.Pending;
    }

    public ApprovalBinding Binding { get; }

    public ApprovalRequirement Requirement { get; }

    public ApprovalStatus Status { get; private set; }

    public IReadOnlyList<ApprovalDecision> Decisions => decisions.AsReadOnly();

    public string? InvalidationCode { get; private set; }

    public static ApprovalRequest Create(
        Guid id,
        ApprovalBinding binding,
        ApprovalRequirement requirement,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(requirement);
        return new ApprovalRequest(id, binding, requirement, createdAt);
    }

    public void Approve(
        Guid decisionId,
        ApprovalActorContext actor,
        ApprovalBinding presentedBinding,
        string reason,
        DateTimeOffset now)
    {
        EnsureDecisionInput(decisionId, actor, presentedBinding, reason, now);
        EnsureActorMeetsRequirement(actor, now);

        decisions.Add(new ApprovalDecision(
            decisionId,
            actor.ActorId,
            ApprovalDecisionType.Approve,
            actor.Assurance,
            actor.ManagedDevice,
            Binding.Digest,
            reason.Trim(),
            now.ToUniversalTime()));

        if (decisions.Count(decision => decision.Decision == ApprovalDecisionType.Approve)
            >= Requirement.RequiredIndependentApprovals)
        {
            Status = ApprovalStatus.Approved;
        }

        RecordChange(now);
    }

    public void Reject(
        Guid decisionId,
        ApprovalActorContext actor,
        ApprovalBinding presentedBinding,
        string reason,
        DateTimeOffset now)
    {
        EnsureDecisionInput(decisionId, actor, presentedBinding, reason, now);
        EnsureActorMeetsRequirement(actor, now);

        decisions.Add(new ApprovalDecision(
            decisionId,
            actor.ActorId,
            ApprovalDecisionType.Reject,
            actor.Assurance,
            actor.ManagedDevice,
            Binding.Digest,
            reason.Trim(),
            now.ToUniversalTime()));
        Status = ApprovalStatus.Rejected;
        RecordChange(now);
    }

    public void MarkExpired(DateTimeOffset now)
    {
        if (Status is not (ApprovalStatus.Pending or ApprovalStatus.Approved))
        {
            throw new DomainException(
                "APPROVAL_STATE_TRANSITION_INVALID",
                $"Approval cannot expire from {Status}.");
        }

        if (now < Binding.ExpiresAt)
        {
            throw new DomainException(
                "APPROVAL_NOT_EXPIRED",
                "The approval binding has not expired.");
        }

        Status = ApprovalStatus.Expired;
        InvalidationCode = "APPROVAL_EXPIRED";
        RecordChange(now);
    }

    public ApprovalValidationResult RevalidateForExecution(
        ApprovalBinding currentBinding,
        IEnumerable<ApprovalActorContext> currentApproverContexts,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);
        ArgumentNullException.ThrowIfNull(currentApproverContexts);

        if (Status != ApprovalStatus.Approved)
        {
            return ApprovalValidationResult.Invalid("APPROVAL_NOT_APPROVED");
        }

        if (now >= Binding.ExpiresAt)
        {
            Invalidate(ApprovalStatus.Expired, "APPROVAL_EXPIRED", now);
            return ApprovalValidationResult.Invalid("APPROVAL_EXPIRED");
        }

        if (!Binding.Matches(currentBinding))
        {
            Invalidate(ApprovalStatus.Invalidated, "APPROVAL_BINDING_MISMATCH", now);
            return ApprovalValidationResult.Invalid("APPROVAL_BINDING_MISMATCH");
        }

        ApprovalActorContext[] contexts = currentApproverContexts.ToArray();
        if (contexts.Select(context => context.ActorId).Distinct().Count() != contexts.Length)
        {
            Invalidate(ApprovalStatus.Invalidated, "APPROVER_CONTEXT_DUPLICATE", now);
            return ApprovalValidationResult.Invalid("APPROVER_CONTEXT_DUPLICATE");
        }

        foreach (ApprovalDecision decision in decisions.Where(
            decision => decision.Decision == ApprovalDecisionType.Approve))
        {
            ApprovalActorContext? context = contexts.SingleOrDefault(
                candidate => candidate.ActorId == decision.ActorId);
            if (context is null)
            {
                Invalidate(ApprovalStatus.Invalidated, "APPROVER_CONTEXT_MISSING", now);
                return ApprovalValidationResult.Invalid("APPROVER_CONTEXT_MISSING");
            }

            string? assuranceFailure = GetAssuranceFailure(context, now);
            if (assuranceFailure is not null)
            {
                Invalidate(ApprovalStatus.Invalidated, assuranceFailure, now);
                return ApprovalValidationResult.Invalid(assuranceFailure);
            }
        }

        return ApprovalValidationResult.Valid;
    }

    private void EnsureDecisionInput(
        Guid decisionId,
        ApprovalActorContext actor,
        ApprovalBinding presentedBinding,
        string reason,
        DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new DomainException(
                "APPROVAL_STATE_TRANSITION_INVALID",
                $"A decision cannot be added while approval is {Status}.");
        }

        if (decisionId == Guid.Empty)
        {
            throw new DomainException(
                "APPROVAL_DECISION_ID_EMPTY",
                "An approval decision identifier cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(presentedBinding);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (now >= Binding.ExpiresAt)
        {
            throw new DomainException("APPROVAL_EXPIRED", "The approval binding has expired.");
        }

        if (!Binding.Matches(presentedBinding))
        {
            throw new DomainException(
                "APPROVAL_BINDING_MISMATCH",
                "The decision does not match the immutable approval binding.");
        }

        if (actor.ActorId == Binding.RequesterId)
        {
            throw new DomainException(
                "APPROVAL_SELF_APPROVAL_FORBIDDEN",
                "A requester cannot approve or reject their own high-risk command.");
        }

        if (decisions.Any(decision => decision.ActorId == actor.ActorId))
        {
            throw new DomainException(
                "APPROVAL_ACTOR_ALREADY_DECIDED",
                "An actor can contribute at most one decision to an approval request.");
        }

        if (decisions.Any(decision => decision.DecisionId == decisionId))
        {
            throw new DomainException(
                "APPROVAL_DECISION_DUPLICATE",
                "An approval decision identifier must be unique.");
        }
    }

    private void EnsureActorMeetsRequirement(ApprovalActorContext actor, DateTimeOffset now)
    {
        string? failureCode = GetAssuranceFailure(actor, now);
        if (failureCode is not null)
        {
            throw new DomainException(
                failureCode,
                "The actor does not meet the approval assurance requirement.");
        }
    }

    private string? GetAssuranceFailure(ApprovalActorContext actor, DateTimeOffset now)
    {
        if ((int)actor.Assurance < (int)Requirement.MinimumAssurance)
        {
            return "APPROVAL_ASSURANCE_INSUFFICIENT";
        }

        if (Requirement.ManagedDeviceRequired && !actor.ManagedDevice)
        {
            return "APPROVAL_MANAGED_DEVICE_REQUIRED";
        }

        if (actor.AuthenticatedAt > now
            || now - actor.AuthenticatedAt > Requirement.MaximumSessionAge)
        {
            return "APPROVAL_STEP_UP_REQUIRED";
        }

        return null;
    }

    private void Invalidate(ApprovalStatus state, string code, DateTimeOffset now)
    {
        Status = state;
        InvalidationCode = code;
        RecordChange(now);
    }
}
