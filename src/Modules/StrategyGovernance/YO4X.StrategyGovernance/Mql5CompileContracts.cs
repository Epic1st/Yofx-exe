using System.Security.Cryptography;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public enum Mql5CompileProofState
{
    StaticOnly,
    Blocked,
    Unsupported,
    Failed,
    Proven
}

public enum Mql5IsolatedRunStatus
{
    Completed,
    Failed,
    TimedOut,
    Unsupported
}

public enum Mql5FileCompileStatus
{
    Succeeded,
    Failed
}

public sealed record Mql5IsolationPolicy(
    bool NetworkAccessDisabled,
    bool ReadOnlyRootFileSystem,
    bool EphemeralWorkspace,
    bool HostMountsDisabled,
    bool NoNewPrivileges,
    long MemoryLimitBytes,
    long CpuTimeLimitMilliseconds,
    long WallClockTimeoutMilliseconds,
    int ProcessLimit,
    long TemporaryStorageLimitBytes,
    int CompilerOutputLimitBytes);

public sealed record Mql5PinnedToolchain(
    string RunnerImageDigest,
    string MetaEditorSha256,
    string MetaEditorVersion,
    string PlatformLibrarySnapshotSha256);

public sealed class Mql5ApprovedPlatformLibrarySnapshot
{
    public const string ApprovalSchemaVersion =
        "yo4x.mql5-approved-platform-library-snapshot.v1";

    public Mql5ApprovedPlatformLibrarySnapshot(
        string approvalId,
        string snapshotSha256,
        string provenanceEvidenceSha256)
    {
        if (!Mql5CompileValidation.IsSafeToken(approvalId)
            || !Mql5CompileValidation.IsExactSha256(snapshotSha256)
            || !Mql5CompileValidation.IsExactSha256(provenanceEvidenceSha256))
        {
            throw new ArgumentException("The approved platform-library snapshot is invalid.", nameof(approvalId));
        }

        ApprovalId = approvalId;
        SnapshotSha256 = snapshotSha256;
        ProvenanceEvidenceSha256 = provenanceEvidenceSha256;
        ApprovalSha256 = CanonicalJson.Sha256(new
        {
            SchemaVersion = ApprovalSchemaVersion,
            ApprovalId,
            SnapshotSha256,
            ProvenanceEvidenceSha256
        });
    }

    public string ApprovalId { get; }

    public string SnapshotSha256 { get; }

    public string ProvenanceEvidenceSha256 { get; }

    public string ApprovalSha256 { get; }
}

public sealed class Mql5ApprovedCompileProfile
{
    private readonly HashSet<string> approvedSigningKeyIds;
    private readonly IReadOnlyList<string> orderedSigningKeyIds;

    public Mql5ApprovedCompileProfile(
        string profileId,
        Mql5PinnedToolchain toolchain,
        Mql5ApprovedPlatformLibrarySnapshot platformLibrarySnapshot,
        Mql5IsolationPolicy maximumIsolationPolicy,
        IEnumerable<string> approvedSigningKeyIds)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(platformLibrarySnapshot);
        ArgumentNullException.ThrowIfNull(maximumIsolationPolicy);
        ArgumentNullException.ThrowIfNull(approvedSigningKeyIds);
        if (!Mql5CompileValidation.IsSafeToken(profileId)
            || !Mql5CompileValidation.IsExactImageDigest(toolchain.RunnerImageDigest)
            || !Mql5CompileValidation.IsExactSha256(toolchain.MetaEditorSha256)
            || !Mql5CompileValidation.IsSafeToken(toolchain.MetaEditorVersion)
            || !Mql5CompileValidation.IsExactSha256(toolchain.PlatformLibrarySnapshotSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                toolchain.PlatformLibrarySnapshotSha256,
                platformLibrarySnapshot.SnapshotSha256)
            || !maximumIsolationPolicy.NetworkAccessDisabled
            || !maximumIsolationPolicy.ReadOnlyRootFileSystem
            || !maximumIsolationPolicy.EphemeralWorkspace
            || !maximumIsolationPolicy.HostMountsDisabled
            || !maximumIsolationPolicy.NoNewPrivileges
            || Mql5CompileValidation.ValidateIsolationPolicy(maximumIsolationPolicy) is not null)
        {
            throw new ArgumentException("The approved compile profile is invalid.", nameof(profileId));
        }

