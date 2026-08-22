using System.Security.Cryptography;

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

public sealed record Mql5CompileJob(
    Guid JobId,
    DateTimeOffset RequestedAtUtc,
    Mql5CorpusManifest StaticManifest,
    IReadOnlyList<Mql5SourceDocument> Sources,
    Mql5PinnedToolchain Toolchain,
    Mql5IsolationPolicy IsolationPolicy);

public sealed record Mql5IsolatedCompileRequest(
    Guid JobId,
    DateTimeOffset RequestedAtUtc,
    string ChallengeSha256,
    string CorpusSha256,
    IReadOnlyList<Mql5SourceDocument> Sources,
    IReadOnlyList<string> CompileTargets,
    Mql5PinnedToolchain Toolchain,
    Mql5IsolationPolicy IsolationPolicy);

public sealed record Mql5RunnerAttestationDescriptor(
    string SchemaVersion,
    Guid JobId,
    string ChallengeSha256,
    string CorpusSha256,
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
        Attestation = attestation;
        this.compilerOutput = compilerOutput?.ToArray() ?? [];
    }

    public Mql5RunnerAttestation? Attestation { get; }

    public byte[] GetCompilerOutput() => compilerOutput.ToArray();
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
    Mql5CompileProofState State,
    string ReasonCode,
    string RunnerImageDigest,
    string MetaEditorSha256,
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
    public const string AttestationSchemaVersion = "yo4x.mql5-runner-attestation.v1";
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256_DER";

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
                || character is ' ' or '-' or '_' or '.' or '(' or ')' or '[' or ']'));
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
}
