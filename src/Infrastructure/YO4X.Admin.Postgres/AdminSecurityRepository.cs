using Npgsql;
using NpgsqlTypes;
using YO4X.Admin.Application;
using YO4X.Authorization;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal static class AdminSecurityRepository
{
    private const string SessionSql = """
        with authority_time as materialized
        (
            select clock_timestamp() as authorization_now
        )
        select
            session.managed_device,
            session.mfa_level,
            session.assurance_method,
            session.authenticated_at,
            session.step_up_at,
            authority_time.authorization_now
        from identity.admin_sessions as session
        cross join authority_time
        join identity.admin_identities as identity
          on identity.tenant_id = session.tenant_id
         and identity.id = session.admin_identity_id
        where session.tenant_id = @tenant_id
          and session.id = @session_id
          and session.admin_identity_id = @actor_id
          and session.state = 'active'
          and session.revoked_at is null
          and session.expires_at > authority_time.authorization_now
          and identity.state = 'active'
        for share of session, identity
        """;

    private const string GrantsSql = """
        select
            assignment.id,
            permission.permission_key,
            assignment.environment,
            assignment.scope_type,
            assignment.scope_id
        from "authorization".role_assignments as assignment
        join "authorization".roles as role
          on role.tenant_id = assignment.tenant_id
         and role.id = assignment.role_id
        join "authorization".role_permissions as role_permission
          on role_permission.tenant_id = role.tenant_id
         and role_permission.role_id = role.id
        join "authorization".permissions as permission
          on permission.id = role_permission.permission_id
        where assignment.tenant_id = @tenant_id
          and assignment.admin_identity_id = @actor_id
          and assignment.environment = @environment
          and assignment.state = 'active'
          and assignment.starts_at <= @authorization_now
          and assignment.expires_at > @authorization_now
          and assignment.approved_by is not null
          and assignment.approved_by <> assignment.requested_by
          and assignment.revoked_at is null
          and role.state = 'active'
          and (cardinality(role.environment_restrictions) = 0
               or @environment = any(role.environment_restrictions))
          and role_permission.revoked_at is null
        order by permission.permission_key, assignment.id
        """;

    public static async Task<AdminSecuritySnapshot> LoadAsync(
        TenantPostgresTransaction transaction,
        AdminActor actor,
        TimeSpan maximumAuthenticationAge,
        TimeSpan maximumClockSkew,
        CancellationToken cancellationToken)
    {
        ValidateActorBinding(transaction, actor);
        string environment = AdminStorageValues.NormalizeEnvironment(actor.Environment);

        (AdminSessionEvidence session, DateTimeOffset authorizationNow) = await LoadSessionAsync(
            transaction,
            actor,
            cancellationToken).ConfigureAwait(false);
        ValidateAssurance(
            actor,
            session,
            authorizationNow,
            maximumAuthenticationAge,
            maximumClockSkew);

        IReadOnlyList<AdminGrant> grants = await LoadGrantsAsync(
            transaction,
            actor,
            environment,
            authorizationNow,
            cancellationToken).ConfigureAwait(false);
        return new AdminSecuritySnapshot(actor, environment, session, grants, authorizationNow);
    }

    private static async Task<(AdminSessionEvidence Session, DateTimeOffset AuthorizationNow)> LoadSessionAsync(
        TenantPostgresTransaction transaction,
        AdminActor actor,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(SessionSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, actor.SessionId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, actor.ActorId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("The admin session is no longer active.");
        }

        bool managedDevice = reader.GetBoolean(0);
        string mfaLevel = reader.GetString(1);
        string assuranceMethod = reader.GetString(2);
        DateTimeOffset authenticatedAt = reader.GetFieldValue<DateTimeOffset>(3);
        DateTimeOffset stepUpAt = reader.GetFieldValue<DateTimeOffset>(4);
        if (!string.Equals(mfaLevel, "phishing_resistant", StringComparison.Ordinal)
            || assuranceMethod is not ("webauthn" or "hardware_key")
            || !managedDevice
            || authenticatedAt != stepUpAt)
        {
            throw new AdminAuthorizationDeniedException(
                "ADMIN_SESSION_ASSURANCE_INVALID",
                "The active admin session lacks authoritative phishing-resistant managed-device assurance.");
        }

        DateTimeOffset authorizationNow = reader.GetFieldValue<DateTimeOffset>(5);
        return (new AdminSessionEvidence(assuranceMethod, managedDevice, authenticatedAt), authorizationNow);
    }

    private static async Task<IReadOnlyList<AdminGrant>> LoadGrantsAsync(
        TenantPostgresTransaction transaction,
        AdminActor actor,
        string environment,
        DateTimeOffset authorizationNow,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(GrantsSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, actor.ActorId);
        command.Parameters.AddWithValue("environment", NpgsqlDbType.Text, environment);
        command.Parameters.AddWithValue(
            "authorization_now",
            NpgsqlDbType.TimestampTz,
            authorizationNow);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var grants = new List<AdminGrant>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            grants.Add(new AdminGrant(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return grants.AsReadOnly();
    }

    private static void ValidateActorBinding(
        TenantPostgresTransaction transaction,
        AdminActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.TenantId == Guid.Empty
            || actor.ActorId == Guid.Empty
            || actor.SessionId == Guid.Empty
            || transaction.Context.TenantId != actor.TenantId
            || transaction.Context.ActorId != actor.ActorId
            || transaction.Context.SessionId != actor.SessionId)
        {
            throw new UnauthorizedAccessException("The admin identity context is invalid.");
        }
    }

    private static void ValidateAssurance(
        AdminActor actor,
        AdminSessionEvidence session,
        DateTimeOffset now,
        TimeSpan maximumAuthenticationAge,
        TimeSpan maximumClockSkew)
    {
        if (actor.Assurance != AuthenticationAssurance.PhishingResistant
            || !actor.ManagedDevice)
        {
            throw new AdminAuthorizationDeniedException(
                "ADMIN_ASSURANCE_REQUIRED",
                "Phishing-resistant authentication from a managed device is required.");
        }

        if (actor.AuthenticatedAt.ToUnixTimeSeconds() != session.AuthenticatedAt.ToUnixTimeSeconds())
        {
            throw new UnauthorizedAccessException(
                "The admin authentication claim is stale relative to the active server session.");
        }

        if (session.AuthenticatedAt > now + maximumClockSkew
            || now - session.AuthenticatedAt > maximumAuthenticationAge)
        {
            throw new AdminAuthorizationDeniedException(
                "STEP_UP_AUTHENTICATION_REQUIRED",
                "A fresh phishing-resistant authentication is required for this operation.");
        }
    }
}