        string[] signingKeys = approvedSigningKeyIds.Take(33).ToArray();
        if (signingKeys.Length is < 1 or > 32
            || signingKeys.Any(static key => !Mql5CompileValidation.IsSafeToken(key))
            || signingKeys.Distinct(StringComparer.Ordinal).Count() != signingKeys.Length)
        {
            throw new ArgumentException("The approved signing-key scope is invalid.", nameof(approvedSigningKeyIds));
        }

        Array.Sort(signingKeys, StringComparer.Ordinal);

        ProfileId = profileId;
        Toolchain = toolchain;
        PlatformLibrarySnapshot = platformLibrarySnapshot;
        MaximumIsolationPolicy = maximumIsolationPolicy;
        this.approvedSigningKeyIds = new HashSet<string>(signingKeys, StringComparer.Ordinal);
        orderedSigningKeyIds = Array.AsReadOnly(signingKeys);
        ProfileSha256 = CanonicalJson.Sha256(new
        {
            ProfileId,
            Toolchain,
            PlatformLibrarySnapshotApprovalSha256 = PlatformLibrarySnapshot.ApprovalSha256,
            MaximumIsolationPolicy,
            ApprovedSigningKeyIds = signingKeys
        });
    }

    public string ProfileId { get; }

    public string ProfileSha256 { get; }

    public Mql5PinnedToolchain Toolchain { get; }

    public Mql5ApprovedPlatformLibrarySnapshot PlatformLibrarySnapshot { get; }

    public Mql5IsolationPolicy MaximumIsolationPolicy { get; }

    public IReadOnlyList<string> ApprovedSigningKeyIds => orderedSigningKeyIds;

    public bool ApprovesToolchain(Mql5PinnedToolchain candidate) =>
        candidate == Toolchain;

    public bool ApprovesIsolationPolicy(Mql5IsolationPolicy candidate) =>
        candidate.NetworkAccessDisabled
        && candidate.ReadOnlyRootFileSystem
        && candidate.EphemeralWorkspace
        && candidate.HostMountsDisabled
        && candidate.NoNewPrivileges
        && candidate.MemoryLimitBytes <= MaximumIsolationPolicy.MemoryLimitBytes
        && candidate.CpuTimeLimitMilliseconds <= MaximumIsolationPolicy.CpuTimeLimitMilliseconds
        && candidate.WallClockTimeoutMilliseconds <= MaximumIsolationPolicy.WallClockTimeoutMilliseconds
        && candidate.ProcessLimit <= MaximumIsolationPolicy.ProcessLimit
        && candidate.TemporaryStorageLimitBytes <= MaximumIsolationPolicy.TemporaryStorageLimitBytes
        && candidate.CompilerOutputLimitBytes <= MaximumIsolationPolicy.CompilerOutputLimitBytes;

    public bool ApprovesSigningKey(string signingKeyId) =>
        approvedSigningKeyIds.Contains(signingKeyId);
}

public sealed record Mql5CompileJob(
    Guid JobId,
    DateTimeOffset RequestedAtUtc,
    Mql5CorpusManifest StaticManifest,
    Mql5ConversionCorpusEvidence ConversionEvidence,
    IReadOnlyList<Mql5SourceDocument> Sources,
    Mql5TargetCompilePackageDossier CompilePackage,
    Mql5PinnedToolchain Toolchain,
    Mql5IsolationPolicy IsolationPolicy);

