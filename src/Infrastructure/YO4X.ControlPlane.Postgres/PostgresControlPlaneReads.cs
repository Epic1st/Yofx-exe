using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BrokerAccounts;
using YO4X.ControlPlane.Application;
using YO4X.Deployments;
using YO4X.Identity;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    private static readonly HashSet<string> ActivityDetailAllowlist = new(StringComparer.Ordinal)
    {
        "operationId",
        "deploymentId",
        "brokerAccountId",
        "strategyVersionId",
        "riskPolicyVersionId",
        "gatewayArtifactId",
        "configurationSha256",
        "desiredState",
        "operationType",
        "submittedVersion"
    };

    public async Task<UserView?> GetMeAsync(UserActor actor, CancellationToken cancellationToken)
    {
        (var transaction, AuthorizedUser user) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new UserView(
                actor.UserId,
                MaskEmail(user.NormalizedEmail),
                user.EmailVerified,
                UserSecurityState.Active,
                actor.Assurance);
        }
    }

    public async Task<IReadOnlyList<SessionView>> GetSessionsAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                with authority_time as materialized
                (
                    select clock_timestamp() as authority_now
                )
                select session.id, session.device_id,
                       case
                           when session.state = 'active'
                                and session.expires_at <= authority_time.authority_now
                               then 'expired'
                           else session.state
                       end,
                       session.created_at, session.expires_at, session.revoked_at
                from identity.user_session_families as session
                cross join authority_time
                where session.tenant_id = @tenant_id
                  and session.user_id = @user_id
                order by session.created_at desc, session.id desc
                limit 100
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);

            var sessions = new List<SessionView>();
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    DateTimeOffset expiresAt = reader.GetFieldValue<DateTimeOffset>(4);
                    SessionState state = ParseSessionState(reader.GetString(2));

                    sessions.Add(new SessionView(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        state,
                        reader.GetFieldValue<DateTimeOffset>(3),
                        expiresAt,
                        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                        reader.GetGuid(0) == actor.SessionId));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return sessions;
        }
    }

    public async Task<BrokerAccountView?> GetBrokerAccountAsync(
        UserActor actor,
        Guid brokerAccountId,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                with authority_time as materialized
                (
                    select clock_timestamp() as authority_now
                )
                select
                    account.id,
                    account.broker_id,
                    account.server,
                    account.masked_login,
                    account.environment,
                    account.account_mode,
                    case
                        when account.capability_valid_until is null then 'UNKNOWN'
                        when account.capability_valid_until <= authority_time.authority_now then 'STALE'
                        else 'CURRENT'
                    end,
                    account.row_version,
                    account.updated_at
                from operations.broker_accounts as account
                cross join authority_time
                where account.tenant_id = @tenant_id
                  and account.user_id = @user_id
                  and account.id = @account_id
                  and account.state <> 'deleted'
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddUuid(command, "account_id", brokerAccountId);

            BrokerAccountView? view;
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                view = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? new BrokerAccountView(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        ParseBrokerEnvironment(reader.GetString(4)),
                        reader.IsDBNull(5) ? null : ParseBrokerMode(reader.GetString(5)),
                        reader.GetString(6),
                        reader.GetInt64(7),
                        reader.GetFieldValue<DateTimeOffset>(8))
                    : null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    public async Task<CredentialStateView?> GetCredentialStateAsync(
        UserActor actor,
        Guid brokerAccountId,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    account.credential_state in ('ready', 'disabled', 'rotation_pending', 'deletion_pending'),
                    account.credential_state,
                    metadata.last_authorized_worker_use_at,
                    account.masked_login
                from operations.broker_accounts as account
                left join readmodel.secret_metadata as metadata
                  on metadata.tenant_id = account.tenant_id
                 and metadata.broker_account_id = account.id
                where account.tenant_id = @tenant_id
                  and account.user_id = @user_id
                  and account.id = @account_id
                  and account.state <> 'deleted'
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddUuid(command, "account_id", brokerAccountId);

            CredentialStateView? view;
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                view = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? new CredentialStateView(
                        reader.GetBoolean(0),
                        ParseCredentialState(reader.GetString(1)),
                        reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                        reader.GetString(3))
                    : null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    public async Task<DeploymentView?> GetDeploymentAsync(
        UserActor actor,
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            DeploymentView? view = await ReadDeploymentAsync(
                transaction,
                actor,
                deploymentId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    public async Task<UserOperationView?> GetOperationAsync(
        UserActor actor,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    id, operation_type, target_type, target_id, state,
                    last_error_code, row_version, created_at, updated_at, completed_at
                from control.user_operations
                where tenant_id = @tenant_id
                  and user_id = @user_id
                  and id = @operation_id
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddUuid(command, "operation_id", operationId);
            UserOperationView? view;
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                view = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? new UserOperationView(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetGuid(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetInt64(6),
                        reader.GetFieldValue<DateTimeOffset>(7),
                        reader.GetFieldValue<DateTimeOffset>(8),
                        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9))
                    : null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    public async Task<IReadOnlyList<ActivityView>> GetDeploymentActivityAsync(
        UserActor actor,
        Guid deploymentId,
        int limit,
        Guid? before,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand ownership = transaction.CreateCommand(
                """
                select 1
                from operations.deployments
                where tenant_id = @tenant_id and user_id = @user_id and id = @deployment_id
                """);
            AddUuid(ownership, "tenant_id", actor.TenantId);
            AddUuid(ownership, "user_id", actor.UserId);
            AddUuid(ownership, "deployment_id", deploymentId);
            if (await ownership.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Array.Empty<ActivityView>();
            }

            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                with cursor as
                (
                    select occurred_at, id
                    from audit.audit_events
                    where tenant_id = @tenant_id
                      and target_type = 'deployment'
                      and target_id = @deployment_id
                      and id = @before
                )
                select event.id, event.category, event.outcome, event.action, event.payload::text, event.occurred_at
                from audit.audit_events as event
                where event.tenant_id = @tenant_id
                  and event.target_type = 'deployment'
                  and event.target_id = @deployment_id
                  and
                  (
                      @before is null
                      or (event.occurred_at, event.id) <
                         (select cursor.occurred_at, cursor.id from cursor)
                  )
                order by event.occurred_at desc, event.id desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Text, deploymentId.ToString("D"));
            command.Parameters.AddWithValue("before", NpgsqlDbType.Uuid, before is null ? DBNull.Value : before.Value);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

            var activity = new List<ActivityView>(limit);
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    activity.Add(new ActivityView(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        ReadSafeDetails(reader.GetString(4)),
                        reader.GetFieldValue<DateTimeOffset>(5)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return activity;
        }
    }

    private static async Task<DeploymentView?> ReadDeploymentAsync(
        YO4X.Persistence.Postgres.TenantPostgresTransaction transaction,
        UserActor actor,
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                deployment.id,
                deployment.desired_state,
                deployment.observed_state,
                coalesce(health.reconciliation_state, 'unknown'),
                deployment.fence_generation,
                deployment.row_version,
                deployment.updated_at
            from operations.deployments as deployment
            left join readmodel.deployment_health as health
              on health.tenant_id = deployment.tenant_id
             and health.deployment_id = deployment.id
            where deployment.tenant_id = @tenant_id
              and deployment.user_id = @user_id
              and deployment.id = @deployment_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "user_id", actor.UserId);
        AddUuid(command, "deployment_id", deploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new DeploymentView(
                reader.GetGuid(0),
                DeploymentMode.CloudDemo,
                ParseDeploymentState(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    private static Dictionary<string, string> ReadSafeDetails(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return document.RootElement.EnumerateObject()
            .Where(property => ActivityDetailAllowlist.Contains(property.Name))
            .Where(property => property.Value.ValueKind is
                JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            .Take(20)
            .ToDictionary(
                property => property.Name,
                property => property.Value.ToString(),
                StringComparer.Ordinal);
    }

    private static string MaskEmail(string email)
    {
        int separator = email.IndexOf('@');
        return separator > 0
            ? $"{email[0]}***{email[separator..].ToLowerInvariant()}"
            : "***";
    }

    private static SessionState ParseSessionState(string value) => value switch
    {
        "active" => SessionState.Active,
        "revoked" => SessionState.Revoked,
        "expired" => SessionState.Expired,
        "compromised" => SessionState.Compromised,
        _ => throw new InvalidOperationException("An unknown session state is persisted.")
    };

    private static BrokerAccountEnvironment ParseBrokerEnvironment(string value) => value switch
    {
        "demo" => BrokerAccountEnvironment.Demo,
        "live" => BrokerAccountEnvironment.Live,
        _ => throw new InvalidOperationException("An unknown broker environment is persisted.")
    };

    private static BrokerAccountMode ParseBrokerMode(string value) => value switch
    {
        "hedging" => BrokerAccountMode.Hedging,
        "netting" => BrokerAccountMode.Netting,
        _ => throw new InvalidOperationException("An unknown broker account mode is persisted.")
    };

    private static CloudCredentialState ParseCredentialState(string value) => value switch
    {
        "absent" => CloudCredentialState.Absent,
        "ingestion_pending" => CloudCredentialState.IngestionPending,
        "ready" => CloudCredentialState.Ready,
        "disabled" => CloudCredentialState.Disabled,
        "rotation_pending" => CloudCredentialState.RotationPending,
        "deletion_pending" => CloudCredentialState.DeletionPending,
        "deleted" => CloudCredentialState.Deleted,
        _ => throw new InvalidOperationException("An unknown credential state is persisted.")
    };

    private static DeploymentState ParseDeploymentState(string value) => value switch
    {
        "draft" => DeploymentState.Draft,
        "validating" => DeploymentState.Validating,
        "ready" => DeploymentState.Ready,
        "starting" => DeploymentState.Starting,
        "reconciling" => DeploymentState.Reconciling,
        "running" => DeploymentState.Running,
        "close_only" => DeploymentState.CloseOnly,
        "stop_after_flat" => DeploymentState.StopAfterFlat,
        "stopping" => DeploymentState.Stopping,
        "stopped" => DeploymentState.Stopped,
        "faulted" => DeploymentState.Faulted,
        "fenced" => DeploymentState.Fenced,
        "expired" => DeploymentState.Expired,
        "revoked" => DeploymentState.Revoked,
        _ => throw new InvalidOperationException("An unknown deployment state is persisted.")
    };
}
