using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using YO4X.Admin.Application;
using YO4X.Api;
using YO4X.Approvals;
using YO4X.Authorization;
using YO4X.Commands;
using YO4X.Policy;

namespace YO4X.Admin.Bff;

internal sealed record ApprovalDecisionRequest(
    string ReasonCode,
    string WrittenReason,
    string? TicketReference,
    string BindingDigest);

internal sealed record CommandCancellationRequest(
    string ReasonCode,
    string WrittenReason,
    string? TicketReference);

internal sealed record CommandCompensationRequest(
    CommandType CompensationType,
    string ReasonCode,
    string WrittenReason,
    string? TicketReference);

internal sealed record AdminContainmentRequest(
    string ReasonCode,
    string WrittenReason,
    string? TicketReference);

internal static partial class AdminRoutes
{
    private const int MaximumReasonCodeLength = 64;
    private const int MaximumWrittenReasonLength = 2_000;
    private const int MaximumTicketLength = 128;

    public static void MapAdminRoutes(this WebApplication app, AdminOriginPolicy originPolicy)
    {
        RouteGroupBuilder admin = app.MapGroup("/admin/v1").RequireAuthorization("admin");

        admin.MapGet("/antiforgery-token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new
            {
                requestToken = tokens.RequestToken,
                headerName = tokens.HeaderName
            });
        });

        admin.MapGet("/me", async (
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) => Results.Ok(
                await application.GetMeAsync(ToAdminActor(context.User), cancellationToken)));

        admin.MapGet("/approvals", async (
            int? limit,
            Guid? before,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) => Results.Ok(
                await application.GetApprovalsAsync(
                    ToAdminActor(context.User),
                    Math.Clamp(limit ?? 50, 1, 100),
                    before,
                    cancellationToken)));

        admin.MapGet("/approvals/{approvalId:guid}", async (
            Guid approvalId,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) =>
        {
            ApprovalSummary? approval = await application.GetApprovalAsync(
                ToAdminActor(context.User), approvalId, cancellationToken);
            return approval is null ? NotFound(context) : Results.Ok(approval);
        });

        MapApprovalDecision(admin, originPolicy, "approve", ApprovalDecisionType.Approve);
        MapApprovalDecision(admin, originPolicy, "reject", ApprovalDecisionType.Reject);

        admin.MapGet("/commands/{commandId:guid}", async (
            Guid commandId,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) =>
        {
            CommandSummary? command = await application.GetCommandAsync(
                ToAdminActor(context.User), commandId, cancellationToken);
            return command is null ? NotFound(context) : Results.Ok(command);
        });

        admin.MapGet("/commands/{commandId:guid}/targets", async (
            Guid commandId,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) => Results.Ok(
                await application.GetCommandTargetsAsync(
                    ToAdminActor(context.User), commandId, cancellationToken)));

        RouteHandlerBuilder cancel = admin.MapPost("/commands/{commandId:guid}/cancel", async (
            Guid commandId,
            CommandCancellationRequest request,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) =>
        {
            IResult? invalid = ValidateReason(
                context,
                request.ReasonCode,
                request.WrittenReason,
                request.TicketReference);
            if (invalid is not null)
            {
                return invalid;
            }

            CommandSummary? command = await application.CancelCommandAsync(
                ToAdminActor(context.User),
                commandId,
                ToMetadata(
                    context,
                    request.ReasonCode,
                    request.WrittenReason,
                    request.TicketReference),
                cancellationToken);
            return command is null ? NotFound(context) : Results.Ok(command);
        });
        AddMutationGuards(cancel, originPolicy, requireExpectedVersion: true);

        RouteHandlerBuilder compensate = admin.MapPost(
            "/commands/{commandId:guid}/compensations",
            async (
                Guid commandId,
                CommandCompensationRequest request,
                HttpContext context,
                IAdminApplication application,
                CancellationToken cancellationToken) =>
            {
                IResult? invalid = ValidateReason(
                    context,
                    request.ReasonCode,
                    request.WrittenReason,
                    request.TicketReference);
                if (invalid is not null)
                {
                    return invalid;
                }

                if (!Enum.IsDefined(request.CompensationType))
                {
                    return ApiProblems.Create(
                        context,
                        StatusCodes.Status400BadRequest,
                        "COMPENSATION_TYPE_UNKNOWN",
                        "The compensation command type is not allowlisted.");
                }

                CommandAccepted accepted = await application.RequestCompensationAsync(
                    ToAdminActor(context.User),
                    commandId,
                    new CompensationInput(
                        request.CompensationType,
                        request.ReasonCode,
                        request.WrittenReason),
                    ToMetadata(
                        context,
                        request.ReasonCode,
                        request.WrittenReason,
                        request.TicketReference),
                    cancellationToken);
                return Results.Accepted(accepted.StatusUrl.ToString(), accepted);
            });
        AddMutationGuards(compensate, originPolicy, requireExpectedVersion: true);

        admin.MapGet("/deployments/{deploymentId:guid}", async (
            Guid deploymentId,
            string? purpose,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > MaximumWrittenReasonLength)
            {
                return ApiProblems.Create(
                    context,
                    StatusCodes.Status428PreconditionRequired,
                    "PURPOSE_REQUIRED",
                    "A bounded purpose is required for this sensitive read.");
            }

            var deployment = await application.GetDeploymentAsync(
                ToAdminActor(context.User), deploymentId, purpose.Trim(), cancellationToken);
            return deployment is null ? NotFound(context) : Results.Ok(deployment);
        });

        MapDeploymentContainment(
            admin,
            originPolicy,
            "/deployments/{deploymentId:guid}/close-only",
            CommandType.CloseOnly,
            CreateCloseOnlyVector);
        MapDeploymentContainment(
            admin,
            originPolicy,
            "/deployments/{deploymentId:guid}/stop-after-flat",
            CommandType.StopAfterFlat,
            CreateStopAfterFlatVector);
        MapDeploymentContainment(
            admin,
            originPolicy,
            "/deployments/{deploymentId:guid}/revoke-lease",
            CommandType.RevokeLease,
            CreateLeaseRevocationVector);
        MapDeploymentContainment(
            admin,
            originPolicy,
            "/deployments/{deploymentId:guid}/replace-worker",
            CommandType.ReplaceWorker,
            CreateWorkerReplacementVector);
    }

    private static void MapApprovalDecision(
        RouteGroupBuilder group,
        AdminOriginPolicy originPolicy,
        string action,
        ApprovalDecisionType decision)
    {
        RouteHandlerBuilder route = group.MapPost(
            $"/approvals/{{approvalId:guid}}/{action}",
            async (
                Guid approvalId,
                ApprovalDecisionRequest request,
                HttpContext context,
                IAdminApplication application,
                CancellationToken cancellationToken) =>
            {
                IResult? invalid = ValidateReason(
                    context,
                    request.ReasonCode,
                    request.WrittenReason,
                    request.TicketReference);
                invalid ??= ValidateSha256(context, request.BindingDigest, "/bindingDigest");
                if (invalid is not null)
                {
                    return invalid;
                }

                CommandSummary? command = await application.DecideApprovalAsync(
                    ToAdminActor(context.User),
                    approvalId,
                    decision,
                    new ApprovalDecisionInput(request.WrittenReason, request.BindingDigest),
                    ToMetadata(
                        context,
                        request.ReasonCode,
                        request.WrittenReason,
                        request.TicketReference),
                    cancellationToken);
                return command is null ? NotFound(context) : Results.Ok(command);
            });
        AddMutationGuards(route, originPolicy, requireExpectedVersion: true);
    }

    private static void MapDeploymentContainment(
        RouteGroupBuilder group,
        AdminOriginPolicy originPolicy,
        string pattern,
        CommandType commandType,
        Func<ExecutionSafetyPolicyVector> vectorFactory)
    {
        RouteHandlerBuilder route = group.MapPost(pattern, async (
            Guid deploymentId,
            AdminContainmentRequest request,
            HttpContext context,
            IAdminApplication application,
            CancellationToken cancellationToken) =>
        {
            IResult? invalid = ValidateReason(
                context,
                request.ReasonCode,
                request.WrittenReason,
                request.TicketReference);
            if (invalid is not null)
            {
                return invalid;
            }

            CommandAccepted accepted = await application.RequestContainmentAsync(
                ToAdminActor(context.User),
                commandType,
                new ScopeInput("DEPLOYMENT", deploymentId.ToString("D")),
                vectorFactory(),
                ToMetadata(
                    context,
                    request.ReasonCode,
                    request.WrittenReason,
                    request.TicketReference),
                cancellationToken);
            return Results.Accepted(accepted.StatusUrl.ToString(), accepted);
        });
        AddMutationGuards(route, originPolicy, requireExpectedVersion: true);
    }

    private static void AddMutationGuards(
        RouteHandlerBuilder route,
        AdminOriginPolicy originPolicy,
        bool requireExpectedVersion)
    {
        route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        route.AddEndpointFilter(new AdminOriginFilter(originPolicy));
        route.AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion));
    }

    private static AdminActor ToAdminActor(ClaimsPrincipal principal)
    {
        string mfa = ClaimReader.Required(principal, "mfa");
        AuthenticationAssurance assurance = mfa.ToLowerInvariant() switch
        {
            "hardware_key" or "webauthn" => AuthenticationAssurance.PhishingResistant,
            "totp" => AuthenticationAssurance.MultiFactor,
            _ => AuthenticationAssurance.Unknown
        };

        FrozenSet<string> permissions = principal.FindAll("permission")
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToFrozenSet(StringComparer.Ordinal);

        return new AdminActor(
            ClaimReader.RequiredGuid(principal, "tenant_id"),
            ClaimReader.RequiredGuid(principal, "sub"),
            ClaimReader.RequiredGuid(principal, "admin_session_id"),
            ClaimReader.Required(principal, "environment"),
            assurance,
            string.Equals(
                ClaimReader.Required(principal, "managed_device"),
                "true",
                StringComparison.OrdinalIgnoreCase),
            ReadAuthenticatedAt(principal),
            permissions);
    }

    private static DateTimeOffset ReadAuthenticatedAt(ClaimsPrincipal principal)
    {
        string value = ClaimReader.Required(principal, "auth_time");
        if (!long.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out long seconds))
        {
            throw new UnauthorizedAccessException("The admin authentication time is invalid.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new UnauthorizedAccessException(
                "The admin authentication time is invalid.",
                exception);
        }
    }

    private static AdminRequestMetadata ToMetadata(
        HttpContext context,
        string reasonCode,
        string writtenReason,
        string? ticketReference)
    {
        MutationPreconditions preconditions = MutationPreconditionFilter.Get(context);
        return new AdminRequestMetadata(
            preconditions.IdempotencyKey,
            CorrelationIdMiddleware.GetGuid(context),
            preconditions.ExpectedVersion,
            reasonCode.Trim(),
            writtenReason.Trim(),
            string.IsNullOrWhiteSpace(ticketReference) ? null : ticketReference.Trim());
    }

    private static IResult? ValidateReason(
        HttpContext context,
        string? reasonCode,
        string? writtenReason,
        string? ticketReference)
    {
        var errors = new List<ApiValidationError>();
        if (string.IsNullOrWhiteSpace(reasonCode)
            || reasonCode.Length > MaximumReasonCodeLength
            || !ReasonCodePattern().IsMatch(reasonCode))
        {
            errors.Add(new ApiValidationError(
                "/reasonCode",
                "INVALID",
                "Reason code must be a bounded allowlist-style identifier."));
        }

        if (string.IsNullOrWhiteSpace(writtenReason)
            || writtenReason.Length > MaximumWrittenReasonLength)
        {
            errors.Add(new ApiValidationError(
                "/writtenReason",
                "INVALID",
                "A written reason between 1 and 2000 characters is required."));
        }

        if (ticketReference?.Length > MaximumTicketLength)
        {
            errors.Add(new ApiValidationError(
                "/ticketReference",
                "TOO_LONG",
                "Ticket reference cannot exceed 128 characters."));
        }

        return errors.Count == 0
            ? null
            : ApiProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "The request failed validation.",
                errors);
    }

    private static IResult? ValidateSha256(HttpContext context, string? value, string path) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit)
            ? null
            : ApiProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "DIGEST_INVALID",
                "A hexadecimal SHA-256 digest is required.",
                [new ApiValidationError(path, "INVALID", "Expected 64 hexadecimal characters.")]);

    private static IResult NotFound(HttpContext context) => ApiProblems.Create(
        context,
        StatusCodes.Status404NotFound,
        "RESOURCE_NOT_FOUND",
        "The resource was not found.");

    private static ExecutionSafetyPolicyVector CreateCloseOnlyVector() => new(
        allowNewDeployment: true,
        allowStrategySignals: false,
        allowExposureIncrease: false,
        allowExposureReduction: true,
        allowProtection: true,
        allowPendingOrderCancellation: true,
        allowEmergencyClose: true,
        LeaseMode.Normal,
        WorkerAction.None,
        CredentialMode.Normal,
        PackageEligibility.Eligible);

    private static ExecutionSafetyPolicyVector CreateStopAfterFlatVector() => new(
        allowNewDeployment: true,
        allowStrategySignals: false,
        allowExposureIncrease: false,
        allowExposureReduction: true,
        allowProtection: true,
        allowPendingOrderCancellation: true,
        allowEmergencyClose: true,
        LeaseMode.Normal,
        WorkerAction.StopAfterFlat,
        CredentialMode.Normal,
        PackageEligibility.Eligible);

    private static ExecutionSafetyPolicyVector CreateLeaseRevocationVector() => new(
        allowNewDeployment: false,
        allowStrategySignals: false,
        allowExposureIncrease: false,
        allowExposureReduction: true,
        allowProtection: true,
        allowPendingOrderCancellation: true,
        allowEmergencyClose: true,
        LeaseMode.Revoke,
        WorkerAction.Drain,
        CredentialMode.Normal,
        PackageEligibility.Eligible);

    private static ExecutionSafetyPolicyVector CreateWorkerReplacementVector() => new(
        allowNewDeployment: false,
        allowStrategySignals: false,
        allowExposureIncrease: false,
        allowExposureReduction: true,
        allowProtection: true,
        allowPendingOrderCancellation: true,
        allowEmergencyClose: true,
        LeaseMode.Revoke,
        WorkerAction.Drain | WorkerAction.Fence | WorkerAction.Replace,
        CredentialMode.DisableNewUse,
        PackageEligibility.Eligible);

    [GeneratedRegex("^[A-Z][A-Z0-9_.:-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonCodePattern();
}
