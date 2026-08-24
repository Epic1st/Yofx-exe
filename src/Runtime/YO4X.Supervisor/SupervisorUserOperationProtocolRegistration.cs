using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.Supervisor;

internal static class SupervisorUserOperationProtocolRegistration
{
    internal const string SectionName = "UserOperationProtocol";

    internal static IServiceCollection AddSupervisorUserOperationProtocol(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        RequireDisabled(configuration);

        RemoveAllProtocolPorts(services);
        services.AddSingleton<IUserOperationSupervisorDeliveryApplication,
            UnavailableUserOperationSupervisorDeliveryApplication>();
        services.AddSingleton<IUserOperationResultV5Application,
            UnavailableUserOperationResultV5Application>();
        return services;
    }

    private static void RequireDisabled(IConfiguration configuration)
    {
        string? configured = configuration[$"{SectionName}:Enabled"];
        bool enabled = false;
        if (configured is not null
            && !bool.TryParse(configured, out enabled))
        {
            throw new InvalidOperationException(
                "User-operation protocol host configuration is invalid.");
        }

        if (enabled)
        {
            throw new BackendCapabilityUnavailableException(
                "user_operation_authenticated_cross_host_transport");
        }
    }

    private static void RemoveAllProtocolPorts(IServiceCollection services)
    {
        services.RemoveAll<IUserOperationSupervisorDeliveryApplication>();
        services.RemoveAll<IUserOperationGatewayBeginApplication>();
        services.RemoveAll<IUserOperationCredentialBoundaryApplication>();
        services.RemoveAll<IUserOperationGatewayObservationApplication>();
        services.RemoveAll<IUserOperationResultV5Application>();
        services.RemoveAll<IUserOperationProviderCallInvoker>();
    }
}
