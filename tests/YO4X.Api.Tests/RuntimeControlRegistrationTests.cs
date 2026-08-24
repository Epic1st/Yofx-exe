using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.Api.Tests;

public sealed class RuntimeControlRegistrationTests
{
    [Fact]
    public async Task CompleteDevelopmentConfigurationRegistersSeparateWorkerAdapter()
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable",
            $"sha256:{new string('a', 64)}",
            "Host=localhost;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=Disable");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Development));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.IsType<PostgresRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
        Assert.NotNull(provider.GetService<RuntimePostgresDatabase>());
        Assert.NotNull(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Theory]
    [InlineData("yo4x_worker")]
    [InlineData("yo4x_control_api")]
    [InlineData("postgres")]
    public async Task NonEvidenceRoleCannotOwnBrokerResultIngress(string role)
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable",
            $"sha256:{new string('a', 64)}",
            $"Host=localhost;Database=yo4x;Username={role};Password=test-only;SSL Mode=Disable");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Development));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Fact]
    public async Task ProductionEvidenceConnectionRequiresVerifyFull()
    {
        IConfiguration configuration = Configuration(
            "Host=db.example;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=VerifyFull",
            $"sha256:{new string('a', 64)}",
            "Host=db.example;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=Require");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Production));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public async Task UnsafeEvidenceConnectionFeaturesAreRejected(string unsafeOption)
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable",
            $"sha256:{new string('a', 64)}",
            $"Host=localhost;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=Disable;{unsafeOption}");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Development));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Theory]
    [InlineData("yo4x_control_api")]
    [InlineData("yo4x_migrator")]
    [InlineData("postgres")]
    public void NonWorkerRoleRetainsUnavailableRuntimeAdapter(string role)
    {
        IConfiguration configuration = Configuration(
            $"Host=localhost;Database=yo4x;Username={role};Password=test-only;SSL Mode=Disable");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Development));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
        Assert.Null(provider.GetService<RuntimePostgresDatabase>());
    }

    [Fact]
    public void ProductionConnectionWithoutVerifyFullRetainsUnavailableRuntimeAdapter()
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Require");
        var services = new ServiceCollection();

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Production));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
    }

    [Fact]
    public async Task ProductionVerifyFullConnectionRegistersRuntimeAdapter()
    {
        IConfiguration configuration = Configuration(
            "Host=db.example;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=VerifyFull",
            $"sha256:{new string('a', 64)}",
            "Host=db.example;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=VerifyFull");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Production));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.IsType<PostgresRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
        Assert.NotNull(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Fact]
    public async Task IssuerForAnotherEndpointRetainsUnavailableRuntimeAdapter()
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable",
            $"sha256:{new string('a', 64)}",
            "Host=localhost;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=Disable");
        configuration["ConnectionStrings:ContextIssuer"] =
            "Host=other.example;Database=yo4x;Username=yo4x_context_issuer;Password=test-only;SSL Mode=VerifyFull";
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddRuntimeControlPostgres(
            configuration,
            Environment(Environments.Development));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ITenantContextCapabilityProvider>());
        Assert.Null(provider.GetService<RuntimePostgresDatabase>());
        Assert.Null(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:ABCDEF")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MissingOrInvalidApprovedRuntimeDigestRetainsUnavailableRuntimeAdapter(string? digest)
    {
        IConfiguration configuration = Configuration(
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable",
            digest);
        var services = new ServiceCollection();

        services.TryAddRuntimeControlPostgres(configuration, Environment(Environments.Development));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
    }

    [Theory]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Include Error Detail=true")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Log Parameters=true")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Trust Server Certificate=true")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Options=-c statement_timeout=0")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Search Path=public")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;No Reset On Close=true")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable;Multiplexing=true")]
    public void UnsafeConnectionFeaturesRetainUnavailableRuntimeAdapter(string connectionString)
    {
        var services = new ServiceCollection();

        services.TryAddRuntimeControlPostgres(
            Configuration(connectionString),
            Environment(Environments.Development));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
    }

    [Theory]
    [InlineData("Host=db.example;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only")]
    [InlineData("Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Prefer")]
    public void DevelopmentPlaintextRequiresExplicitLoopbackAndDisable(string connectionString)
    {
        var services = new ServiceCollection();

        services.TryAddRuntimeControlPostgres(
            Configuration(connectionString),
            Environment(Environments.Development));
        services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableRuntimeControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IRuntimeControlPlaneApplication>());
        Assert.Null(provider.GetService<RuntimePostgresDatabase>());
        Assert.Null(provider.GetService<RuntimeEvidencePostgresDatabase>());
    }

    private static IConfiguration Configuration(string connectionString) =>
        Configuration(connectionString, $"sha256:{new string('a', 64)}");

    private static IConfiguration Configuration(
        string connectionString,
        string? approvedRuntimeImageDigest) =>
        Configuration(connectionString, approvedRuntimeImageDigest, null);

    private static IConfiguration Configuration(
        string connectionString,
        string? approvedRuntimeImageDigest,
        string? evidenceConnectionString)
    {
        var runtime = new NpgsqlConnectionStringBuilder(connectionString);
        var issuer = new NpgsqlConnectionStringBuilder
        {
            Host = runtime.Host,
            Port = runtime.Port,
            Database = runtime.Database,
            Username = PostgresTenantContextCapabilityProvider.RequiredDatabaseRole,
            Password = "test-only",
            SslMode = SslMode.VerifyFull
        };
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RuntimePostgres"] = connectionString,
            ["ConnectionStrings:RuntimeEvidencePostgres"] = evidenceConnectionString,
            ["ConnectionStrings:ContextIssuer"] = issuer.ConnectionString,
            ["RuntimePostgres:ApprovedRuntimeImageDigest"] = approvedRuntimeImageDigest
        }).Build();
    }

    private static TestHostEnvironment Environment(string name) => new()
    {
        EnvironmentName = name
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "YO4X.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
