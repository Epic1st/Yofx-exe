using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace YO4X.LocalSecrets.Windows.Tests;

/// <summary>
/// The writer is the only path from the control-plane API to the DPAPI vault.
/// These prove that a password reaches the vault over standard input, that the
/// process renders nothing of it, and that every mismatch fails closed. Every
/// credential here is synthetic.
/// </summary>
public sealed class LocalCredentialWriterProcessTests
{
    private const string Secret = "synthetic-writer-secret";
    private const string Server = "Broker-Demo";
    private const ulong Login = 12345678UL;

    [Fact]
    public async Task StdinCredentialIsStoredUnderTheBoundaryBindingKeyAndReplaysIdempotently()
    {
        using var scope = new TemporaryWriterScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");
        string credentialKey = LocalCredentialKey.Create(Login, Server);
        byte[] block = Block(Secret);

        ProcessResult first = await RunWriterAsync(block, credentialKey, Digest(block), vaultRoot);

        Assert.Equal(0, first.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(first.StandardError), first.StandardError);
        AssertRendersNothingSecret(first);
        using (JsonDocument receipt = JsonDocument.Parse(first.StandardOutput))
        {
            Assert.Equal(1, receipt.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(receipt.RootElement.GetProperty("isSuccess").GetBoolean());
            Assert.False(receipt.RootElement.GetProperty("secretsRendered").GetBoolean());
            Assert.Equal("local_credential_write_created", receipt.RootElement.GetProperty("code").GetString());
            Assert.Equal(credentialKey, receipt.RootElement.GetProperty("credentialKey").GetString());
            Assert.Equal("******78", receipt.RootElement.GetProperty("maskedLogin").GetString());
        }

        // The connection probe finds credentials by this key alone, so the file
        // the API named must be exactly the one the boundary derives.
        var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
        using LocalMt5Credential? stored = await vault.OpenAsync(
            credentialKey,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        using var expected = new LocalMt5Credential(Login, Server, Encoding.UTF8.GetBytes(Secret));
        Assert.True(expected.HasSameSecret(stored));

        ProcessResult replay = await RunWriterAsync(
            Block(Secret),
            credentialKey,
            Digest(Block(Secret)),
            vaultRoot);

        Assert.Equal(0, replay.ExitCode);
        using JsonDocument replayReceipt = JsonDocument.Parse(replay.StandardOutput);
        Assert.Equal(
            "local_credential_write_unchanged",
            replayReceipt.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BindingKeyThatDoesNotFollowFromTheCredentialIsRefusedAndStoresNothing()
    {
        using var scope = new TemporaryWriterScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");
        byte[] block = Block(Secret);

        ProcessResult result = await RunWriterAsync(
            block,
            LocalCredentialKey.Create(87654321UL, Server),
            Digest(block),
            vaultRoot);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("credential_write_binding_mismatch", result.StandardError.Trim());
        AssertRendersNothingSecret(result);
        Assert.False(Directory.Exists(vaultRoot) && Directory.EnumerateFiles(vaultRoot, "*.yo4xcred").Any());
    }

    [Fact]
    public async Task BlockThatDoesNotMatchTheDigestTheCallerIntendedIsRefused()
    {
        using var scope = new TemporaryWriterScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");

        ProcessResult result = await RunWriterAsync(
            Block(Secret),
            LocalCredentialKey.Create(Login, Server),
            Digest(Block("a-different-synthetic-secret")),
            vaultRoot);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("credential_write_source_digest_mismatch", result.StandardError.Trim());
        AssertRendersNothingSecret(result);
    }

    [Fact]
    public async Task DifferentPasswordForAnExistingBindingIsRefusedAndTheStoredSecretSurvives()
    {
        using var scope = new TemporaryWriterScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");
        string credentialKey = LocalCredentialKey.Create(Login, Server);
        Assert.Equal(0, (await RunWriterAsync(
            Block(Secret),
            credentialKey,
            Digest(Block(Secret)),
            vaultRoot)).ExitCode);

        const string replacement = "synthetic-writer-secret-two";
        ProcessResult conflict = await RunWriterAsync(
            Block(replacement),
            credentialKey,
            Digest(Block(replacement)),
            vaultRoot);

        // Linking must never silently replace a working credential; only an
        // explicit rotation may.
        Assert.Equal(4, conflict.ExitCode);
        Assert.Equal(
            "credential_write_conflict_requires_explicit_rotation",
            conflict.StandardError.Trim());
        Assert.DoesNotContain(replacement, conflict.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(replacement, conflict.StandardError, StringComparison.Ordinal);

        var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
        using LocalMt5Credential? stored = await vault.OpenAsync(
            credentialKey,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        using var original = new LocalMt5Credential(Login, Server, Encoding.UTF8.GetBytes(Secret));
        Assert.True(original.HasSameSecret(stored));
    }

    [Fact]
    public async Task PasswordIsNeverAcceptedAsACommandLineArgument()
    {
        using var scope = new TemporaryWriterScope();
        ProcessStartInfo startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("--password");
        startInfo.ArgumentList.Add(Secret);

        ProcessResult result = await RunAsync(startInfo, []);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("credential_write_usage_invalid", result.StandardError.Trim());
    }

    private static void AssertRendersNothingSecret(ProcessResult result)
    {
        foreach (string rendered in new[] { result.StandardOutput, result.StandardError })
        {
            Assert.DoesNotContain(Secret, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(Server, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("12345678", rendered, StringComparison.Ordinal);
        }
    }

    private static byte[] Block(string password) => Encoding.UTF8.GetBytes(
        $"MT5 Login: {Login}\nMT5 Password: {password}\nMT5 Server: {Server}\n");

    private static async Task<ProcessResult> RunWriterAsync(
        byte[] block,
        string credentialKey,
        string sourceDigest,
        string vaultRoot)
    {
        ProcessStartInfo startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("--credential-key");
        startInfo.ArgumentList.Add(credentialKey);
        startInfo.ArgumentList.Add("--source-sha256");
        startInfo.ArgumentList.Add(sourceDigest);
        startInfo.ArgumentList.Add("--vault-root");
        startInfo.ArgumentList.Add(vaultRoot);
        return await RunAsync(startInfo, block);
    }

    private static ProcessStartInfo CreateStartInfo() => new(FindWriterExecutable())
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, byte[] input)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The credential writer could not be started.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await using (Stream stdin = process.StandardInput.BaseStream)
        {
            await stdin.WriteAsync(input);
            await stdin.FlushAsync();
        }

        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindWriterExecutable()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            string projectPath = Path.Combine(
                cursor.FullName,
                "src",
                "Tools",
                "YO4X.LocalCredentialWriter",
                "YO4X.LocalCredentialWriter.csproj");
            if (File.Exists(projectPath))
            {
                string configuration = AppContext.BaseDirectory.Contains(
                    $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Debug"
                    : "Release";
                string executablePath = Path.Combine(
                    Path.GetDirectoryName(projectPath)
                        ?? throw new InvalidOperationException("The writer project directory is unavailable."),
                    "bin",
                    configuration,
                    "net10.0-windows10.0.19041.0",
                    "YO4X.LocalCredentialWriter.exe");
                return File.Exists(executablePath)
                    ? executablePath
                    : throw new FileNotFoundException(
                        "The credential writer test dependency was not built.",
                        executablePath);
            }

            cursor = cursor.Parent;
        }

        throw new FileNotFoundException("The credential writer project was not found.");
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

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryWriterScope : IDisposable
    {
        private readonly string testBase;

        public TemporaryWriterScope()
        {
            testBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-writer-tests"));
            Root = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
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
            string requiredPrefix = Path.TrimEndingDirectorySeparator(testBase)
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
