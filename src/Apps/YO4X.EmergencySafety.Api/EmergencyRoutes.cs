using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.RegularExpressions;
using YO4X.Admin.Application;
using YO4X.Api;
using YO4X.Authorization;

namespace YO4X.EmergencySafety.Api;

internal sealed record SubmitRestrictiveCommandRequest(
    EmergencyTemplate Template,
    ScopeInput Scope,
    Guid IncidentId,
    string? ExactDigest,
    string ReasonCode,
    string WrittenReason,
    Guid PreviewId,
    string PreviewDigest);

internal static partial class EmergencyRoutes
{
    private const int MaximumReasonCodeLength = 64;
    private const int MaximumWrittenReasonLength = 2_000;

    public static void MapEmergencyRoutes(this WebApplication app)
    {
        RouteGroupBuilder emergency = app.MapGroup("/emergency/v1")
            .RequireAuthorization("emergency-restrictive")
            .AddEndpointFilter(new ClientCertificateFilter());

        RouteHandlerBuilder preview = emergency.MapPost(
            "/restrictive-command-previews",
            async (
                RestrictiveCommandInput request,
                HttpContext context,
                IEmergencySafetyApplication application,
                CancellationToken cancellationToken) =>
            {
                if (!TryNormalize(request, out RestrictiveCommandInput normalized, out var errors))
                {
                    return ValidationProblem(context, errors);
                }

                RestrictivePreview result = await application.PreviewAsync(
                    ToEmergencyActor(context.User),
                    normalized,
                    CorrelationIdMiddleware.GetGuid(context),
                    cancellationToken);
                return Results.Ok(result);
            });
        preview.AddEndpointFilter(new MutationPreconditionFilter());

        RouteHandlerBuilder submit = emergency.MapPost(
            "/restrictive-commands",
            async (
                SubmitRestrictiveCommandRequest request,
                HttpContext context,
                IEmergencySafetyApplication application,
                CancellationToken cancellationToken) =>
            {
                var input = new RestrictiveCommandInput(
                    request.Template,
                    request.Scope,
                    request.IncidentId,
                    request.ExactDigest,
                    request.ReasonCode,
                    request.WrittenReason);
                if (!TryNormalize(input, out RestrictiveCommandInput normalized, out var errors))
                {
                    return ValidationProblem(context, errors);
                }

                if (request.PreviewId == Guid.Empty)
                {
                    errors.Add(new ApiValidationError(
                        "/previewId",
                        "INVALID",
                        "A non-empty preview identifier is required."));
                }

                if (!IsSha256(request.PreviewDigest))
                {
                    errors.Add(new ApiValidationError(
                        "/previewDigest",
                        "INVALID",
                        "A hexadecimal SHA-256 preview digest is required."));
                }

                if (errors.Count > 0)
                {
                    return ValidationProblem(context, errors);
                }

                MutationPreconditions preconditions = MutationPreconditionFilter.Get(context);
                var metadata = new AdminRequestMetadata(
                    preconditions.IdempotencyKey,
                    CorrelationIdMiddleware.GetGuid(context),
                    preconditions.ExpectedVersion,
                    normalized.ReasonCode,
                    normalized.WrittenReason,
                    $"incident:{normalized.IncidentId:D}");
                CommandAccepted accepted = await application.SubmitAsync(
                    ToEmergencyActor(context.User),
                    normalized,
                    request.PreviewId,
                    request.PreviewDigest.ToLowerInvariant(),
                    metadata,
                    cancellationToken);
                return Results.Accepted(accepted.StatusUrl.ToString(), accepted);
            });
        submit.AddEndpointFilter(new MutationPreconditionFilter());

        emergency.MapGet("/restrictive-commands/{commandId:guid}", async (
            Guid commandId,
            HttpContext context,
            IEmergencySafetyApplication application,
            CancellationToken cancellationToken) =>
        {
            CommandSummary? command = await application.GetAsync(
                ToEmergencyActor(context.User), commandId, cancellationToken);
            return command is null ? NotFound(context) : Results.Ok(command);
        });

        emergency.MapGet("/restrictive-commands/{commandId:guid}/targets", async (
            Guid commandId,
            HttpContext context,
            IEmergencySafetyApplication application,
            CancellationToken cancellationToken) => Results.Ok(
                await application.GetTargetsAsync(
                    ToEmergencyActor(context.User), commandId, cancellationToken)));
    }

