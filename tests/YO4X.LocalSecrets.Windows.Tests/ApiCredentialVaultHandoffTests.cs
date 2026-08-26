using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;

namespace YO4X.LocalSecrets.Windows.Tests;

/// <summary>
/// Follows one synthetic password the whole way: from the control-plane API's
/// credential sink, through the pinned writer process, into the DPAPI vault.
/// This is the test that says where the plaintext ends up and where it does not.
/// </summary>
public sealed class ApiCredentialVaultHandoffTests
{
    private const string Secret = "synthetic-handoff-secret";
    private const string Server = "Broker-Demo";
    private const ulong Login = 12345678UL;

    [Fact]
    public async Task ApiHandsThePasswordToTheVaultAndKeepsNoReadableCopy()
    {
        using var scope = new TemporaryVaultScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");
        ILocalBrokerCredentialVault sink = CreateSink(vaultRoot);
        string credentialKey = LocalCredentialKey.Create(Login, Server);
        byte[] material = Encoding.UTF8.GetBytes(Secret);
        Utf8Secret password = Utf8Secret.TakeOwnership(material);

        LocalBrokerCredentialWriteResult result;
        try
        {
            result = await sink.StoreAsync(
                Login,
                Server,
                credentialKey,
                password,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            password.Dispose();
        }

        Assert.Equal(credentialKey, result.CredentialKey);
        Assert.Equal("local_credential_write_created", result.Code);

        // The API's buffer is gone the moment the request scope ends.
        Assert.All(material, value => Assert.Equal(0, value));

        // The DPAPI vault holds the real secret, under the same binding key the
        // control plane persisted and the connection probe looks up.
        var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
        using LocalMt5Credential? stored = await vault.OpenAsync(
            credentialKey,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        using var expected = new LocalMt5Credential(Login, Server, Encoding.UTF8.GetBytes(Secret));
        Assert.True(expected.HasSameSecret(stored));

        // Nothing on disk outside the DPAPI ciphertext carries the plaintext.
        foreach (string file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
        {
            byte[] contents = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(contents), StringComparison.Ordinal);
            Assert.DoesNotContain(
                Secret,
                Encoding.Unicode.GetString(contents),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WriterThatDoesNotMatchItsPinnedDigestIsRefusedBeforeAnySecretIsSent()
    {
        using var scope = new TemporaryVaultScope();
        string vaultRoot = Path.Combine(scope.Root, "vault");
        ILocalBrokerCredentialVault sink = CreateSink(vaultRoot, writerSha256: new string('a', 64));
        using Utf8Secret password = Utf8Secret.TakeOwnership(Encoding.UTF8.GetBytes(Secret));

        await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() => sink.StoreAsync(
            Login,
            Server,
            LocalCredentialKey.Create(Login, Server),
            password,
            TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(vaultRoot) && Directory.EnumerateFiles(vaultRoot, "*.yo4xcred").Any());
    }

    [Fact]
    public async Task UnconfiguredDeploymentFailsClosedRatherThanStoringTheSecretAnywhereElse()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddLocalBrokerCredentialVault(new ConfigurationBuilder().Build())
            .BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            ILocalBrokerCredentialVault sink = provider.GetRequiredService<ILocalBrokerCredentialVault>();
            Assert.IsType<UnavailableLocalBrokerCredentialVault>(sink);

            using Utf8Secret password = Utf8Secret.TakeOwnership(Encoding.UTF8.GetBytes(Secret));
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() => sink.StoreAsync(
                Login,
                Server,
                LocalCredentialKey.Create(Login, Server),
                password,
                TestContext.Current.CancellationToken));
        }
    }

    private static ILocalBrokerCredentialVault CreateSink(string vaultRoot, string? writerSha256 = null)
    {
        string writerPath = FindWriterExecutable();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LocalBrokerCredentialVault:Enabled"] = "true",
                ["LocalBrokerCredentialVault:WriterPath"] = writerPath,
                ["LocalBrokerCredentialVault:WriterSha256"] = writerSha256 ?? DigestFile(writerPath),
                ["LocalBrokerCredentialVault:VaultRoot"] = vaultRoot,
                ["LocalBrokerCredentialVault:TimeoutMilliseconds"] = "30000"
            })
            .Build();
        return new ServiceCollection()
            .AddLocalBrokerCredentialVault(configuration)
            .BuildServiceProvider()
            .GetRequiredService<ILocalBrokerCredentialVault>();
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

    private static string DigestFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class TemporaryVaultScope : IDisposable
    {
        private readonly string testBase;

        public TemporaryVaultScope()
        {
            testBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-api-vault-tests"));
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
