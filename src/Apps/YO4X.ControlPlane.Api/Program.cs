using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Api;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Deployments;
using YO4X.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
    options.ConfigureHttpsDefaults(https =>
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate);
});
builder.Services.AddYo4xApiFoundation(options =>
    options.ErrorTypeBase = builder.Configuration["Api:ErrorTypeBase"] ?? "https://errors.yo4x.invalid");
builder.Services.AddRequestTimeouts(options =>
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = ControlPlanePostgresOptions.ProofKeyReplayRequestSafetyMargin
    });
builder.Services.AddYo4xUserAndWorkloadAuthentication(builder.Configuration);
builder.Services.TryAddControlPlanePostgres(builder.Configuration, builder.Environment);
builder.Services.TryAddRuntimeControlPostgres(builder.Configuration, builder.Environment);
builder.Services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();
builder.Services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();
builder.Services.AddSingleton<ControlPlaneReadinessProbe>();

WebApplication app = builder.Build();
app.UseYo4xApiFoundation();
app.UseYo4xHttpsOnly();
app.UseYo4xProblemStatusCodes();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();

ControlPlaneReadinessProbe readiness = app.Services.GetRequiredService<ControlPlaneReadinessProbe>();
app.MapYo4xHealth(
    _ => ValueTask.FromResult(true),
    readiness.IsReadyAsync);

RouteGroupBuilder user = app.MapGroup("/v1").RequireAuthorization("user");

user.MapGet("/me", async (HttpContext context, IControlPlaneApplication application, CancellationToken cancellationToken) =>
{
    UserView? result = await application.GetMeAsync(ToUserActor(context.User), cancellationToken);
    return result is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(result);
});

user.MapGet("/me/sessions", async (HttpContext context, IControlPlaneApplication application, CancellationToken cancellationToken) =>
    Results.Ok(await application.GetSessionsAsync(ToUserActor(context.User), cancellationToken)));

user.MapPost("/me/sessions/{sessionId:guid}/revoke", async (
    Guid sessionId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    await application.RevokeSessionAsync(
        ToUserActor(context.User),
        sessionId,
        ToMetadata(context),
        cancellationToken);
    return Results.NoContent();
}).AddEndpointFilter(new MutationPreconditionFilter());

user.MapPost("/auth/logout", async (
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    UserActor actor = ToUserActor(context.User);
    await application.RevokeSessionAsync(actor, actor.SessionId, ToMetadata(context), cancellationToken);
    return Results.NoContent();
}).AddEndpointFilter(new MutationPreconditionFilter());

user.MapPost("/auth/mfa/challenge", (HttpContext context) =>
    ApiProblems.Create(
        context,
        StatusCodes.Status503ServiceUnavailable,
        "IDENTITY_PROVIDER_NOT_CONFIGURED",
        "The MFA provider is not configured."));

user.MapPost("/auth/refresh", (HttpContext context) =>
    ApiProblems.Create(
        context,
        StatusCodes.Status503ServiceUnavailable,
        "IDENTITY_PROVIDER_NOT_CONFIGURED",
        "The token refresh provider is not configured."))
    .AllowAnonymous();

user.MapPost("/cloud-credential-ingestion-sessions", async (
    CreateCredentialIngestionSession request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    CredentialIngestionSessionView session = await application.CreateCredentialIngestionSessionAsync(
        ToUserActor(context.User),
        request,
        ToMetadata(context),
        cancellationToken);
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    return Results.Created($"/v1/cloud-credential-ingestion-sessions/{session.GrantId}", session);
}).AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion: true));

user.MapPost("/strategy-source-import-sessions", async (
    CreateStrategyImportSession request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    StrategyImportSessionView session = await application.CreateStrategyImportSessionAsync(
        ToUserActor(context.User),
        request,
        ToMetadata(context),
        cancellationToken);
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    return Results.Created($"/v1/strategy-source-import-sessions/{session.ImportJobId:D}", session);
}).AddEndpointFilter(new MutationPreconditionFilter());

user.MapPost("/strategy-source-import-sessions/{importJobId:guid}/revoke", async (
    Guid importJobId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    await application.RevokeStrategyImportSessionAsync(
        ToUserActor(context.User),
        importJobId,
        ToMetadata(context),
        cancellationToken);
    return Results.NoContent();
}).AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion: true));

user.MapGet("/broker-accounts/{brokerAccountId:guid}", async (
    Guid brokerAccountId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    BrokerAccountView? account = await application.GetBrokerAccountAsync(
        ToUserActor(context.User), brokerAccountId, cancellationToken);
    return account is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(account);
});

user.MapGet("/broker-accounts/{brokerAccountId:guid}/credential-state", async (
    Guid brokerAccountId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    var state = await application.GetCredentialStateAsync(ToUserActor(context.User), brokerAccountId, cancellationToken);
    return state is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(state);
});

MapBrokerAction(
    user,
    "/broker-accounts/{brokerAccountId:guid}/cloud-connection-tests",
    BrokerAccountAction.TestCloudConnection,
    requireVersion: true);