    private static bool TryNormalize(
        RestrictiveCommandInput request,
        out RestrictiveCommandInput normalized,
        out List<ApiValidationError> errors)
    {
        errors = [];
        normalized = request;

        if (!Enum.IsDefined(request.Template))
        {
            errors.Add(new ApiValidationError(
                "/template",
                "NOT_ALLOWLISTED",
                "The emergency template is not allowlisted."));
        }

        if (request.IncidentId == Guid.Empty)
        {
            errors.Add(new ApiValidationError(
                "/incidentId",
                "INVALID",
                "A non-empty incident identifier is required."));
        }

        ValidateReason(request.ReasonCode, request.WrittenReason, errors);

        string scopeType = request.Scope?.Type?.Trim().ToUpperInvariant() ?? string.Empty;
        string? scopeId = request.Scope?.Id?.Trim();
        if (!TryNormalizeScope(scopeType, scopeId, out string? normalizedScopeId))
        {
            errors.Add(new ApiValidationError(
                "/scope",
                "INVALID",
                "The scope must use an allowlisted type and bounded identifier."));
        }

        if (Enum.IsDefined(request.Template)
            && !AllowedScopes(request.Template).Contains(scopeType, StringComparer.Ordinal))
        {
            errors.Add(new ApiValidationError(
                "/scope/type",
                "NOT_ALLOWED_FOR_TEMPLATE",
                "The scope type is not permitted for this restrictive template."));
        }

        string? exactDigest = string.IsNullOrWhiteSpace(request.ExactDigest)
            ? null
            : request.ExactDigest.Trim().ToLowerInvariant();
        if (request.Template == EmergencyTemplate.QuarantineExactGatewayDigest)
        {
            if (!IsSha256(exactDigest))
            {
                errors.Add(new ApiValidationError(
                    "/exactDigest",
                    "INVALID",
                    "Gateway quarantine requires one exact hexadecimal SHA-256 digest."));
            }
        }
        else if (exactDigest is not null)
        {
            errors.Add(new ApiValidationError(
                "/exactDigest",
                "NOT_ALLOWED",
                "An artifact digest is accepted only by the exact gateway quarantine template."));
        }

        if (errors.Count != 0)
        {
            return false;
        }

        normalized = new RestrictiveCommandInput(
            request.Template,
            new ScopeInput(scopeType, normalizedScopeId),
            request.IncidentId,
            exactDigest,
            request.ReasonCode.Trim(),
            request.WrittenReason.Trim());
        return true;
    }

    private static void ValidateReason(
        string? reasonCode,
        string? writtenReason,
        List<ApiValidationError> errors)
    {
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
    }

    private static bool TryNormalizeScope(
        string type,
        string? id,
        out string? normalizedId)
    {
        normalizedId = null;
        switch (type)
        {
            case "GLOBAL":
                return string.IsNullOrWhiteSpace(id);
            case "REGION":
                if (!string.IsNullOrWhiteSpace(id) && RegionPattern().IsMatch(id))
                {
                    normalizedId = id.ToLowerInvariant();
                    return true;
                }

                return false;
            case "BROKER":
            case "TENANT":
            case "BROKER_ACCOUNT":
            case "DEPLOYMENT":
            case "WORKER":
                if (Guid.TryParse(id, out Guid resourceId) && resourceId != Guid.Empty)
                {
                    normalizedId = resourceId.ToString("D");
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static IReadOnlyList<string> AllowedScopes(EmergencyTemplate template) => template switch
    {
        EmergencyTemplate.BlockNewExposure =>
            ["GLOBAL", "REGION", "BROKER", "TENANT", "BROKER_ACCOUNT", "DEPLOYMENT"],
        EmergencyTemplate.BlockNewDeployments => ["GLOBAL", "REGION", "BROKER", "TENANT"],
        EmergencyTemplate.CloseOnly =>
            ["REGION", "BROKER", "TENANT", "BROKER_ACCOUNT", "DEPLOYMENT"],
        EmergencyTemplate.QuarantineExactGatewayDigest => ["GLOBAL", "REGION"],
        EmergencyTemplate.RevokeCloudWorker => ["WORKER"],
        _ => []
    };

    private static AdminActor ToEmergencyActor(ClaimsPrincipal principal)
    {
        FrozenSet<string> permissions = principal.FindAll("permission")
            .Select(claim => claim.Value)
            .Append("EMERGENCY_RESTRICT")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToFrozenSet(StringComparer.Ordinal);

        return new AdminActor(
            ClaimReader.RequiredGuid(principal, "tenant_id"),
            ClaimReader.RequiredGuid(principal, "sub"),
            ClaimReader.RequiredGuid(principal, "session_id"),
            ClaimReader.Required(principal, "environment"),
            AuthenticationAssurance.PhishingResistant,
            string.Equals(
                principal.FindFirstValue("managed_device"),
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
            throw new UnauthorizedAccessException("The emergency authentication time is invalid.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new UnauthorizedAccessException(
                "The emergency authentication time is invalid.",
                exception);
        }
    }

    private static IResult ValidationProblem(
        HttpContext context,
        IReadOnlyList<ApiValidationError> errors) => ApiProblems.Create(
            context,
            StatusCodes.Status400BadRequest,
            "VALIDATION_FAILED",
            "The restrictive command failed validation.",
            errors);

    private static IResult NotFound(HttpContext context) => ApiProblems.Create(
        context,
        StatusCodes.Status404NotFound,
        "RESOURCE_NOT_FOUND",
        "The resource was not found.");

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    [GeneratedRegex("^[A-Z][A-Z0-9_.:-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonCodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();
}