public sealed record Mql5IsolatedCompileRequest(
    Guid JobId,
    DateTimeOffset RequestedAtUtc,
    string ChallengeSha256,
    string CompileProfileId,
    string CompileProfileSha256,
    string CorpusSha256,
    string StaticManifestSha256,
    string ConversionEvidenceSha256,
    string ConversionEvidenceContentSha256,
    string DependencyGraphSha256,
    string CompilePackageSha256,
    string SourceClosureSha256,
    string TargetRelativePath,
    Mql5TargetCompilePackageDossier CompilePackage,
    IReadOnlyList<Mql5SourceDocument> Sources,
    Mql5PinnedToolchain Toolchain,
    Mql5IsolationPolicy IsolationPolicy);

public sealed record Mql5RunnerAttestationDescriptor(
    string SchemaVersion,
    Guid JobId,
    string ChallengeSha256,
    string CompileProfileId,
    string CompileProfileSha256,
    string CorpusSha256,
    string StaticManifestSha256,
    string ConversionEvidenceSha256,
    string ConversionEvidenceContentSha256,
    string DependencyGraphSha256,
    string CompilePackageSha256,
    string SourceClosureSha256,
    string TargetRelativePath,
    string RunnerId,
    string RunnerSessionId,
    string RunnerImageDigest,
    string MetaEditorSha256,
    string MetaEditorVersion,
    string PlatformLibrarySnapshotSha256,
    Mql5IsolationPolicy IsolationPolicy,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    Mql5IsolatedRunStatus RunStatus,
    string OutputSha256,
    int OutputRecordCount);

public sealed class Mql5RunnerAttestation
{
    private readonly byte[] signature;

    public Mql5RunnerAttestation(
        Mql5RunnerAttestationDescriptor? descriptor,
        string? algorithm,
        string? signingKeyId,
        byte[]? signature,
        string? signatureSha256,
        string? signedPayloadSha256)
    {
        if (signature is { Length: > Mql5CompileValidation.MaximumAttestationSignatureBytes })
        {
            throw new ArgumentOutOfRangeException(
                nameof(signature),
                "The runner attestation signature exceeds the absolute input limit.");
        }

        Descriptor = descriptor;
        Algorithm = algorithm;
        SigningKeyId = signingKeyId;
        this.signature = signature?.ToArray() ?? [];
        SignatureSha256 = signatureSha256;
        SignedPayloadSha256 = signedPayloadSha256;
    }

    public Mql5RunnerAttestationDescriptor? Descriptor { get; }

    public string? Algorithm { get; }

    public string? SigningKeyId { get; }

    public string? SignatureSha256 { get; }

    public string? SignedPayloadSha256 { get; }

    public byte[] GetSignature() => signature.ToArray();
}

public sealed class Mql5IsolatedCompileResponse
{
    private readonly byte[] compilerOutput;

    public Mql5IsolatedCompileResponse(Mql5RunnerAttestation? attestation, byte[]? compilerOutput)
    {
        if (compilerOutput is { Length: > Mql5CompileValidation.MaximumCompilerOutputBytes })
        {
            throw new ArgumentOutOfRangeException(
                nameof(compilerOutput),
                "The compiler output exceeds the absolute input limit.");
        }

        Attestation = attestation;
        this.compilerOutput = compilerOutput?.ToArray() ?? [];
    }

    public Mql5RunnerAttestation? Attestation { get; }

    public int CompilerOutputLength => compilerOutput.Length;

