using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public enum StrategyOwnershipClass
{
    Yo4xOwned,
    UserPrivate
}

public enum StrategyVersionState
{
    Draft,
    ManuallyReviewed,
    DemoEligible,
    Suspended,
    Revoked
}

public enum EvidenceTrustLabel
{
    UserSupplied,
    LabVerified,
    Reproduced,
    Unavailable
}

public sealed record StrategyValidationEvidence(
    string EvidenceDigest,
    EvidenceTrustLabel TrustLabel,
    string DatasetDigest,
    string RuntimeVersion,
    DateTimeOffset RecordedAt);

public sealed partial class StrategyVersion : VersionedAggregate
{
    private StrategyVersion(
        Guid id,
        Guid strategyId,
        int versionNumber,
        StrategyOwnershipClass ownershipClass,
        string packageDigest,
        string manifestDigest,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        StrategyId = strategyId;
        VersionNumber = versionNumber;
        OwnershipClass = ownershipClass;
        PackageDigest = packageDigest;
        ManifestDigest = manifestDigest;
        State = StrategyVersionState.Draft;
    }

    public Guid StrategyId { get; }

    public int VersionNumber { get; }

    public StrategyOwnershipClass OwnershipClass { get; }

    public string PackageDigest { get; }

    public string ManifestDigest { get; }

    public StrategyVersionState State { get; private set; }

    public StrategyValidationEvidence? ValidationEvidence { get; private set; }

    public string? ReviewEvidenceDigest { get; private set; }

    public static StrategyVersion CreateManualU0Candidate(
        Guid strategyId,
        int versionNumber,
        string packageDigest,
        string manifestDigest,
        IClock clock)
    {
        if (strategyId == Guid.Empty)
        {
            throw new ArgumentException("A strategy identifier is required.", nameof(strategyId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ValidateDigest(packageDigest, nameof(packageDigest));
        ValidateDigest(manifestDigest, nameof(manifestDigest));
        return new StrategyVersion(
            Identifiers.NewId(),
            strategyId,
            versionNumber,
            StrategyOwnershipClass.Yo4xOwned,
            packageDigest.ToLowerInvariant(),
            manifestDigest.ToLowerInvariant(),
            clock.UtcNow);
    }

    public void RecordManualReview(string reviewEvidenceDigest, DateTimeOffset occurredAt)
    {
        EnsureNotRevoked();
        ValidateDigest(reviewEvidenceDigest, nameof(reviewEvidenceDigest));
        ReviewEvidenceDigest = reviewEvidenceDigest.ToLowerInvariant();
        State = StrategyVersionState.ManuallyReviewed;
        RecordChange(occurredAt);
    }

    public void ApproveForDemo(StrategyValidationEvidence evidence, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (State != StrategyVersionState.ManuallyReviewed || ReviewEvidenceDigest is null)
        {
            throw new DomainException("MANUAL_STRATEGY_REVIEW_REQUIRED", "A manual review is required before demo eligibility.");
        }

        ValidateDigest(evidence.EvidenceDigest, nameof(evidence));
        ValidateDigest(evidence.DatasetDigest, nameof(evidence));
        if (evidence.TrustLabel == EvidenceTrustLabel.Unavailable)
        {
            throw new DomainException("STRATEGY_VALIDATION_EVIDENCE_REQUIRED", "Unavailable evidence cannot establish demo eligibility.");
        }

        ValidationEvidence = evidence;
        State = StrategyVersionState.DemoEligible;
        RecordChange(occurredAt);
    }

    public void Suspend(DateTimeOffset occurredAt)
    {
        EnsureNotRevoked();
        State = StrategyVersionState.Suspended;
        RecordChange(occurredAt);
    }

    public void Revoke(DateTimeOffset occurredAt)
    {
        if (State == StrategyVersionState.Revoked)
        {
            return;
        }

        State = StrategyVersionState.Revoked;
        RecordChange(occurredAt);
    }

    private static void ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DigestPattern().IsMatch(value))
        {
            throw new ArgumentException("A SHA-256 digest is required.", parameterName);
        }
    }

    private void EnsureNotRevoked()
    {
        if (State == StrategyVersionState.Revoked)
        {
            throw new DomainException("STRATEGY_VERSION_REVOKED", "A revoked strategy version cannot be made eligible again.");
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
