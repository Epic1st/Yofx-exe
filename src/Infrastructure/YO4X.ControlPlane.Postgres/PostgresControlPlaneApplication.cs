using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Audit;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication : IControlPlaneApplication
{
    private readonly PostgresDatabase database;
    private readonly ControlPlanePostgresOptions options;
    private readonly PolicySignatureTrustStore policyTrustStore;
    private readonly CredentialIngestionProofIssuer? proofIssuer;
    private readonly StrategyImportProofIssuer? strategyImportProofIssuer;

    public PostgresControlPlaneApplication(
        PostgresDatabase database,
        ControlPlanePostgresOptions options,
        IClock clock,
        PolicySignatureTrustStore policyTrustStore,
        CredentialIngestionProofIssuer? proofIssuer = null,
        StrategyImportProofIssuer? strategyImportProofIssuer = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(clock);
        this.policyTrustStore = policyTrustStore ?? throw new ArgumentNullException(nameof(policyTrustStore));
        this.proofIssuer = proofIssuer;
        this.strategyImportProofIssuer = strategyImportProofIssuer;
    }

    private async ValueTask<(TenantPostgresTransaction Transaction, AuthorizedUser User)> BeginAuthorizedAsync(
        UserActor actor,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        await BeginAuthorizedAsync(actor, correlationId, acquireAuthorityLock: false, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<(TenantPostgresTransaction Transaction, AuthorizedUser User)> BeginMutationAuthorizedAsync(
        UserActor actor,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        await BeginAuthorizedAsync(actor, correlationId, acquireAuthorityLock: true, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<(TenantPostgresTransaction Transaction, AuthorizedUser User)> BeginAuthorizedAsync(
        UserActor actor,
        Guid correlationId,
        bool acquireAuthorityLock,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(actor.Assurance))
        {
            throw new AuthorizationDeniedException(
                "AUTHENTICATION_ASSURANCE_INVALID",
                "The authentication assurance is not accepted.");
        }

        var executionContext = new TenantExecutionContext(
            actor.TenantId,
            actor.UserId,
            correlationId,
            actor.SessionId);
        TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(executionContext, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (acquireAuthorityLock)
            {
                await using NpgsqlCommand authorityLock = transaction.CreateCommand(
                    "select control.acquire_u0_authority_lock()");
                await authorityLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    identity.normalized_email,
                    identity.security_state,
                    identity.email_verified_at,
                    identity.row_version,
                    session.device_id,
                    session.state,
                    tenant.state
                from identity.user_identities as identity
                join identity.tenants as tenant
                  on tenant.id = identity.tenant_id
                join identity.user_session_families as session
                  on session.tenant_id = identity.tenant_id
                 and session.user_id = identity.id
                where identity.tenant_id = @tenant_id
                  and identity.id = @user_id
                  and session.id = @session_id
                  and session.expires_at > clock_timestamp()
                """);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, actor.SessionId);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("The authenticated user session is not active.");
            }

            string securityState = reader.GetString(1);
            string sessionState = reader.GetString(5);
            string tenantState = reader.GetString(6);
            if (!string.Equals(tenantState, "active", StringComparison.Ordinal)
                || !string.Equals(securityState, "active", StringComparison.Ordinal)
                || !string.Equals(sessionState, "active", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("The authenticated user session is not active.");
            }

            var user = new AuthorizedUser(
                reader.GetString(0),
                !reader.IsDBNull(2),
                reader.GetInt64(3),
                reader.GetGuid(4));
            return (transaction, user);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static void AddNullableLong(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Bigint, value is null ? DBNull.Value : value.Value);

    private static void RequireVerifiedUser(AuthorizedUser user)
    {
        if (!user.EmailVerified)
        {
            throw new AuthorizationDeniedException(
                "EMAIL_VERIFICATION_REQUIRED",
                "A verified email is required for this operation.");
        }
    }

    private static void RequireMultiFactorAssurance(UserActor actor)
    {
        if (actor.Assurance == YO4X.Identity.AuthenticationAssurance.Password)
        {
            throw new AuthorizationDeniedException(
                "MULTI_FACTOR_ASSURANCE_REQUIRED",
                "Multi-factor authentication is required for this operation.");
        }
    }

    private static AuditEvidenceContext CreateUserAuditContext(
        UserActor actor,
        AuthorizedUser user,
        RequestMetadata metadata,
        long? resourceVersionBefore = null,
        long? resourceVersionAfter = null,
        string? effectivePolicyDigest = null,
        string? policyVersionWatermark = null,
        string? policyInputSha256 = null) => new(
            actor.SessionId,
            user.DeviceId,
            actor.Assurance switch
            {
                YO4X.Identity.AuthenticationAssurance.Password => "password",
                YO4X.Identity.AuthenticationAssurance.Totp => "totp",
                YO4X.Identity.AuthenticationAssurance.WebAuthn => "webauthn",
                YO4X.Identity.AuthenticationAssurance.HardwareKey => "hardware_key",
                _ => throw new AuthorizationDeniedException(
                    "AUTHENTICATION_ASSURANCE_INVALID",
                    "The authentication assurance is not accepted.")
            },
            metadata.SourceNetworkClass,
            effectivePolicyDigest,
            policyVersionWatermark,
            policyInputSha256,
            resourceVersionBefore,
            resourceVersionAfter);

    private sealed record AuthorizedUser(
        string NormalizedEmail,
        bool EmailVerified,
        long Version,
        Guid DeviceId);
}
