using YO4X.BuildingBlocks;

namespace YO4X.Commands;

public sealed record ImpactTargetSnapshot
{
    public ImpactTargetSnapshot(Guid resourceId, long resourceVersion, string? exposureClass = null)
    {
        if (resourceId == Guid.Empty)
        {
            throw new DomainException(
                "IMPACT_TARGET_ID_EMPTY",
                "An impact target identifier cannot be empty.");
        }

        if (resourceVersion < 0)
        {
            throw new DomainException(
                "IMPACT_TARGET_VERSION_INVALID",
                "An impact target version cannot be negative.");
        }

        ResourceId = resourceId;
        ResourceVersion = resourceVersion;
        ExposureClass = NormalizeOptional(exposureClass);
    }

    public Guid ResourceId { get; }

    public long ResourceVersion { get; }

    public string? ExposureClass { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ImpactSummary
{
    public ImpactSummary(
        int users,
        int accounts,
        int deployments,
        int positions,
        IEnumerable<string>? regions = null,
        IEnumerable<string>? versions = null)
    {
        if (users < 0 || accounts < 0 || deployments < 0 || positions < 0)
        {
            throw new DomainException(
                "IMPACT_SUMMARY_COUNT_INVALID",
                "Impact summary counts cannot be negative.");
        }

        Users = users;
        Accounts = accounts;
        Deployments = deployments;
        Positions = positions;
        Regions = NormalizeValues(regions);
        Versions = NormalizeValues(versions);
    }

    public int Users { get; }

    public int Accounts { get; }

    public int Deployments { get; }

    public int Positions { get; }

    public IReadOnlyList<string> Regions { get; }

    public IReadOnlyList<string> Versions { get; }

    public string ComputeDigest() => CanonicalJson.Sha256(ToDigestContract());

    internal object ToDigestContract() => new
    {
        Users,
        Accounts,
        Deployments,
        Positions,
        Regions,
        Versions
    };

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        string[] normalized = values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return Array.AsReadOnly(normalized);
    }
}

public sealed record PreviewComparison(
    bool IsMateriallyEquivalent,
    IReadOnlyList<string> MaterialChanges);

/// <summary>
/// An immutable, canonically hashed snapshot used for approval and dispatch
/// revalidation. Resolved targets are normalized by identifier.
/// </summary>
public sealed class ImpactPreview
{
    private ImpactPreview(
        string scopeExpression,
        IReadOnlyList<ImpactTargetSnapshot> resolvedTargets,
        string? snapshotReference,
        int targetCount,
        long resourceVersionWatermark,
        string policyVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ImpactSummary impactSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentNullException.ThrowIfNull(impactSummary);

        if (targetCount < 0 || resourceVersionWatermark < 0)
        {
            throw new DomainException(
                "IMPACT_PREVIEW_COUNT_INVALID",
                "Target counts and version watermarks cannot be negative.");
        }

        if (expiresAt <= createdAt)
        {
            throw new DomainException(
                "IMPACT_PREVIEW_EXPIRY_INVALID",
                "An impact preview must expire after it is created.");
        }

        ScopeExpression = scopeExpression.Trim();
        ResolvedTargets = resolvedTargets;
        SnapshotReference = NormalizeOptional(snapshotReference);
        TargetCount = targetCount;
        ResourceVersionWatermark = resourceVersionWatermark;
        PolicyVersion = policyVersion.Trim();
        CreatedAt = createdAt.ToUniversalTime();
        ExpiresAt = expiresAt.ToUniversalTime();
        ImpactSummary = impactSummary;
        Digest = CanonicalJson.Sha256(ToDigestContract());
    }

    public string ScopeExpression { get; }

    public IReadOnlyList<ImpactTargetSnapshot> ResolvedTargets { get; }

    public string? SnapshotReference { get; }

    public int TargetCount { get; }

    public long ResourceVersionWatermark { get; }

    public string PolicyVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string Digest { get; }

