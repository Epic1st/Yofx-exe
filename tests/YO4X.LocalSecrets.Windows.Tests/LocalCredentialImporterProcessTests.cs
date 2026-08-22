using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using YO4X.LocalSecrets.Windows;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class LocalCredentialImporterProcessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SyntheticImportEmitsValidatedRedactedEvidenceAndReplaysIdempotently()
    {
        using var scope = new TemporaryImporterScope();
        string sourcePath = Path.Combine(scope.Root, "synthetic-credentials.txt");
        string vaultRoot = Path.Combine(scope.Root, "vault");
        const string firstSecret = "synthetic-process-secret-one";
        const string secondSecret = "synthetic-process-secret-two";
        byte[] source = Encoding.UTF8.GetBytes(
            $"MT5 Login: 12345678\nMT5 Password: {firstSecret}\nMT5 Server: Broker-One\n\n"
            + $"MT5 Login: 87654321\nMT5 Password: {secondSecret}\nMT5 Server: Broker-Two\n");
        await File.WriteAllBytesAsync(sourcePath, source);
        string sourceDigest = Digest(source);
        string importerPath = FindImporterExecutable();

        ProcessResult first = await RunImporterAsync(
            importerPath,
            sourcePath,
            sourceDigest,
            vaultRoot);

        Assert.Equal(0, first.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(first.StandardError), first.StandardError);
        Assert.DoesNotContain(firstSecret, first.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(secondSecret, first.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Broker-One", first.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Broker-Two", first.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", first.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("87654321", first.StandardOutput, StringComparison.Ordinal);
        LocalCredentialImportEvidence firstEvidence = DeserializeEvidence(first.StandardOutput);
        Assert.True(firstEvidence.HasValidContentHash());
        Assert.False(firstEvidence.CryptographicallyAttested);
        Assert.False(firstEvidence.SecretsRendered);
        Assert.Equal(sourceDigest, firstEvidence.Source.Sha256);
        Assert.Equal(source.Length, firstEvidence.Source.ByteCount);
        Assert.Equal(64, firstEvidence.Destination.VaultIdentitySha256.Length);
        Assert.Equal(
            "root-user-bound-vault-identity-sha256",
            firstEvidence.Destination.Binding);
        LocalCredentialImportRunEvidence firstRun = Assert.Single(firstEvidence.Runs);
        Assert.Equal(2, firstRun.Created);
        Assert.Equal(0, firstRun.Unchanged);
        Assert.Equal(0, firstRun.Rotated);

        string outputDirectory = Path.GetDirectoryName(importerPath)
            ?? throw new InvalidOperationException("The importer output directory is unavailable.");
        Assert.Equal(
            DigestFile(Path.Combine(outputDirectory, "YO4X.LocalCredentialImporter.dll")),
            firstEvidence.Tool.EntryAssemblySha256);
        Assert.Equal(
            DigestFile(Path.Combine(outputDirectory, "YO4X.LocalSecrets.Windows.dll")),
            firstEvidence.Tool.BoundaryAssemblySha256);

        ProcessResult replay = await RunImporterAsync(
            importerPath,
            sourcePath,
            sourceDigest,
            vaultRoot);

        Assert.Equal(0, replay.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(replay.StandardError), replay.StandardError);
        Assert.DoesNotContain(firstSecret, replay.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(secondSecret, replay.StandardOutput, StringComparison.Ordinal);
        LocalCredentialImportEvidence replayEvidence = DeserializeEvidence(replay.StandardOutput);
        Assert.True(replayEvidence.HasValidContentHash());
        Assert.Equal(
            firstEvidence.Destination.VaultIdentitySha256,
            replayEvidence.Destination.VaultIdentitySha256);
        LocalCredentialImportRunEvidence replayRun = Assert.Single(replayEvidence.Runs);
        Assert.Equal(0, replayRun.Created);
        Assert.Equal(2, replayRun.Unchanged);
        Assert.Equal(0, replayRun.Rotated);
        Assert.Equal(source, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task RecoveryResidueUsesDedicatedExitCodeAndRendersNoSourceMaterial()
    {
        using var scope = new TemporaryImporterScope();
        string sourcePath = Path.Combine(scope.Root, "synthetic-credentials.txt");
        string vaultRoot = Path.Combine(scope.Root, "vault");
        const string secret = "synthetic-recovery-secret";
        byte[] source = Encoding.UTF8.GetBytes(
            $"MT5 Login: 12345678\nMT5 Password: {secret}\nMT5 Server: Broker-One\n");
        await File.WriteAllBytesAsync(sourcePath, source);

        ProcessResult initialize = await RunImporterAsync(
            FindImporterExecutable(),
            sourcePath,
            Digest(source),
            vaultRoot);
        Assert.Equal(0, initialize.ExitCode);
        await File.WriteAllBytesAsync(
            Path.Combine(vaultRoot, "orphan.yo4xcred.stage-interrupted"),
            [0x01]);

        ProcessResult result = await RunImporterAsync(
            FindImporterExecutable(),
            sourcePath,
            Digest(source),
            vaultRoot);

        Assert.Equal(6, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Equal("credential_import_manual_recovery_required", result.StandardError.Trim());
        Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task DigestMismatchUsesDedicatedExitCodeAndRendersNoSourceMaterial()
    {
        using var scope = new TemporaryImporterScope();
        string sourcePath = Path.Combine(scope.Root, "synthetic-credentials.txt");
        const string secret = "synthetic-digest-mismatch-secret";
        byte[] source = Encoding.UTF8.GetBytes(
            $"MT5 Login: 12345678\nMT5 Password: {secret}\nMT5 Server: Broker-One\n");
        await File.WriteAllBytesAsync(sourcePath, source);

        ProcessResult result = await RunImporterAsync(
            FindImporterExecutable(),
            sourcePath,
            new string('0', 64),
            Path.Combine(scope.Root, "vault"));

        Assert.Equal(3, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Equal("credential_import_source_digest_mismatch", result.StandardError.Trim());
        Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task MissingRotationTargetUsesStableConflictClassExitWithoutCreatingIt()
    {
        using var scope = new TemporaryImporterScope();
        string sourcePath = Path.Combine(scope.Root, "synthetic-credentials.txt");
        string vaultRoot = Path.Combine(scope.Root, "vault");
        const string secret = "synthetic-missing-rotation-secret";
        byte[] source = Encoding.UTF8.GetBytes(
            $"MT5 Login: 12345678\nMT5 Password: {secret}\nMT5 Server: Broker-One\n");
        await File.WriteAllBytesAsync(sourcePath, source);

        ProcessResult result = await RunImporterAsync(
            FindImporterExecutable(),
            sourcePath,
            Digest(source),
            vaultRoot,
            rotate: true);

        Assert.Equal(4, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Equal("credential_import_rotation_target_missing", result.StandardError.Trim());
        Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(vaultRoot, "*.yo4xcred"));
    }

    [Fact]
    public async Task InvalidArgumentsUseStableRedactedUsageFailure()
    {
        var startInfo = CreateStartInfo(FindImporterExecutable());

        ProcessResult result = await RunAsync(startInfo);

        Assert.Equal(2, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Equal("credential_import_usage_invalid", result.StandardError.Trim());
    }

    private static LocalCredentialImportEvidence DeserializeEvidence(string json)
    {
        LocalCredentialImportEvidence? evidence = JsonSerializer.Deserialize<LocalCredentialImportEvidence>(
            json,
            JsonOptions);
        return evidence ?? throw new InvalidDataException("The importer returned no evidence payload.");
    }

    private static async Task<ProcessResult> RunImporterAsync(
        string importerPath,
        string sourcePath,
        string sourceDigest,
        string vaultRoot,
        bool rotate = false)
    {
        ProcessStartInfo startInfo = CreateStartInfo(importerPath);
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("--sha256");
        startInfo.ArgumentList.Add(sourceDigest);
        startInfo.ArgumentList.Add("--vault-root");
        startInfo.ArgumentList.Add(vaultRoot);
        if (rotate)
        {
            startInfo.ArgumentList.Add("--rotate");
        }

        return await RunAsync(startInfo);
    }

    private static ProcessStartInfo CreateStartInfo(string importerPath) => new(importerPath)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The credential importer could not be started.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string FindImporterExecutable()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            string projectPath = Path.Combine(
                cursor.FullName,
                "src",
                "Tools",
                "YO4X.LocalCredentialImporter",
                "YO4X.LocalCredentialImporter.csproj");
            if (File.Exists(projectPath))
            {
                string configuration = AppContext.BaseDirectory.Contains(
                    $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Debug"
                    : "Release";
                string executablePath = Path.Combine(
                    Path.GetDirectoryName(projectPath)
                        ?? throw new InvalidOperationException("The importer project directory is unavailable."),
                    "bin",
                    configuration,
                    "net10.0-windows10.0.19041.0",
                    "YO4X.LocalCredentialImporter.exe");
                if (!File.Exists(executablePath))
                {
                    throw new FileNotFoundException(
                        "The credential importer test dependency was not built.",
                        executablePath);
                }

                return executablePath;
            }

            cursor = cursor.Parent;
        }

        throw new FileNotFoundException("The credential importer project was not found.");
    }

    private static string Digest(ReadOnlySpan<byte> bytes)
    {
        byte[] digest = SHA256.HashData(bytes);
        try
        {
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static string DigestFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] digest = SHA256.HashData(stream);
        try
        {
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryImporterScope : IDisposable
    {
        private readonly string _testBase;

        public TemporaryImporterScope()
        {
            _testBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-importer-tests"));
            Root = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ApplyPrivateAcl(Root);
        }

        public string Root { get; }

        private static void ApplyPrivateAcl(string path)
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            SecurityIdentifier currentUser = identity.User
                ?? throw new InvalidOperationException("The test identity has no SID.");
            var security = new DirectorySecurity();
            security.SetOwner(currentUser);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (SecurityIdentifier sid in new[]
                     {
                         currentUser,
                         new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                         new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                     })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            new DirectoryInfo(path).SetAccessControl(security);
        }

        public void Dispose()
        {
            string resolved = Path.GetFullPath(Root);
            string requiredPrefix = Path.TrimEndingDirectorySeparator(_testBase)
                + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
