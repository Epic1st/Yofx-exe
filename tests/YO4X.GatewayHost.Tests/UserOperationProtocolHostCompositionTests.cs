using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.GatewayHost;
using YO4X.Supervisor;

namespace YO4X.GatewayHost.Tests;

public sealed class UserOperationProtocolHostCompositionTests
{
    [Fact]
    public async Task GatewayDefaultsToOnlyItsFailClosedLocalProtocolPorts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserOperationSupervisorDeliveryApplication,
            UnavailableUserOperationSupervisorDeliveryApplication>();
        services.AddSingleton<IUserOperationCredentialBoundaryApplication,
            UnavailableUserOperationCredentialBoundaryApplication>();
        services.AddSingleton<IUserOperationResultV5Application,
            UnavailableUserOperationResultV5Application>();

        GatewayUserOperationProtocolRegistration.AddGatewayUserOperationProtocol(
            services,
            EmptyConfiguration());

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        Assert.IsType<UnavailableUserOperationGatewayBeginApplication>(
            provider.GetRequiredService<IUserOperationGatewayBeginApplication>());
        Assert.IsType<UnavailableUserOperationGatewayObservationApplication>(
            provider.GetRequiredService<IUserOperationGatewayObservationApplication>());
        Assert.Single(provider.GetServices<IUserOperationGatewayBeginApplication>());
        Assert.Single(provider.GetServices<IUserOperationGatewayObservationApplication>());
        Assert.Null(provider.GetService<IUserOperationSupervisorDeliveryApplication>());
        Assert.Null(provider.GetService<IUserOperationCredentialBoundaryApplication>());
        Assert.Null(provider.GetService<IUserOperationResultV5Application>());
        Assert.Null(provider.GetService<IUserOperationProviderCallInvoker>());
    }

    [Fact]
    public async Task SupervisorDefaultsToOnlyItsFailClosedLocalProtocolPorts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserOperationGatewayBeginApplication,
            UnavailableUserOperationGatewayBeginApplication>();
        services.AddSingleton<IUserOperationCredentialBoundaryApplication,
            UnavailableUserOperationCredentialBoundaryApplication>();
        services.AddSingleton<IUserOperationGatewayObservationApplication,
            UnavailableUserOperationGatewayObservationApplication>();
        services.AddSingleton<IUserOperationProviderCallInvoker,
            UnavailableUserOperationProviderCallInvoker>();

        SupervisorUserOperationProtocolRegistration.AddSupervisorUserOperationProtocol(
            services,
            EmptyConfiguration());

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        Assert.IsType<UnavailableUserOperationSupervisorDeliveryApplication>(
            provider.GetRequiredService<IUserOperationSupervisorDeliveryApplication>());
        Assert.IsType<UnavailableUserOperationResultV5Application>(
            provider.GetRequiredService<IUserOperationResultV5Application>());
        Assert.Single(provider.GetServices<IUserOperationSupervisorDeliveryApplication>());
        Assert.Single(provider.GetServices<IUserOperationResultV5Application>());
        Assert.Null(provider.GetService<IUserOperationGatewayBeginApplication>());
        Assert.Null(provider.GetService<IUserOperationCredentialBoundaryApplication>());
        Assert.Null(provider.GetService<IUserOperationGatewayObservationApplication>());
        Assert.Null(provider.GetService<IUserOperationProviderCallInvoker>());
    }

    [Fact]
    public void EnablingEitherHostFailsStartupWithoutLeakingConfiguration()
    {
        const string secret = "do-not-expose-user-operation-transport-secret";
        IConfiguration configuration = Configuration(
            ("UserOperationProtocol:Enabled", "true"),
            ("UserOperationProtocol:ClientSecret", secret));

        BackendCapabilityUnavailableException gateway =
            Assert.Throws<BackendCapabilityUnavailableException>(() =>
                GatewayUserOperationProtocolRegistration.AddGatewayUserOperationProtocol(
                    new ServiceCollection(),
                    configuration));
        BackendCapabilityUnavailableException supervisor =
            Assert.Throws<BackendCapabilityUnavailableException>(() =>
                SupervisorUserOperationProtocolRegistration.AddSupervisorUserOperationProtocol(
                    new ServiceCollection(),
                    configuration));

        Assert.Equal(
            "user_operation_authenticated_cross_host_transport",
            gateway.Capability);
        Assert.Equal(gateway.Capability, supervisor.Capability);
        Assert.Equal(
            "The required backend capability is not configured or is not safely available.",
            gateway.Message);
        Assert.Equal(gateway.Message, supervisor.Message);
        Assert.DoesNotContain(secret, gateway.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, supervisor.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedEnablementFailsClosedWithSanitizedConfigurationError()
    {
        const string secret = "not-a-boolean-secret";
        IConfiguration configuration = Configuration(
            ("UserOperationProtocol:Enabled", secret));

        InvalidOperationException gateway = Assert.Throws<InvalidOperationException>(() =>
            GatewayUserOperationProtocolRegistration.AddGatewayUserOperationProtocol(
                new ServiceCollection(),
                configuration));
        InvalidOperationException supervisor = Assert.Throws<InvalidOperationException>(() =>
            SupervisorUserOperationProtocolRegistration.AddSupervisorUserOperationProtocol(
                new ServiceCollection(),
                configuration));

        Assert.Equal(
            "User-operation protocol host configuration is invalid.",
            gateway.Message);
        Assert.Equal(gateway.Message, supervisor.Message);
        Assert.DoesNotContain(secret, gateway.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, supervisor.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutableProtocolGateRunsBeforeOtherHostComposition()
    {
        string gatewayProgram = RepositoryFile(
            "src",
            "Runtime",
            "YO4X.GatewayHost",
            "Program.cs");
        string supervisorProgram = RepositoryFile(
            "src",
            "Runtime",
            "YO4X.Supervisor",
            "Program.cs");
        string gatewayProject = RepositoryFile(
            "src",
            "Runtime",
            "YO4X.GatewayHost",
            "YO4X.GatewayHost.csproj");
        string supervisorProject = RepositoryFile(
            "src",
            "Runtime",
            "YO4X.Supervisor",
            "YO4X.Supervisor.csproj");

        int protocolGate = gatewayProgram.IndexOf(
            "AddGatewayUserOperationProtocol(builder.Configuration)",
            StringComparison.Ordinal);
        int processBoundary = gatewayProgram.IndexOf(
            "AddMt5ProcessBoundary(builder.Configuration)",
            StringComparison.Ordinal);
        int brokerComposition = gatewayProgram.IndexOf(
            "AddBrokerCommandOneShot(builder.Configuration)",
            StringComparison.Ordinal);
        Assert.True(protocolGate >= 0 && processBoundary > protocolGate);
        Assert.True(brokerComposition > protocolGate);
        Assert.Contains(
            "AddSupervisorUserOperationProtocol(builder.Configuration)",
            supervisorProgram,
            StringComparison.Ordinal);
        Assert.DoesNotContain("YO4X.RuntimeControl.Postgres", gatewayProject, StringComparison.Ordinal);
        Assert.DoesNotContain("YO4X.RuntimeControl.Postgres", supervisorProject, StringComparison.Ordinal);
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration Configuration(
        params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.Ordinal)).Build();

    private static string RepositoryFile(
        string firstSegment,
        params string[] remainingSegments) =>
        File.ReadAllText(Path.Combine(
            [RepositoryRoot(), firstSegment, .. remainingSegments]));

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
