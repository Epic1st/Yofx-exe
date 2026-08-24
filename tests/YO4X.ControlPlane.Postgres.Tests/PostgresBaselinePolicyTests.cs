using System.Security.Cryptography;
using System.Text;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresBaselinePolicyTests
{
    private const string ExpectedFoundationSha256 =
        "1de1cad6257edbd1a2c9eacd969171222b950d38b8cfa2f09ea5525506279db6";

    private const string ExpectedInvocationProtocolSha256 =
        "0cdf77558e519e9a1eedd3813d5c92a3d2d67b775a3b7d5829154c0ccb914f74";

    private const string ExpectedRoleScriptSha256 =
        "292286093807f76a4a09bf7535736cdb4d006c6e9ae6accf12cd389d07eefa35";

    [Fact]
    public void FoundationMigrationIsLfPinnedAndChecksumFrozen()
    {
        string repository = FindRepositoryRoot();
        string attributes = File.ReadAllText(Path.Combine(repository, ".gitattributes"));
        Assert.Contains("*.sql text eol=lf", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("*.sql text eol=crlf", attributes, StringComparison.Ordinal);

        string migration = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql"));
        Assert.DoesNotContain('\r', migration);
        Assert.Equal(ExpectedFoundationSha256, Sha256Utf8(migration));

        string invocationProtocol = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "002_user_operation_invocation_protocol.sql"));
        Assert.DoesNotContain('\r', invocationProtocol);
        Assert.Equal(ExpectedInvocationProtocolSha256, Sha256Utf8(invocationProtocol));

        string roleScript = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql"));
        Assert.DoesNotContain('\r', roleScript);
        Assert.Equal(ExpectedRoleScriptSha256, Sha256Utf8(roleScript));

        string policy = File.ReadAllText(Path.Combine(
            repository,
            "docs",
            "backend",
            "POSTGRESQL_BASELINE_POLICY.md"));
        string normalizedPolicy = string.Join(
            ' ',
            policy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(ExpectedFoundationSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedInvocationProtocolSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedRoleScriptSha256, policy, StringComparison.Ordinal);
        Assert.Contains("explicitly pre-release and greenfield", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("must never edit `control.schema_migrations`", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("provision a new empty database", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("commission a staged additive upgrade", normalizedPolicy, StringComparison.Ordinal);
    }

    private static string Sha256Utf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
