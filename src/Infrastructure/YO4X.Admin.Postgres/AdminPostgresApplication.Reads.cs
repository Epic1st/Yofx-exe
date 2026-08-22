using YO4X.Admin.Application;
using YO4X.Approvals;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Persistence.Postgres;
using YO4X.ReadModels;

namespace YO4X.Admin.Postgres;

public sealed partial class AdminPostgresApplication
{
    private static readonly string[] DeploymentAuditFields =
    [
        "desired_state",
        "component_states",
        "broker_reconciliation_state",
        "fence_generation",
        "source_version"
    ];

    public async Task<AdminMeView> GetMeAsync(
        AdminActor actor,
        CancellationToken cancellationToken)
    {
        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.ReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadOwnSession);

        var view = new AdminMeView(
            actor.ActorId,
            actor.SessionId,
            context.Security.Environment,
            context.Security.EffectivePermissions,
            context.Security.Session.AuthenticatedAt);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<IReadOnlyList<ApprovalSummary>> GetApprovalsAsync(
        AdminActor actor,
        int limit,
        Guid? before,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 || limit > options.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Page size must be between 1 and {options.MaximumPageSize}.");
        }

        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.ReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadApprovals);

        var result = new List<ApprovalSummary>(limit);
        Guid? cursor = before;
        int batchSize = Math.Min(options.MaximumPageSize, Math.Max(limit * 2, 32));
        while (result.Count < limit)
        {
            IReadOnlyList<ApprovalRecord> page = await AdminReadRepository.GetApprovalPageAsync(
                context.Transaction,
                batchSize,
                cursor,
                cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            foreach (ApprovalRecord approval in page)
            {
                AdminResourceScope scope = await ResolveCommandScopeAsync(
                    context.Transaction,
                    approval.Command,
                    cancellationToken).ConfigureAwait(false);
                if (context.Security.CanAccess(AdminPermissions.ReadApprovals, scope))
                {
                    result.Add(approval.ToSummary(context.Now));
                    if (result.Count == limit)
                    {
                        break;
                    }
                }
            }

            cursor = page[^1].Id;
            if (page.Count < batchSize)
            {
                break;
            }
        }

        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result.AsReadOnly();
    }

    public async Task<ApprovalSummary?> GetApprovalAsync(
        AdminActor actor,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(approvalId, nameof(approvalId));
        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.ReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadApprovals);
        ApprovalRecord? approval = await AdminReadRepository.GetApprovalAsync(
            context.Transaction,
            approvalId,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        if (approval is null)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        AdminResourceScope scope = await ResolveCommandScopeAsync(
            context.Transaction,
            approval.Command,
            cancellationToken).ConfigureAwait(false);
        if (!context.Security.CanAccess(AdminPermissions.ReadApprovals, scope))
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        ApprovalSummary summary = approval.ToSummary(context.Now);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return summary;
    }

    public async Task<CommandSummary?> GetCommandAsync(
        AdminActor actor,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(commandId, nameof(commandId));
        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.ReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadCommands);
        AdminCommandRecord? command = await AdminReadRepository.GetCommandAsync(
            context.Transaction,
            commandId,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        if (command is null)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        AdminResourceScope scope = await ResolveCommandScopeAsync(
            context.Transaction,
            command,
            cancellationToken).ConfigureAwait(false);
        if (!context.Security.CanAccess(AdminPermissions.ReadCommands, scope))
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        CommandSummary summary = command.ToSummary();
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return summary;
    }

    public async Task<IReadOnlyList<CommandTargetView>> GetCommandTargetsAsync(
        AdminActor actor,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(commandId, nameof(commandId));
        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.ReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadCommands);
        AdminCommandRecord? command = await AdminReadRepository.GetCommandAsync(
            context.Transaction,
            commandId,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        if (command is null)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Array.Empty<CommandTargetView>();
        }

        AdminResourceScope scope = await ResolveCommandScopeAsync(
            context.Transaction,
            command,
            cancellationToken).ConfigureAwait(false);
        if (!context.Security.CanAccess(AdminPermissions.ReadCommands, scope))
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Array.Empty<CommandTargetView>();
        }

        IReadOnlyList<CommandTargetView> targets = await AdminReadRepository.GetTargetsAsync(
            context.Transaction,
            commandId,
            cancellationToken).ConfigureAwait(false);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return targets;
    }

    public async Task<DeploymentOperationsView?> GetDeploymentAsync(
        AdminActor actor,
        Guid deploymentId,
        string purpose,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(deploymentId, nameof(deploymentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        await using AdminOperationContext context = await BeginAsync(
            actor,
            Identifiers.NewId(),
            options.SensitiveReadAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.ReadDeployments);
        DeploymentResource? deployment = await AdminReadRepository.GetDeploymentAsync(
            context.Transaction,
            deploymentId,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        if (deployment is null
            || !context.Security.CanAccess(AdminPermissions.ReadDeployments, deployment.ToScope()))
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var view = AdminReadRepository.ToView(deployment);
        AuditEvent audit = AuditEvent.Create(
            actor.TenantId,
            actor.ActorId,
            AuditCategory.SensitiveRead,
            "admin.deployment.read",
            "deployment",
            deployment.Id.ToString("D"),
            AuditOutcome.Succeeded,
            "PURPOSE_RECORDED",
            context.Transaction.Context.CorrelationId,
            causationId: null,
            new
            {
                PurposeSha256 = CanonicalJson.Sha256(purpose.Trim()),
                Fields = DeploymentAuditFields
            },
            context.Now);
        await PostgresAuditOutboxWriter.AppendAuditAsync(
            context.Transaction,
            audit,
            cancellationToken).ConfigureAwait(false);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return view;
    }

    private static async Task<AdminResourceScope> ResolveCommandScopeAsync(
        TenantPostgresTransaction transaction,
        AdminCommandRecord command,
        CancellationToken cancellationToken)
    {
        if (command.ScopeType == "deployment"
            && Guid.TryParse(command.ScopeId, out Guid deploymentId))
        {
            DeploymentResource? deployment = await AdminReadRepository.GetDeploymentAsync(
                transaction,
                deploymentId,
                forUpdate: false,
                cancellationToken).ConfigureAwait(false);
            if (deployment is not null
                && string.Equals(
                    deployment.Environment,
                    command.Environment,
                    StringComparison.Ordinal))
            {
                return deployment.ToScope();
            }
        }

        return AdminResourceScope.ForCommand(command);
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}
