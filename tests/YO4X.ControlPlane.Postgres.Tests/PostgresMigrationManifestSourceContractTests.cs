namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresMigrationManifestSourceContractTests
{
    [Fact]
    public void MigrationExecutionAndRuntimeReadinessShareOneExactManifest()
    {
        string manifest = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "PostgresMigrationManifest.cs");
        string runner = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "PostgresMigrationRunner.cs");
        string fingerprint = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "PostgresRoleCapabilityFingerprint.cs");
        string controlReadiness = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api",
            "ControlPlaneReadinessProbe.cs");
        string adminReadiness = ReadRepositoryFile(
            "src", "Infrastructure", "YO4X.Admin.Postgres",
            "AdminDatabaseReadiness.cs");

        Assert.Contains("SHA256.HashData(bytes)", manifest, StringComparison.Ordinal);
        Assert.Contains("Distinct(StringComparer.Ordinal)", manifest, StringComparison.Ordinal);
        Assert.Contains("OrderBy(id => id, StringComparer.Ordinal)",
            manifest, StringComparison.Ordinal);
        Assert.Contains("PostgresMigrationManifest.Load()", runner, StringComparison.Ordinal);
        Assert.Contains("VerifyAppliedManifestAsync(", runner, StringComparison.Ordinal);
        Assert.Contains("PostgresMigrationManifest.Load()", fingerprint, StringComparison.Ordinal);
        Assert.Contains("from unnest(@migration_ids::text[], @migration_sha256::text[])",
            fingerprint, StringComparison.Ordinal);
        Assert.Contains("from control.schema_migrations", fingerprint, StringComparison.Ordinal);
        Assert.Contains("except", fingerprint, StringComparison.Ordinal);

        Assert.DoesNotContain("001_foundation", controlReadiness, StringComparison.Ordinal);
        Assert.DoesNotContain("001_foundation", adminReadiness, StringComparison.Ordinal);
        Assert.Contains("PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(",
            controlReadiness, StringComparison.Ordinal);
        Assert.Contains("PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(",
            adminReadiness, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        string repository = directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
        string[] segments = new string[path.Length + 1];
        segments[0] = repository;
        Array.Copy(path, 0, segments, 1, path.Length);
        return File.ReadAllText(Path.Combine(segments));
    }
}
