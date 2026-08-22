using System.Security.Cryptography;
using System.Text.Json;

namespace YO4X.LocalSecrets.Windows;

public sealed record LocalCredentialImportEvidence(
    string SchemaVersion,
    string EvidenceAuthority,
    bool CryptographicallyAttested,
    DateTimeOffset GeneratedAtUtc,
    LocalCredentialImportSourceEvidence Source,
    LocalCredentialImportDestinationEvidence Destination,
    LocalCredentialImportToolEvidence Tool,
    IReadOnlyList<LocalCredentialImportRunEvidence> Runs,
    bool SecretsRendered,
    string Protection,
    string EvidenceContentSha256)
{
    public const string CurrentSchemaVersion = "yo4x.local-credential-import-evidence.v3";
    public const string UnsignedLocalAuthority = "unsigned-local-observation";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static LocalCredentialImportEvidence Create(
        LocalCredentialImportResult result,
        string entryAssemblySha256,
        string boundaryAssemblySha256,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateDigest(result.SourceSha256, nameof(result));
        if (result.SourceByteCount is < 1 or > Mt5CredentialFileParser.MaximumSourceBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "The evidence source byte count is outside the accepted credential-source bounds.");
        }

        if (result.Writes.Count is < 1 or > Mt5CredentialFileParser.MaximumCredentials)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "The evidence credential count is outside the accepted import bounds.");
        }

        ValidateDigest(entryAssemblySha256, nameof(entryAssemblySha256));
        ValidateDigest(boundaryAssemblySha256, nameof(boundaryAssemblySha256));
        ValidateDigest(result.DestinationVaultIdentitySha256, nameof(result));
        var runs = Array.AsReadOnly([
            LocalCredentialImportRunEvidence.FromResult(result)
        ]);
        var payload = new LocalCredentialImportEvidencePayload(
            CurrentSchemaVersion,
            UnsignedLocalAuthority,
            CryptographicallyAttested: false,
            generatedAtUtc.ToUniversalTime(),
            new LocalCredentialImportSourceEvidence(
                result.SourceSha256,
                result.SourceByteCount),
            new LocalCredentialImportDestinationEvidence(
                result.DestinationVaultIdentitySha256,
                "root-user-bound-vault-identity-sha256"),
            new LocalCredentialImportToolEvidence(
                entryAssemblySha256.ToLowerInvariant(),
                boundaryAssemblySha256.ToLowerInvariant(),
                "entry-and-local-secrets-assembly-sha256"),
            runs,
            SecretsRendered: false,
            Protection: "windows-dpapi-current-user");

        return FromPayload(payload);
    }

    public bool HasValidContentHash()
    {
        try
        {
            ValidateDigest(EvidenceContentSha256, nameof(EvidenceContentSha256));
            LocalCredentialImportEvidencePayload payload = ToPayload();
            string expected = ComputePayloadSha256(payload);
            byte[] expectedBytes = Convert.FromHexString(expected);
            byte[] actualBytes = Convert.FromHexString(EvidenceContentSha256);
            try
            {
                return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedBytes);
                CryptographicOperations.ZeroMemory(actualBytes);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static LocalCredentialImportEvidence FromPayload(
        LocalCredentialImportEvidencePayload payload) => new(
            payload.SchemaVersion,
            payload.EvidenceAuthority,
            payload.CryptographicallyAttested,
            payload.GeneratedAtUtc,
            payload.Source,
            payload.Destination,
            payload.Tool,
            payload.Runs,
            payload.SecretsRendered,
            payload.Protection,
            ComputePayloadSha256(payload));

    private LocalCredentialImportEvidencePayload ToPayload() => new(
        SchemaVersion,
        EvidenceAuthority,
        CryptographicallyAttested,
        GeneratedAtUtc.ToUniversalTime(),
        Source,
        Destination,
        Tool,
        Runs,
        SecretsRendered,
        Protection);

    private static string ComputePayloadSha256(LocalCredentialImportEvidencePayload payload)
    {
        byte[] canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        byte[] digest = SHA256.HashData(canonicalPayload);
        try
        {
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalPayload);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static void ValidateDigest(string digest, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest, parameterName);
        if (digest.Length != 64 || digest.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", parameterName);
        }
    }

    private sealed record LocalCredentialImportEvidencePayload(
        string SchemaVersion,
        string EvidenceAuthority,
        bool CryptographicallyAttested,
        DateTimeOffset GeneratedAtUtc,
        LocalCredentialImportSourceEvidence Source,
        LocalCredentialImportDestinationEvidence Destination,
        LocalCredentialImportToolEvidence Tool,
        IReadOnlyList<LocalCredentialImportRunEvidence> Runs,
        bool SecretsRendered,
        string Protection);
}

public sealed record LocalCredentialImportSourceEvidence(
    string Sha256,
    int ByteCount);

public sealed record LocalCredentialImportDestinationEvidence(
    string VaultIdentitySha256,
    string Binding);

public sealed record LocalCredentialImportToolEvidence(
    string EntryAssemblySha256,
    string BoundaryAssemblySha256,
    string Binding);

public sealed record LocalCredentialImportRunEvidence(
    string Mode,
    int CredentialCount,
    int Created,
    int Unchanged,
    int Rotated)
{
    internal static LocalCredentialImportRunEvidence FromResult(
        LocalCredentialImportResult result) => new(
            result.Mode == LocalCredentialWriteMode.Rotate
                ? "rotate"
                : "createOrVerify",
            result.CredentialCount,
            result.Writes.Count(write =>
                write.Disposition == LocalCredentialWriteDisposition.Created),
            result.Writes.Count(write =>
                write.Disposition == LocalCredentialWriteDisposition.Unchanged),
            result.Writes.Count(write =>
                write.Disposition == LocalCredentialWriteDisposition.Rotated));
}
