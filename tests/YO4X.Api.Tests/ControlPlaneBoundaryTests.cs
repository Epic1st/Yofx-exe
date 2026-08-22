using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Persistence.Postgres;

namespace YO4X.Api.Tests;

public sealed class ControlPlaneBoundaryTests
{
    [Fact]
    public void IncompletePersistenceConfigurationRetainsUnavailableApplication()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only"
        });
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<PostgresDatabase>());
        Assert.Null(provider.GetService<CredentialProofKey>());
    }

    [Fact]
    public async Task CompleteSafeConfigurationRegistersPostgresApplicationAndProofIssuer()
    {
        IConfiguration configuration = CompleteConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.IsType<PostgresControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.NotNull(provider.GetService<PostgresDatabase>());
        Assert.NotNull(provider.GetService<CredentialIngestionProofIssuer>());
        Assert.NotNull(provider.GetService<StrategyImportProofIssuer>());
    }

    [Theory]
    [InlineData("http://desktop.example")]
    [InlineData("https://desktop.example/path")]
    [InlineData("https://user@desktop.example")]
    public void UnsafeIngestionOriginRetainsUnavailableApplication(string origin)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["SecretIngestion:Origin"] = origin;
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Theory]
    [InlineData("yo4x_migrator")]
    [InlineData("yo4x_admin_bff")]
    [InlineData("postgres")]
    public void WrongDatabaseRoleRetainsUnavailableApplication(string databaseRole)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            $"Host=localhost;Database=yo4x;Username={databaseRole};Password=test-only";
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Fact]
    public void PlaceholderProofKeyRetainsUnavailableApplication()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["SecretIngestion:CredentialProofKeyBase64"] = Convert.ToBase64String(new byte[32]);
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingOrInvalidBrokerProfilePinRetainsUnavailableApplication(string? brokerProfileId)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["U0:ApprovedBrokerProfileId"] = brokerProfileId;
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Fact]
    public async Task UnavailableBackendIsNeverReportedReady()
    {
        var services = new ServiceCollection();
        services.AddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();
        services.AddSingleton<ControlPlaneReadinessProbe>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ControlPlaneReadinessProbe probe = provider.GetRequiredService<ControlPlaneReadinessProbe>();

        Assert.False(await probe.IsReadyAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    public void InvalidWorkloadGenerationIsAnAuthenticationFailure(string generation)
    {
        ClaimsPrincipal principal = WorkloadPrincipal(generation);

        Assert.Throws<UnauthorizedAccessException>(() => WorkloadActorClaims.Read(principal));
    }

    [Fact]
    public void PositiveWorkloadGenerationIsParsedInvariantly()
    {
        WorkloadActor actor = WorkloadActorClaims.Read(WorkloadPrincipal("42"));

        Assert.Equal(42, actor.Generation);
    }

    private static IConfiguration CompleteConfiguration() => Configuration(CompleteValues());

    private static Dictionary<string, string?> CompleteValues() => new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only",
        ["U0:ApprovedGatewayDigest"] = new string('a', 64),
        ["U0:ApprovedRegion"] = "region-1",
        ["U0:ApprovedBrokerServer"] = "demo-server",
        ["U0:ApprovedBrokerProfileId"] = "40000000-0000-0000-0000-000000000001",
        ["RuntimePostgres:ApprovedRuntimeImageDigest"] = $"sha256:{new string('b', 64)}",
        ["SecretIngestion:Origin"] = "https://desktop.example",
        ["SecretIngestion:ApprovedClientOrigin"] = "https://portal.example",
        ["SecretIngestion:CredentialProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray()),
        ["Conversion:ImportProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray()),
        ["PolicyTrust:EcdsaP256Keys:test-policy-key"] = CreatePolicyPublicKeyBase64()
    };

    private static string CreatePolicyPublicKeyBase64()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ClaimsPrincipal WorkloadPrincipal(string generation)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("tenant_id", "00000000-0000-0000-0000-000000000001"),
            new Claim("workload_id", "10000000-0000-0000-0000-000000000001"),
            new Claim("worker_instance_id", "11000000-0000-0000-0000-000000000001"),
            new Claim("deployment_id", "20000000-0000-0000-0000-000000000001"),
            new Claim("broker_account_id", "30000000-0000-0000-0000-000000000001"),
            new Claim("generation", generation),
            new Claim("region", "region-1"),
            new Claim("component", "supervisor")
        ],
        "test");
        return new ClaimsPrincipal(identity);
    }
}
