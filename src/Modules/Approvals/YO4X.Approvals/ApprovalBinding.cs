using YO4X.BuildingBlocks;

namespace YO4X.Approvals;

public sealed record ExpectedResourceVersion
{
    public ExpectedResourceVersion(Guid resourceId, long version)
    {
        if (resourceId == Guid.Empty)
        {
            throw new DomainException(
                "APPROVAL_RESOURCE_ID_EMPTY",
                "An expected resource identifier cannot be empty.");
        }

        if (version < 0)
        {
            throw new DomainException(
                "APPROVAL_RESOURCE_VERSION_INVALID",
                "An expected resource version cannot be negative.");
        }

        ResourceId = resourceId;
        Version = version;
    }

    public Guid ResourceId { get; }

    public long Version { get; }
}

/// <summary>
/// The exact immutable material approved for a command. Any edited payload,
/// preview, resource version, policy version, reason, or expiry changes the digest.
/// </summary>
public sealed class ApprovalBinding
{
    public ApprovalBinding(
        Guid commandId,
        string commandType,
        Guid requesterId,
        string normalizedPayloadDigest,
        string impactPreviewDigest,
        IEnumerable<ExpectedResourceVersion> expectedResourceVersions,
        string policyVersion,
        string reason,
        string? ticketReference,
        DateTimeOffset expiresAt)
    {
        if (commandId == Guid.Empty || requesterId == Guid.Empty)
        {
            throw new DomainException(
                "APPROVAL_BINDING_ID_EMPTY",
                "Command and requester identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        EnsureSha256(normalizedPayloadDigest, nameof(normalizedPayloadDigest));
        EnsureSha256(impactPreviewDigest, nameof(impactPreviewDigest));
        ArgumentNullException.ThrowIfNull(expectedResourceVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ExpectedResourceVersion[] normalizedVersions = expectedResourceVersions
            .OrderBy(item => item.ResourceId)
            .ToArray();
        if (normalizedVersions.Select(item => item.ResourceId).Distinct().Count()
            != normalizedVersions.Length)
        {
            throw new DomainException(
                "APPROVAL_RESOURCE_VERSION_DUPLICATE",
                "An approval binding cannot contain duplicate resource identifiers.");
        }

        CommandId = commandId;
        CommandType = commandType.Trim();
        RequesterId = requesterId;
        NormalizedPayloadDigest = normalizedPayloadDigest.ToLowerInvariant();
        ImpactPreviewDigest = impactPreviewDigest.ToLowerInvariant();
        ExpectedResourceVersions = Array.AsReadOnly(normalizedVersions);
        PolicyVersion = policyVersion.Trim();
        Reason = reason.Trim();
        TicketReference = NormalizeOptional(ticketReference);
        ExpiresAt = expiresAt.ToUniversalTime();
        Digest = CanonicalJson.Sha256(ToDigestContract());
    }

    public Guid CommandId { get; }

    public string CommandType { get; }

    public Guid RequesterId { get; }

    public string NormalizedPayloadDigest { get; }

    public string ImpactPreviewDigest { get; }

    public IReadOnlyList<ExpectedResourceVersion> ExpectedResourceVersions { get; }

    public string PolicyVersion { get; }

    public string Reason { get; }

    public string? TicketReference { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string Digest { get; }

    public bool Matches(ApprovalBinding? current) =>
        current is not null && string.Equals(Digest, current.Digest, StringComparison.Ordinal);

    private object ToDigestContract() => new
    {
        CommandId,
        CommandType,
        RequesterId,
        NormalizedPayloadDigest,
        ImpactPreviewDigest,
        ExpectedResourceVersions = ExpectedResourceVersions.Select(item => new
        {
            item.ResourceId,
            item.Version
        }).ToArray(),
        PolicyVersion,
        Reason,
        TicketReference,
        ExpiresAt
    };

    private static void EnsureSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainException(
                "APPROVAL_DIGEST_INVALID",
                "Approval bindings require SHA-256 digests encoded as hexadecimal.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
