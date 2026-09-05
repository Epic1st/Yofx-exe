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

EnvironmentFileLoader.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    // Signed marketplace uploads can carry a 64 MiB package as Base64. A path-
    // aware limit below keeps every normal API request at the original 1 MiB.
    options.Limits.MaxRequestBodySize = 90L * 1024 * 1024;
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
builder.Services.AddYo4xUserAndWorkloadAuthentication(builder.Configuration, builder.Environment);
builder.Services.TryAddControlPlanePostgres(builder.Configuration, builder.Environment);
builder.Services.TryAddRuntimeControlPostgres(builder.Configuration, builder.Environment);
builder.Services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();
builder.Services.TryAddScoped<IFrontendProjectionApplication, UnavailableFrontendProjectionApplication>();
builder.Services.TryAddScoped<IRuntimeControlPlaneApplication, UnavailableRuntimeControlPlaneApplication>();
builder.Services.TryAddScoped<IBotExecutionCoordinator, ProjectionBotExecutionCoordinator>();
builder.Services.AddSingleton<ControlPlaneReadinessProbe>();
builder.Services.AddHostedService<LocalBotRunExpiryService>();
builder.Services.AddDevelopmentMt5ConnectionProbe(builder.Configuration, builder.Environment);
builder.Services.AddLocalBrokerCredentialVault(builder.Configuration);
builder.Services.TryAddLocalBotExecution(builder.Configuration, builder.Environment);
string[] frontendOrigins = builder.Configuration
    .GetSection("Frontend:AllowedOrigins")
    .Get<string[]>()
    ?? (builder.Environment.IsDevelopment()
        ? ["http://127.0.0.1:5173", "http://127.0.0.1:4173", "http://127.0.0.1:4174"]
        : []);
foreach (string origin in frontendOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed)
        || parsed.GetLeftPart(UriPartial.Authority) != origin
        || parsed.Scheme != Uri.UriSchemeHttps
           && (parsed.Scheme != Uri.UriSchemeHttp || !parsed.IsLoopback))
        throw new InvalidOperationException("Frontend allowed origins must be exact HTTPS origins or HTTP loopback origins.");
}
if (frontendOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("desktop-frontend", policy =>
        policy.WithOrigins(frontendOrigins)
            .WithHeaders(
                "Accept",
                "Authorization",
                "Content-Type",
                ApiHeaders.IdempotencyKey,
                ApiHeaders.IfMatch,
                ApiHeaders.CorrelationId)
            .WithMethods("GET", "POST", "PUT", "OPTIONS")
            .AllowCredentials()));
}

// Password members bind straight from the request bytes into a buffer this
// process can erase, so no broker password is ever materialized as a string.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(json =>
    json.SerializerOptions.Converters.Add(new Utf8SecretJsonConverter()));

WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
    Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature? feature =
        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (feature is { IsReadOnly: false })
    {
        feature.MaxRequestBodySize = context.Request.Path.StartsWithSegments(
            "/internal/v1/marketplace/publications", StringComparison.Ordinal)
            || context.Request.Path.StartsWithSegments(
                "/internal/v1/marketplace/mql5-publications", StringComparison.Ordinal)
                ? 90L * 1024 * 1024
                : 1024 * 1024;
    }
    await next(context);
});
app.UseYo4xApiFoundation();
app.UseYo4xHttpsOnly();
if (frontendOrigins.Length > 0)
{
    app.UseCors("desktop-frontend");
}
app.UseYo4xProblemStatusCodes();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.MapMarketplacePublicationEndpoint();
app.MapLocalBotExecutionReadiness();

ControlPlaneReadinessProbe readiness = app.Services.GetRequiredService<ControlPlaneReadinessProbe>();
app.MapYo4xHealth(
    _ => ValueTask.FromResult(true),
    readiness.IsReadyAsync,
    health =>
    {
        // The control-plane readiness probe re-attests four least-privilege logins
        // and recomputes the whole-catalog semantic manifest for each, so it costs
        // seconds rather than milliseconds. The outer deadline must sit above the
        // probe's own bound so the probe's fail-closed result is what is published,
        // and the snapshot must live long enough that anonymous polling cannot pin
        // a database connection to continuous re-attestation.
        health.ProbeTimeout = TimeSpan.FromSeconds(12);
        health.SnapshotLifetime = TimeSpan.FromSeconds(5);
    });

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

user.MapPost("/broker-accounts", async (
    CreateBrokerAccountBody request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    BrokerAccountLinkRequest link = BrokerAccountLinkValidation.Validate(request);
    BrokerAccountView account = await application.CreateBrokerAccountAsync(
        ToUserActor(context.User),
        BrokerAccountLinkValidation.ToApplicationRequest(request, link),
        ToMetadata(context),
        cancellationToken);

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";
    return Results.Created($"/v1/broker-accounts/{account.Id:D}", account);
}).AddEndpointFilter(new MutationPreconditionFilter());

// Approving a directory server is what makes it linkable for the caller's own
// tenant. It is deliberately one server per request: there is no bulk route.
user.MapPost("/broker-server-approvals", async (
    ApproveBrokerServer request,
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
{
    BrokerAccountRegistrationOption option = await application.ApproveBrokerServerAsync(
        ToUserActor(context.User),
        request,
        ToMetadata(context),
        cancellationToken);
    return Results.Created(
        $"/v1/broker-account-registration-options?query={Uri.EscapeDataString(option.Server)}",
        option);
}).AddEndpointFilter(new MutationPreconditionFilter());

user.MapBrokerAccountDiscovery();
user.MapFrontendProjections();
user.MapMarketplaceUserEndpoints();
user.MapDevelopmentMt5ConnectionProbe(builder.Configuration, builder.Environment);

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

user.MapGet("/strategy-source-corpora", async (
    HttpContext context,
    IControlPlaneApplication application,
    CancellationToken cancellationToken) =>
    Results.Ok(await application.GetStrategySourceCorporaAsync(
        ToUserActor(context.User), cancellationToken)));

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

    if (address.IsIPv4MappedToIPv6)
    {
        address = address.MapToIPv4();
    }

    if (IPAddress.IsLoopback(address))
    {
        return "loopback";
    }

    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        byte[] ipv6 = address.GetAddressBytes();
        bool privateIpv6 = address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || (ipv6[0] & 0xfe) == 0xfc;
        return privateIpv6 ? "private" : "public";
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