    public byte[] CopyCompilerOutput(int maximumBytes)
    {
        if (maximumBytes < 0 || compilerOutput.Length > maximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        return compilerOutput.ToArray();
    }

    internal void ClearCompilerOutput() =>
        CryptographicOperations.ZeroMemory(compilerOutput);
}

public sealed record Mql5CompilerDiagnosticEvidence(
    string Severity,
    string Code,
    int Line,
    int Column,
    string MessageSha256);

public sealed record Mql5FileCompileEvidence(
    string RelativePath,
    string SourceSha256,
    Mql5FileCompileStatus Status,
    int ExitCode,
    string? ArtifactSha256,
    string? RepeatArtifactSha256,
    IReadOnlyList<Mql5CompilerDiagnosticEvidence> Diagnostics,
    string EvidenceSha256);

public sealed record Mql5CompileEvidence(
    Guid JobId,
    string CorpusSha256,
    string? CompileProfileId,
    string? CompileProfileSha256,
    string? StaticManifestSha256,
    string? ConversionEvidenceSha256,
    string? ConversionEvidenceContentSha256,
    string? DependencyGraphSha256,
    string? CompilePackageSha256,
    string? SourceClosureSha256,
    string? TargetRelativePath,
    Mql5CompileProofState State,
    string ReasonCode,
    string RunnerImageDigest,
    string MetaEditorSha256,
    string? MetaEditorVersion,
    string? PlatformLibrarySnapshotSha256,
    string? ToolchainSha256,
    string? IsolationPolicySha256,
    string? RunnerId,
    string? RunnerSessionId,
    string? AttestationSigningKeyId,
    string? AttestationSha256,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<Mql5FileCompileEvidence> Files)
{
    public bool MetaEditorCompileProven => State == Mql5CompileProofState.Proven;
}

public interface IMql5IsolatedCompileRunner
{
    Task<Mql5IsolatedCompileResponse> CompileAsync(
        Mql5IsolatedCompileRequest request,
        CancellationToken cancellationToken);
}

public interface IMql5RunnerAttestationVerifier
{
    bool Verify(
        string signingKeyId,
        string algorithm,
        ReadOnlySpan<byte> signature,
        string canonicalPayload);
}

public sealed class Mql5RunnerUnavailableException : Exception
{
    public Mql5RunnerUnavailableException(string reasonCode)
        : base("No approved isolated MQL5 compile runner is available.")
    {
        ReasonCode = Mql5CompileValidation.IsSafeReasonCode(reasonCode)
            ? reasonCode
            : "ISOLATED_RUNNER_UNAVAILABLE";
    }

    public string ReasonCode { get; }
}

public sealed class UnavailableMql5IsolatedCompileRunner : IMql5IsolatedCompileRunner
{
    public Task<Mql5IsolatedCompileResponse> CompileAsync(
        Mql5IsolatedCompileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new Mql5RunnerUnavailableException("ISOLATED_RUNNER_NOT_CONFIGURED");
    }
}

public static class Mql5CompileProofTransitions
{
    public static bool CanTransition(Mql5CompileProofState from, Mql5CompileProofState to) => from switch
    {
        Mql5CompileProofState.StaticOnly => to is Mql5CompileProofState.StaticOnly
            or Mql5CompileProofState.Blocked
            or Mql5CompileProofState.Unsupported
            or Mql5CompileProofState.Failed
            or Mql5CompileProofState.Proven,
        Mql5CompileProofState.Blocked or Mql5CompileProofState.Unsupported or Mql5CompileProofState.Failed =>
            to is Mql5CompileProofState.Blocked
                or Mql5CompileProofState.Unsupported
                or Mql5CompileProofState.Failed
                or Mql5CompileProofState.Proven,
        Mql5CompileProofState.Proven => to == Mql5CompileProofState.Proven,
        _ => false
    };
}

internal static class Mql5CompileValidation
{
    public const string AttestationSchemaVersion = "yo4x.mql5-runner-attestation.v3";
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256_DER";
    public const int MaximumCompilerOutputBytes = 16 * 1024 * 1024;
    public const int MaximumAttestationSignatureBytes = 256;
    public const int MaximumSourceFileCount = 10_000;
    public const long MaximumSourceFileBytes = 4L * 1024 * 1024;
    public const long MaximumSourceCorpusBytes = 256L * 1024 * 1024;

