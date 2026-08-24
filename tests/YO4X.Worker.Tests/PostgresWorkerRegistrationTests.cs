using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.Persistence.Postgres;

namespace YO4X.Worker.Tests;

public sealed class PostgresWorkerRegistrationTests
{
    [Fact]
    public async Task ExactTlsIssuerForTheRuntimeEndpointRegistersWorkerPersistence()
    {
        var services = new ServiceCollection();

        services.TryAddWorkerPostgres(Configuration("worker-db.example"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ITenantContextCapabilityProvider>());
        Assert.NotNull(provider.GetService<PostgresDatabase>());
        Assert.NotNull(provider.GetService<PostgresWorkerReadiness>());
    }

    [Fact]
    public async Task MissingIssuerFailsCompositionClosed()
    {
        var services = new ServiceCollection();

        services.TryAddWorkerPostgres(Configuration(issuerHost: null));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ITenantContextCapabilityProvider>());
        Assert.Null(provider.GetService<PostgresDatabase>());
        Assert.Null(provider.GetService<PostgresWorkerReadiness>());
    }

    [Fact]
    public async Task IssuerForAnotherEndpointFailsCompositionClosed()
    {
        var services = new ServiceCollection();

        services.TryAddWorkerPostgres(Configuration("other-db.example"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ITenantContextCapabilityProvider>());
        Assert.Null(provider.GetService<PostgresDatabase>());
        Assert.Null(provider.GetService<PostgresWorkerReadiness>());
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void UnsafePostgresSessionFeaturesFailCompositionClosed(string unsafeSetting)
    {
        string connectionString =
            "Host=worker-db.example;Database=yo4x;Username=yo4x_worker;Password=test-only;"
            + $"SSL Mode=VerifyFull;{unsafeSetting}";

        Assert.False(PostgresWorkerRegistration.TryReadRuntimeConnectionString(
            connectionString,
            out string normalized));
        Assert.Empty(normalized);
    }

    private static IConfiguration Configuration(string? issuerHost)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] =
                "Host=worker-db.example;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=VerifyFull",
            ["PolicyTrust:EcdsaP256Keys:test-key"] =
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
        };
        if (issuerHost is not null)
        {
            values["ConnectionStrings:ContextIssuer"] =
                $"Host={issuerHost};Database=yo4x;Username=yo4x_context_issuer;Password=test-only;SSL Mode=VerifyFull";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