    public ImpactSummary ImpactSummary { get; }

    public static ImpactPreview CreateResolved(
        string scopeExpression,
        IEnumerable<ImpactTargetSnapshot> targets,
        string policyVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ImpactSummary impactSummary)
    {
        ArgumentNullException.ThrowIfNull(targets);

        ImpactTargetSnapshot[] normalizedTargets = targets
            .OrderBy(target => target.ResourceId)
            .ToArray();

        if (normalizedTargets.Select(target => target.ResourceId).Distinct().Count()
            != normalizedTargets.Length)
        {
            throw new DomainException(
                "IMPACT_PREVIEW_DUPLICATE_TARGET",
                "An impact preview cannot contain the same target more than once.");
        }

        long watermark = normalizedTargets.Length == 0
            ? 0
            : normalizedTargets.Max(target => target.ResourceVersion);

        return new ImpactPreview(
            scopeExpression,
            Array.AsReadOnly(normalizedTargets),
            snapshotReference: null,
            normalizedTargets.Length,
            watermark,
            policyVersion,
            createdAt,
            expiresAt,
            impactSummary);
    }

    public static ImpactPreview CreateReferenced(
        string scopeExpression,
        string snapshotReference,
        int targetCount,
        long resourceVersionWatermark,
        string policyVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ImpactSummary impactSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotReference);

        return new ImpactPreview(
            scopeExpression,
            Array.Empty<ImpactTargetSnapshot>(),
            snapshotReference,
            targetCount,
            resourceVersionWatermark,
            policyVersion,
            createdAt,
            expiresAt,
            impactSummary);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public PreviewComparison CompareForDispatch(ImpactPreview current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var changes = new List<string>();
        AddChangeIf(ScopeExpression != current.ScopeExpression, "SCOPE_CHANGED");
        AddChangeIf(TargetCount != current.TargetCount, "TARGET_COUNT_CHANGED");
        AddChangeIf(
            ResourceVersionWatermark != current.ResourceVersionWatermark,
            "RESOURCE_VERSION_WATERMARK_CHANGED");
        AddChangeIf(PolicyVersion != current.PolicyVersion, "POLICY_VERSION_CHANGED");
        AddChangeIf(SnapshotReference != current.SnapshotReference, "SNAPSHOT_REFERENCE_CHANGED");
        AddChangeIf(
            !ResolvedTargets.SequenceEqual(current.ResolvedTargets),
            "TARGET_SET_OR_VERSION_CHANGED");
        AddChangeIf(
            ImpactSummary.ComputeDigest() != current.ImpactSummary.ComputeDigest(),
            "SAFETY_IMPACT_CHANGED");

        return new PreviewComparison(
            changes.Count == 0,
            Array.AsReadOnly(changes.ToArray()));

        void AddChangeIf(bool changed, string code)
        {
            if (changed)
            {
                changes.Add(code);
            }
        }
    }

    public void EnsureDispatchableAgainst(ImpactPreview current, DateTimeOffset now)
    {
        if (IsExpired(now))
        {
            throw new DomainException(
                "IMPACT_PREVIEW_EXPIRED",
                "The approved impact preview has expired.");
        }

        PreviewComparison comparison = CompareForDispatch(current);
        if (!comparison.IsMateriallyEquivalent)
        {
            throw new DomainException(
                "PREVIEW_STALE_REAPPROVAL_REQUIRED",
                $"The impact changed: {string.Join(",", comparison.MaterialChanges)}.");
        }
    }

    private object ToDigestContract() => new
    {
        ScopeExpression,
        ResolvedTargets = ResolvedTargets.Select(target => new
        {
            target.ResourceId,
            target.ResourceVersion,
            target.ExposureClass
        }).ToArray(),
        SnapshotReference,
        TargetCount,
        ResourceVersionWatermark,
        PolicyVersion,
        CreatedAt,
        ExpiresAt,
        ImpactSummary = ImpactSummary.ToDigestContract()
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