    public static bool IsExactSha256(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsExactImageDigest(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && IsExactSha256(value[7..]);

    public static bool IsSafeToken(string? value, int maximum = 200) => value is { Length: >= 1 }
        && value.Length <= maximum
        && value.All(static character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');

    public static bool IsSafeReasonCode(string? value) => value is { Length: >= 1 and <= 100 }
        && value.All(static character => character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character == '_');

    public static bool IsSafeRelativeSourcePath(string? value)
    {
        if (value is not { Length: >= 1 and <= 500 }
            || value[0] == '/'
            || value.Contains('\\')
            || value.Contains("//", StringComparison.Ordinal)
            || Path.IsPathRooted(value))
        {
            return false;
        }

        string[] segments = value.Split('/');
        return segments.All(static segment => segment is not "" and not "." and not ".."
            && segment.Length <= 255
            && !segment.EndsWith(' ')
            && !segment.EndsWith('.')
            && !IsWindowsDeviceName(segment)
            && segment.All(static character => char.IsAsciiLetterOrDigit(character)
                || character is ' ' or '-' or '_' or '.' or '(' or ')' or '[' or ']'
                    or '@' or ',' or '\u00b4'));
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        string stem = Path.GetFileNameWithoutExtension(segment);
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9';
    }

    public static bool FixedTimeHexEquals(string left, string right)
    {
        if (!IsExactSha256(left) || !IsExactSha256(right))
        {
            return false;
        }

        byte[] leftBytes = Convert.FromHexString(left);
        byte[] rightBytes = Convert.FromHexString(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    public static string? ValidateSourceReferences(Mql5SourceDocument[] sources)
    {
        if (sources.Length is < 1 or > MaximumSourceFileCount)
        {
            return "SOURCE_CORPUS_INVALID";
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (Mql5SourceDocument? source in sources)
        {
            if (source is null
                || source.Content is null
                || !IsSafeRelativeSourcePath(source.RelativePath))
            {
                return "SOURCE_PATH_UNSAFE_FOR_RUNNER";
            }

            if (!paths.Add(source.RelativePath))
            {
                return "SOURCE_CORPUS_INVALID";
            }

            if (source.Content.LongLength > MaximumSourceFileBytes)
            {
                return "SOURCE_SIZE_LIMIT_EXCEEDED";
            }

            try
            {
                totalBytes = checked(totalBytes + source.Content.LongLength);
            }
            catch (OverflowException)
            {
                return "SOURCE_SIZE_LIMIT_EXCEEDED";
            }

            if (totalBytes > MaximumSourceCorpusBytes)
            {
                return "SOURCE_SIZE_LIMIT_EXCEEDED";
            }
        }

        return null;
    }

    public static string? ValidateIsolationPolicy(Mql5IsolationPolicy policy)
    {
        if (!policy.NetworkAccessDisabled
            || !policy.ReadOnlyRootFileSystem
            || !policy.EphemeralWorkspace
            || !policy.HostMountsDisabled
            || !policy.NoNewPrivileges)
        {
            return "ISOLATION_POLICY_NOT_FAIL_CLOSED";
        }

        return policy.MemoryLimitBytes is < 64 * 1024 * 1024 or > 4L * 1024 * 1024 * 1024
            || policy.CpuTimeLimitMilliseconds is < 1_000 or > 10 * 60 * 1_000
            || policy.WallClockTimeoutMilliseconds is < 1_000 or > 15 * 60 * 1_000
            || policy.ProcessLimit is < 1 or > 64
            || policy.TemporaryStorageLimitBytes is < 1024 * 1024 or > 4L * 1024 * 1024 * 1024
            || policy.CompilerOutputLimitBytes is < 1024 or > MaximumCompilerOutputBytes
                ? "ISOLATION_RESOURCE_LIMIT_INVALID"
                : null;
    }
}
