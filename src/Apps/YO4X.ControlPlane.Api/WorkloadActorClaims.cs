using System.Globalization;
using System.Security.Claims;
using YO4X.Api;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Api;

internal static class WorkloadActorClaims
{
    public static WorkloadActor Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string generationClaim = ClaimReader.Required(principal, "generation");
        if (!long.TryParse(
                generationClaim,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long generation)
            || generation <= 0)
        {
            throw new UnauthorizedAccessException("The required 'generation' identity claim is invalid.");
        }

        return new WorkloadActor(
            ClaimReader.RequiredGuid(principal, "tenant_id"),
            ClaimReader.RequiredGuid(principal, "workload_id"),
            ClaimReader.RequiredGuid(principal, "worker_instance_id"),
            ClaimReader.RequiredGuid(principal, "deployment_id"),
            ClaimReader.RequiredGuid(principal, "broker_account_id"),
            generation,
            ClaimReader.Required(principal, "region"),
            ClaimReader.Required(principal, "component"));
    }
}
