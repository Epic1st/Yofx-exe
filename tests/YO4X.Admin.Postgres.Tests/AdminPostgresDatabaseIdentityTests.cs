using System.Runtime.CompilerServices;
using Npgsql;
using YO4X.Admin.Postgres;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres.Tests;

public sealed class AdminPostgresDatabaseIdentityTests
{
    [Fact]
    public void ExactAdminBffRuntimeIdentityIsAccepted()
    {
        const string configured =
            "Host=db.example;Database=yo4x;Username=yo4x_admin_bff;"
            + "Password=test-only;SSL Mode=VerifyFull";

        bool accepted = AdminPostgresDatabaseIdentity.TryReadRuntimeConnectionString(
            configured,
            out string connectionString);

        Assert.True(accepted);
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal(AdminPostgresDatabaseIdentity.RequiredRole, parsed.Username);
        Assert.Equal(AdminPostgresDatabaseIdentity.RequiredRole, Yo4xPostgresRoleContracts.AdminBff.Role);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);
    }

    [Theory]
    [InlineData("yo4x_control_api")]
    [InlineData("yo4x_emergency")]
    [InlineData("YO4X_ADMIN_BFF")]
    [InlineData("")]
    public void AnyOtherConfiguredRoleIsRejected(string role)
    {
        string configured =
            $"Host=db.example;Database=yo4x;Username={role};"
            + "Password=test-only;SSL Mode=VerifyFull";

        bool accepted = AdminPostgresDatabaseIdentity.TryReadRuntimeConnectionString(
            configured,
            out string connectionString);

        Assert.False(accepted);
        Assert.Empty(connectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Database=yo4x;Username=yo4x_admin_bff;SSL Mode=VerifyFull")]
    [InlineData("Host=db.example;Username=yo4x_admin_bff;SSL Mode=VerifyFull")]
    [InlineData("Unknown Setting=true")]
    public void MissingOrMalformedRuntimeConnectionIsRejected(string? configured)
    {
        bool accepted = AdminPostgresDatabaseIdentity.TryReadRuntimeConnectionString(
            configured,
            out string connectionString);

        Assert.False(accepted);
        Assert.Empty(connectionString);
    }

    [Theory]
    [InlineData("SSL Mode=Require")]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=-c search_path=public")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void UnsafeRuntimeConnectionFeaturesAreRejected(string unsafeSetting)
    {
        string configured =
            "Host=db.example;Database=yo4x;Username=yo4x_admin_bff;"
            + $"Password=test-only;SSL Mode=VerifyFull;{unsafeSetting}";

        bool accepted = AdminPostgresDatabaseIdentity.TryReadRuntimeConnectionString(
            configured,
            out string connectionString);

        Assert.False(accepted);
        Assert.Empty(connectionString);
    }

    [Fact]
    public void StartupAndReadinessRetainBothIdentityPins()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.Admin.Bff",
            "Program.cs");
        string readiness = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.Admin.Postgres",
            "AdminDatabaseReadiness.cs");
        string application = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.Admin.Postgres",
            "AdminPostgresApplication.cs");
        string normalizedProgram = NormalizeWhitespace(program);

        Assert.Equal("yo4x_admin_bff", AdminPostgresDatabaseIdentity.RequiredRole);
        Assert.Contains(
            "AdminPostgresDatabaseIdentity.TryReadRuntimeConnectionString(",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "new PostgresDatabase( adminPostgresConnection, PostgresDatabaseUsage.Runtime, serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>())",
            normalizedProgram,
            StringComparison.Ordinal);
        Assert.Contains(
            "PostgresTenantContextCapabilityProvider.TryNormalizeIssuerConnectionString(",
            program,
            StringComparison.Ordinal);
        Assert.Contains("current_user = 'yo4x_admin_bff'", readiness, StringComparison.Ordinal);
        Assert.Contains("control.assert_safe_runtime_role()", readiness, StringComparison.Ordinal);
        Assert.Contains(
            "PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(",
            readiness,
            StringComparison.Ordinal);
        Assert.Contains(
            "Yo4xPostgresRoleContracts.AdminBff",
            readiness,
            StringComparison.Ordinal);
        Assert.Contains(
            "AdminDatabaseReadiness.IsReadyAsync(database, cancellationToken)",
            application,
            StringComparison.Ordinal);
        Assert.Contains("required_relations", readiness, StringComparison.Ordinal);
        Assert.Contains("required_columns", readiness, StringComparison.Ordinal);
        Assert.Contains("required_table_privileges", readiness, StringComparison.Ordinal);
        Assert.Contains("sensitive_relations", readiness, StringComparison.Ordinal);
        Assert.Contains("control.schema_migrations", readiness, StringComparison.Ordinal);
        Assert.Contains(
            "not has_table_privilege(\n               current_user, 'messaging.outbox_messages', 'SELECT')",
            readiness.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "'aggregate_id', 'aggregate_type', 'attempts', 'available_at'",
            readiness,
            StringComparison.Ordinal);
        Assert.Contains(
            "'payload_sha256', 'published_at', 'schema_version', 'state'",
            readiness,
            StringComparison.Ordinal);
        Assert.Contains("ExecuteScalarAsync", readiness, StringComparison.Ordinal);
        Assert.Contains("return result is true", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentRequiresDirectNamedRuntimeLoginsAndNoLoginMigrator()
    {
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string normalized = NormalizeWhitespace(roles);

        Assert.Contains(
            "not rolcanlogin or rolinherit or rolsuper or rolbypassrls",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "YO4X runtime roles must be current LOGIN NOINHERIT NOSUPERUSER "
            + "NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION identities",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "yo4x_migrator must be NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS "
            + "NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT -1",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "revoke all privileges on control.schema_migrations from yo4x_admin_bff",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "grant select (migration_id, sha256) on control.schema_migrations to yo4x_admin_bff",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "Reapplication is globally subtractive for every direct runtime identity",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "revoke all privileges on all tables in schema identity, \"authorization\", control, "
            + "operations, governance, audit, messaging, readmodel from "
            + "yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "revoke all privileges on all functions in schema identity, \"authorization\", control, "
            + "operations, governance, audit, messaging, readmodel from "
            + "yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff",
            normalized,
            StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadRepositoryFile(params string[] segments)
    {
        string path = Path.Combine([RepositoryRoot(), .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (string start in new[]
        {
            Path.GetDirectoryName(sourceFilePath) ?? string.Empty,
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
            {
                directory = directory.Parent;
            }

            if (directory is not null)
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