user.MapPost("/broker-accounts/{brokerAccountId:guid}/credential-rotation-sessions", async (
    Guid brokerAccountId,
    CreateCredentialRotationSession request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    CredentialIngestionSessionView session = await application.CreateCredentialIngestionSessionAsync(
        ToUserActor(context.User),
        new CreateCredentialIngestionSession(
            brokerAccountId,
            YO4X.SecretCoordination.CredentialIngestionOperation.Rotate,
            request.ClientOrigin),
        ToMetadata(context),
        cancellationToken);
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    return Results.Created($"/v1/cloud-credential-ingestion-sessions/{session.GrantId}", session);
}).AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion: true));
MapBrokerAction(user, "/broker-accounts/{brokerAccountId:guid}/disable-cloud-use", BrokerAccountAction.DisableCloudUse, requireVersion: true);
MapBrokerAction(user, "/broker-accounts/{brokerAccountId:guid}/credential-deletion-requests", BrokerAccountAction.RequestCredentialDeletion, requireVersion: true);

user.MapPost("/deployments/validate", async (
    ValidateDeployment request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    IReadOnlyList<string> findings = await application.ValidateDeploymentAsync(
        ToUserActor(context.User), request, cancellationToken);
    return Results.Ok(new { valid = findings.Count == 0, findings });
});

user.MapPost("/deployments", async (
    CreateDeployment request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    DeploymentView deployment = await application.CreateDeploymentAsync(
        ToUserActor(context.User), request, ToMetadata(context), cancellationToken);
    return Results.Created($"/v1/deployments/{deployment.Id}", deployment);
}).AddEndpointFilter(new MutationPreconditionFilter());

user.MapGet("/deployments/{deploymentId:guid}", async (
    Guid deploymentId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    DeploymentView? deployment = await application.GetDeploymentAsync(
        ToUserActor(context.User), deploymentId, cancellationToken);
    return deployment is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(deployment);
});

MapDeploymentAction(user, "/deployments/{deploymentId:guid}/start", DeploymentState.Starting, requireVersion: true);
MapDeploymentAction(user, "/deployments/{deploymentId:guid}/close-only", DeploymentState.CloseOnly, requireVersion: true);
MapDeploymentAction(user, "/deployments/{deploymentId:guid}/stop-after-flat", DeploymentState.StopAfterFlat, requireVersion: true);

user.MapGet("/deployments/{deploymentId:guid}/activity", async (
    Guid deploymentId,
    int? limit,
    Guid? before,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    int boundedLimit = Math.Clamp(limit ?? 50, 1, 100);
    return Results.Ok(await application.GetDeploymentActivityAsync(
        ToUserActor(context.User), deploymentId, boundedLimit, before, cancellationToken));
});

user.MapGet("/strategy-source-corpora/{corpusId:guid}/compatibility", async (
    Guid corpusId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    StrategyCompatibilityProjection? projection = await application.GetStrategyCompatibilityAsync(
        ToUserActor(context.User), corpusId, cancellationToken);
    return projection is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(projection);
});

user.MapGet("/operations/{operationId:guid}", async (
    Guid operationId,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    UserOperationView? operation = await application.GetOperationAsync(
        ToUserActor(context.User), operationId, cancellationToken);
    return operation is null
        ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
        : Results.Ok(operation);
});

RouteGroupBuilder runtime = app.MapGroup("/internal/v1")
    .RequireAuthorization("workload")
    .AddEndpointFilter(new ClientCertificateFilter());

