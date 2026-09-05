namespace YO4X.Desktop.Tests;

public sealed class LocalServerHostRouteContractTests
{
    [Fact]
    public void ShellDoesNotServeProductApiRoutes()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Apps",
            "YO4X.Desktop",
            "LocalServerHost.cs"));

        Assert.Contains("MapGet(\"/health\"", source, StringComparison.Ordinal);
        foreach (string forbidden in new[]
        {
            "/v1/auth",
            "/v1/me",
            "/v1/catalog",
            "/v1/bots",
            "/v1/broker-accounts",
            "accounts.json",
            "bots.json",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
