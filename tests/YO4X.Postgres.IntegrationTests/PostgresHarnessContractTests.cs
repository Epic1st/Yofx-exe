using System.Text.Json;

namespace YO4X.Postgres.IntegrationTests;

public sealed class PostgresHarnessContractTests
{
    [Fact]
    public void ComposeAndTestcontainerUseTheLockedPostgresManifest()
    {
        string repositoryRoot = FindRepositoryRoot();
        string lockPath = Path.Combine(
            repositoryRoot,
            "scripts",
            "postgresql-windows-x64.lock.json");
        using JsonDocument lockDocument = JsonDocument.Parse(File.ReadAllText(lockPath));
        JsonElement dockerFallback = lockDocument.RootElement.GetProperty("dockerFallback");
        string image = dockerFallback.GetProperty("image").GetString()
            ?? throw new InvalidDataException("The PostgreSQL Docker image lock is empty.");
        string digest = dockerFallback.GetProperty("manifestDigest").GetString()
            ?? throw new InvalidDataException("The PostgreSQL Docker manifest lock is empty.");
        string lockedReference = $"{image}@{digest}";

        Assert.Equal(PostgresContainerFixture.PostgreSqlContainerImage, lockedReference);
        string compose = File.ReadAllText(Path.Combine(repositoryRoot, "compose.yaml"));
        Assert.Contains($"image: {lockedReference}", compose, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "YO4X.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The YO4X repository root was not found.");
    }
}