runtime.MapPost("/workers/register", async (
    WorkerRegistration request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    WorkerRegistrationView worker = await application.RegisterWorkerAsync(
        ToWorkloadActor(context.User), request, ToMetadata(context), cancellationToken);
    return Results.Created($"/internal/v1/workers/{worker.WorkerId}", worker);
}).AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/workers/{workerId:guid}/components/{component}/heartbeat", async (
    Guid workerId,
    string component,
    ComponentHeartbeat request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    YO4X.Runtime.Contracts.RuntimeComponentRole componentRole = ParseComponent(component);
    await application.RecordHeartbeatAsync(
        ToWorkloadActor(context.User), workerId, componentRole, request, ToMetadata(context), cancellationToken);
    return Results.NoContent();
}).AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/execution-leases/issue", async (
    IssueExecutionLease request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Ok(await application.IssueLeaseAsync(
        ToWorkloadActor(context.User), request, ToMetadata(context), cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/execution-leases/renew", async (
    RenewExecutionLease request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Ok(await application.RenewLeaseAsync(
        ToWorkloadActor(context.User), request, ToMetadata(context), cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/deployments/{deploymentId:guid}/events", async (
    Guid deploymentId,
    RuntimeEventInput request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Accepted(value: await application.RecordDeploymentEventAsync(
        ToWorkloadActor(context.User), deploymentId, request, ToMetadata(context), cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/command-targets/{targetId:guid}/delivery-events", async (
    Guid targetId,
    TargetDeliveryInput request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Accepted(value: await application.RecordTargetDeliveryAsync(
        ToWorkloadActor(context.User), targetId, request, ToMetadata(context), cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/command-targets/{targetId:guid}/reconciliation-results", async (
    Guid targetId,
    TargetDeliveryInput request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Accepted(value: await application.RecordTargetReconciliationAsync(
        ToWorkloadActor(context.User), targetId, request, ToMetadata(context), cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/broker-accounts/{brokerAccountId:guid}/operation-results", async (
    Guid brokerAccountId,
    BrokerUserOperationResultInput request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Accepted(value: await application.RecordBrokerUserOperationResultAsync(
        ToWorkloadActor(context.User),
        brokerAccountId,
        request,
        ToMetadata(context),
        cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

runtime.MapPost("/deployments/{deploymentId:guid}/operation-results", async (
    Guid deploymentId,
    DeploymentUserOperationResultInput request,
    HttpContext context,
    IRuntimeControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Accepted(value: await application.RecordDeploymentUserOperationResultAsync(
        ToWorkloadActor(context.User),
        deploymentId,
        request,
        ToMetadata(context),
        cancellationToken)))
    .AddEndpointFilter(new MutationPreconditionFilter());

app.Run();

static void MapBrokerAction(
    RouteGroupBuilder group,
    string pattern,
    BrokerAccountAction action,
    bool requireVersion = false)
{
    group.MapPost(pattern, async (
        Guid brokerAccountId,
        DeploymentAction request,
        HttpContext context,
        IControlPlaneApplication application,
        CancellationToken cancellationToken) =>
    {
        AcceptedOperation accepted = await application.RequestBrokerAccountActionAsync(
            ToUserActor(context.User),
            brokerAccountId,
            action,
            request,
            ToMetadata(context, request.ReasonCode),
            cancellationToken);
        return Results.Accepted(accepted.StatusUrl.ToString(), accepted);
    }).AddEndpointFilter(new MutationPreconditionFilter(requireVersion));
}

static void MapDeploymentAction(
    RouteGroupBuilder group,
    string pattern,
    DeploymentState requestedState,
    bool requireVersion)
{
    group.MapPost(pattern, async (
        Guid deploymentId,
        DeploymentAction request,
        HttpContext context,
        IControlPlaneApplication application,
        CancellationToken cancellationToken) =>
    {
        AcceptedOperation accepted = await application.RequestDeploymentActionAsync(
            ToUserActor(context.User),
            deploymentId,
            requestedState,
            request,
            ToMetadata(context, request.ReasonCode),
            cancellationToken);
        return Results.Accepted(accepted.StatusUrl.ToString(), accepted);
    }).AddEndpointFilter(new MutationPreconditionFilter(requireVersion));
}

static UserActor ToUserActor(ClaimsPrincipal principal)
{
    string assuranceValue = principal.FindFirstValue("assurance") ?? "password";
    AuthenticationAssurance assurance = assuranceValue.ToLowerInvariant() switch
    {
        "hardware_key" => AuthenticationAssurance.HardwareKey,
        "webauthn" => AuthenticationAssurance.WebAuthn,
        "totp" => AuthenticationAssurance.Totp,
        _ => AuthenticationAssurance.Password
    };

    return new UserActor(
        ClaimReader.RequiredGuid(principal, "tenant_id"),
        ClaimReader.RequiredGuid(principal, "sub"),
        ClaimReader.RequiredGuid(principal, "session_id"),
        assurance);
}

static RequestMetadata ToMetadata(HttpContext context, string? reason = null)
{
    MutationPreconditions preconditions = MutationPreconditionFilter.Get(context);
    return new RequestMetadata(
        preconditions.IdempotencyKey,
        CorrelationIdMiddleware.GetGuid(context),
        preconditions.ExpectedVersion,
        reason,
        ClassifySourceNetwork(context.Connection.RemoteIpAddress));
}

static string ClassifySourceNetwork(IPAddress? address)
{
    if (address is null)
    {
        return "unknown";
    }

    if (IPAddress.IsLoopback(address))
    {
        return "loopback";
    }

    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        else
        {
            byte[] ipv6 = address.GetAddressBytes();
            bool privateIpv6 = address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || (ipv6[0] & 0xfe) == 0xfc;
            return privateIpv6 ? "private" : "public";
        }
    }

    byte[] bytes = address.GetAddressBytes();
    bool privateAddress = bytes[0] == 10
        || bytes[0] == 127
        || (bytes[0] == 169 && bytes[1] == 254)
        || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
        || (bytes[0] == 192 && bytes[1] == 168);
    return privateAddress ? "private" : "public";
}

static WorkloadActor ToWorkloadActor(ClaimsPrincipal principal) => WorkloadActorClaims.Read(principal);

static YO4X.Runtime.Contracts.RuntimeComponentRole ParseComponent(string value) => value.ToUpperInvariant() switch
{
    "SUPERVISOR" => YO4X.Runtime.Contracts.RuntimeComponentRole.Supervisor,
    "STRATEGY_HOST" => YO4X.Runtime.Contracts.RuntimeComponentRole.StrategyHost,
    "GATEWAY_HOST" => YO4X.Runtime.Contracts.RuntimeComponentRole.GatewayHost,
    _ => throw new YO4X.BuildingBlocks.DomainException("RUNTIME_COMPONENT_UNKNOWN", "The runtime component is not allowlisted.")
};

public partial class Program;
