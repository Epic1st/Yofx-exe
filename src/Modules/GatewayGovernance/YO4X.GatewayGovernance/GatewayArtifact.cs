using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.GatewayGovernance;

public enum GatewayArtifactState
{
    Quarantined,
    EvidenceReady,
    DemoCanaryApproved,
    Revoked
}

public sealed partial record GatewayEvidence(
    string ProvenanceDigest,
    string SbomDigest,
    string LicenseEvidenceDigest,
    string NetworkEvidenceDigest,
    string CompatibilityEvidenceDigest,
    bool WrittenCloudRightsConfirmed,
    bool ProductionArtifactConfirmed,
    DateTimeOffset RecordedAt)
{
    public string EvidenceDigest => CanonicalJson.Sha256(this);

    public bool IsComplete =>
        WrittenCloudRightsConfirmed
        && ProductionArtifactConfirmed
        && IsDigest(ProvenanceDigest)
        && IsDigest(SbomDigest)
        && IsDigest(LicenseEvidenceDigest)
        && IsDigest(NetworkEvidenceDigest)
        && IsDigest(CompatibilityEvidenceDigest);

    private static bool IsDigest(string value) => DigestPattern().IsMatch(value);

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

public sealed record GatewayArtifactEvent(
    string EventType,
    string ActorId,
    string Reason,
    string? ApprovalId,
    DateTimeOffset OccurredAt);

public sealed partial class GatewayArtifact : VersionedAggregate
{
    private readonly List<GatewayArtifactEvent> _events = [];

    private GatewayArtifact(
        Guid id,
        string quarantineObjectReference,
        string sha256,
        long sizeBytes,
        string vendorIdentity,
        string provenanceReference,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        QuarantineObjectReference = quarantineObjectReference;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        VendorIdentity = vendorIdentity;
        ProvenanceReference = provenanceReference;
        State = GatewayArtifactState.Quarantined;
    }

    public string QuarantineObjectReference { get; }

    public string Sha256 { get; }

    public long SizeBytes { get; }

    public string VendorIdentity { get; }

    public string ProvenanceReference { get; }

    public GatewayArtifactState State { get; private set; }

    public GatewayEvidence? Evidence { get; private set; }

    public IReadOnlyList<GatewayArtifactEvent> Events => _events;

    public static GatewayArtifact RegisterQuarantined(
        string quarantineObjectReference,
        string sha256,
        long sizeBytes,
        string vendorIdentity,
        string provenanceReference,
        string actorId,
        IClock clock)
    {
        ValidateObjectReference(quarantineObjectReference);
        ValidateDigest(sha256, nameof(sha256));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenanceReference);

        var artifact = new GatewayArtifact(
            Identifiers.NewId(),
            quarantineObjectReference,
            sha256.ToLowerInvariant(),
            sizeBytes,
            vendorIdentity,
            provenanceReference,
            clock.UtcNow);
        artifact._events.Add(new GatewayArtifactEvent("REGISTERED_QUARANTINED", actorId, "CONTROLLED_INTAKE", null, clock.UtcNow));
        return artifact;
    }

    public void AttachEvidence(GatewayEvidence evidence, string actorId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (State == GatewayArtifactState.Revoked)
        {
            throw new DomainException("GATEWAY_ARTIFACT_REVOKED", "Evidence cannot reactivate a revoked gateway artifact.");
        }

        if (!evidence.IsComplete)
        {
            throw new DomainException("GATEWAY_EVIDENCE_INCOMPLETE", "All gateway rights, provenance, SBOM, network, and compatibility evidence is required.");
        }

        Evidence = evidence;
        State = GatewayArtifactState.EvidenceReady;
        _events.Add(new GatewayArtifactEvent("EVIDENCE_ATTACHED", actorId, evidence.EvidenceDigest, null, occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }

    public void ApproveDemoCanary(
        string expectedArtifactDigest,
        string expectedEvidenceDigest,
        string approvalId,
        string actorId,
        DateTimeOffset occurredAt)
    {
        if (State != GatewayArtifactState.EvidenceReady || Evidence is null)
        {
            throw new DomainException("GATEWAY_EVIDENCE_REQUIRED", "Complete gateway evidence is required before canary approval.");
        }

        if (!string.Equals(Sha256, expectedArtifactDigest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Evidence.EvidenceDigest, expectedEvidenceDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("GATEWAY_DIGEST_MISMATCH", "The approval is not bound to the current artifact and evidence digests.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        State = GatewayArtifactState.DemoCanaryApproved;
        _events.Add(new GatewayArtifactEvent("DEMO_CANARY_APPROVED", actorId, "EXACT_DIGEST_APPROVAL", approvalId, occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }

    public void Revoke(string reason, string actorId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (State == GatewayArtifactState.Revoked)
        {
            return;
        }

        State = GatewayArtifactState.Revoked;
        _events.Add(new GatewayArtifactEvent("REVOKED", actorId, reason, null, occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }

    private static void ValidateObjectReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("s3" or "az" or "gs")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("A controlled immutable object reference is required.", nameof(value));
        }
    }

    private static void ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DigestPattern().IsMatch(value))
        {
            throw new ArgumentException("A SHA-256 digest is required.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

public sealed record GatewayReleaseAssignment(
    Guid ReleaseId,
    Guid ArtifactId,
    string ArtifactDigest,
    string EvidenceDigest,
    string Environment,
    string PreviewDigest,
    string ApprovalId,
    DateTimeOffset AssignedAt);
