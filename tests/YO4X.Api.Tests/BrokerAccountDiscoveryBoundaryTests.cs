namespace YO4X.Api.Tests;

public sealed class BrokerAccountDiscoveryBoundaryTests
{
    [Fact]
    public void DiscoveryRoutesAreAuthenticatedActorBoundAndReadOnly()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string routes = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "BrokerAccountDiscoveryEndpoints.cs");

        Assert.Contains("app.MapGroup(\"/v1\").RequireAuthorization(\"user\")", program, StringComparison.Ordinal);
        Assert.Contains("user.MapBrokerAccountDiscovery();", program, StringComparison.Ordinal);
        Assert.Contains("GetBrokerAccountsAsync(", routes, StringComparison.Ordinal);
        Assert.Contains("GetBrokerAccountRegistrationOptionsAsync(", routes, StringComparison.Ordinal);

        // The directory search term is a plain optional query parameter: the
        // route forwards it and never widens it into a filter object.
        Assert.Contains("string? query,", routes, StringComparison.Ordinal);
        Assert.Contains(
            "ToUserActor(context.User), query, cancellationToken)",
            Collapse(routes),
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(routes, "ToUserActor(context.User)"));
        Assert.Equal(2, CountOccurrences(routes, "Results.Ok("));
        Assert.DoesNotContain("AllowAnonymous", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("Credential", routes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BindingFingerprint", routes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoveryContractsExposeOnlyRedactedAccountAndApprovedDemoOptionMetadata()
    {
        string contracts = ReadRepositoryFile(
            "src",
            "Application",
            "YO4X.ControlPlane.Application",
            "ControlPlaneContracts.cs");
        string option = Slice(
            contracts,
            "public sealed record BrokerAccountRegistrationOption(",
            "public sealed record ApproveBrokerServer(");

        Assert.Contains("Guid? BrokerProfileId", option, StringComparison.Ordinal);
        Assert.Contains("Guid? DirectoryServerId", option, StringComparison.Ordinal);
        Assert.Contains("string BrokerCompany", option, StringComparison.Ordinal);
        Assert.Contains("string Server", option, StringComparison.Ordinal);
        Assert.Contains("BrokerAccountEnvironment Environment", option, StringComparison.Ordinal);

        // An unapproved directory match carries no broker profile, which is what
        // keeps it from being turned into a registration request by accident.
        Assert.Contains("bool Approved", option, StringComparison.Ordinal);
        Assert.DoesNotContain("Login", option, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", option, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", option, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fingerprint", option, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrokerServerApprovalRouteIsAuthenticatedIdempotentAndPromotesOneServer()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string endpoint = Slice(
            program,
            "user.MapPost(\"/broker-server-approvals\"",
            "user.MapBrokerAccountDiscovery();");
        string contracts = ReadRepositoryFile(
            "src",
            "Application",
            "YO4X.ControlPlane.Application",
            "ControlPlaneContracts.cs");
        string request = Slice(
            contracts,
            "public sealed record ApproveBrokerServer(",
            "public sealed record DeploymentView(");

        Assert.Contains("app.MapGroup(\"/v1\").RequireAuthorization(\"user\")", program, StringComparison.Ordinal);
        Assert.Contains("ApproveBrokerServerAsync(", endpoint, StringComparison.Ordinal);
        Assert.Contains("ToUserActor(context.User)", endpoint, StringComparison.Ordinal);
        Assert.Contains("ToMetadata(context)", endpoint, StringComparison.Ordinal);
        Assert.Contains(
            "Results.Created( $\"/v1/broker-account-registration-options"
                + "?query={Uri.EscapeDataString(option.Server)}\", option);",
            Collapse(endpoint),
            StringComparison.Ordinal);

        // The same precondition filter every other user mutation carries, so an
        // approval without a high-entropy Idempotency-Key never reaches the
        // application.
        Assert.Contains(".AddEndpointFilter(new MutationPreconditionFilter())", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("requireExpectedVersion: true", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", endpoint, StringComparison.Ordinal);

        // One server per request and nothing else in the body: there is no bulk
        // approval route and no way to name a governance profile directly.
        Assert.Contains("Guid DirectoryServerId", request, StringComparison.Ordinal);
        Assert.DoesNotContain(",", request, StringComparison.Ordinal);
        Assert.DoesNotContain("BrokerProfileId", request, StringComparison.Ordinal);
        Assert.DoesNotContain("[]", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Credential", request, StringComparison.OrdinalIgnoreCase);
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

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
