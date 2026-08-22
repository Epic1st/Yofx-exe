using YO4X.BuildingBlocks;

namespace YO4X.Policy;

public enum ContainmentPolicyState
{
    Active,
    ExpiryReviewRequired,
    Extended,
    ReleaseApproved,
    Deactivating,
    Reconciling,
    Inactive
}

/// <summary>
/// Governs the lifecycle of an activated restrictive policy. Reaching an expiry
/// never removes the restriction; it only moves the policy into review.
/// </summary>
public sealed class ContainmentPolicy : VersionedAggregate
{
    private ContainmentPolicy(
        Guid id,
        ExecutionSafetyPolicyVector vector,
        DateTimeOffset createdAt,
        DateTimeOffset? reviewAt)
        : base(id, createdAt)
    {
        if (!vector.IsAtLeastAsRestrictiveAs(ExecutionSafetyPolicyVector.Unrestricted)
            || vector == ExecutionSafetyPolicyVector.Unrestricted)
        {
            throw new DomainException(
                "CONTAINMENT_POLICY_NOT_RESTRICTIVE",
                "A containment policy must add at least one restriction.");
        }

        if (reviewAt is not null && reviewAt <= createdAt)
        {
            throw new DomainException(
                "CONTAINMENT_REVIEW_TIME_INVALID",
                "The containment review time must be later than creation time.");
        }

        Vector = vector;
        VectorDigest = vector.ComputeDigest();
        ReviewAt = reviewAt?.ToUniversalTime();
        State = ContainmentPolicyState.Active;
    }

    public ExecutionSafetyPolicyVector Vector { get; }

    public string VectorDigest { get; }

    public ContainmentPolicyState State { get; private set; }

    public DateTimeOffset? ReviewAt { get; private set; }

    public string? ReleasePreviewDigest { get; private set; }

    public string? ReleaseApprovalBindingDigest { get; private set; }

    public static ContainmentPolicy Activate(
        Guid id,
        ExecutionSafetyPolicyVector vector,
        DateTimeOffset createdAt,
        DateTimeOffset? reviewAt = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        return new ContainmentPolicy(id, vector, createdAt, reviewAt);
    }

    public bool IsReviewDue(DateTimeOffset now) => ReviewAt is not null && now >= ReviewAt;

    public void RequireExpiryReview(DateTimeOffset now)
    {
        EnsureState(ContainmentPolicyState.Active, ContainmentPolicyState.Extended);
        if (!IsReviewDue(now))
        {
            throw new DomainException(
                "CONTAINMENT_REVIEW_NOT_DUE",
                "The containment policy has not reached its review time.");
        }

        State = ContainmentPolicyState.ExpiryReviewRequired;
        RecordChange(now);
    }

    public void Extend(DateTimeOffset newReviewAt, DateTimeOffset now)
    {
        EnsureState(ContainmentPolicyState.ExpiryReviewRequired);
        if (newReviewAt <= now || (ReviewAt is not null && newReviewAt <= ReviewAt))
        {
            throw new DomainException(
                "CONTAINMENT_EXTENSION_INVALID",
                "An extension must move the review time into the future.");
        }

        ReviewAt = newReviewAt.ToUniversalTime();
        State = ContainmentPolicyState.Extended;
        RecordChange(now);
    }

    public void ApproveRelease(
        string releasePreviewDigest,
        string approvalBindingDigest,
        DateTimeOffset now)
    {
        EnsureSha256(releasePreviewDigest, "CONTAINMENT_RELEASE_PREVIEW_DIGEST_INVALID");
        EnsureSha256(approvalBindingDigest, "CONTAINMENT_RELEASE_APPROVAL_DIGEST_INVALID");
        EnsureState(
            ContainmentPolicyState.Active,
            ContainmentPolicyState.Extended,
            ContainmentPolicyState.ExpiryReviewRequired);

        ReleasePreviewDigest = releasePreviewDigest;
        ReleaseApprovalBindingDigest = approvalBindingDigest;
        State = ContainmentPolicyState.ReleaseApproved;
        RecordChange(now);
    }

    public void BeginDeactivation(DateTimeOffset now)
    {
        Transition(
            ContainmentPolicyState.ReleaseApproved,
            ContainmentPolicyState.Deactivating,
            now);
    }

    public void BeginReconciliation(DateTimeOffset now)
    {
        Transition(
            ContainmentPolicyState.Deactivating,
            ContainmentPolicyState.Reconciling,
            now);
    }

    public void CompleteRelease(DateTimeOffset now)
    {
        Transition(ContainmentPolicyState.Reconciling, ContainmentPolicyState.Inactive, now);
    }

    private void Transition(
        ContainmentPolicyState expected,
        ContainmentPolicyState next,
        DateTimeOffset now)
    {
        EnsureState(expected);
        State = next;
        RecordChange(now);
    }

    private void EnsureState(params ContainmentPolicyState[] allowed)
    {
        if (!allowed.Contains(State))
        {
            throw new DomainException(
                "CONTAINMENT_STATE_TRANSITION_INVALID",
                $"Containment policy cannot transition from {State} in this operation.");
        }
    }

    private static void EnsureSha256(string digest, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainException(
                errorCode,
                "Containment release evidence must use a hexadecimal SHA-256 digest.");
        }
    }
}
