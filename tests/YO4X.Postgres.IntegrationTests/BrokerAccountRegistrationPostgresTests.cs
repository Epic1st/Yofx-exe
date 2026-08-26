using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Identity;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class PostgresFoundationTests
{
    [PostgresFact]
    public async Task PendingDemoBrokerAccountRegistrationIsNormalizedAndIdempotent()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        BrokerRegistrationFixture fixture = await SeedBrokerRegistrationFixtureAsync(database);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = new PolicySignatureTrustStore(
            new Dictionary<string, byte[]>
            {
                ["broker-registration-test"] = policyKey.ExportSubjectPublicKeyInfo()
            });
        var options = new ControlPlanePostgresOptions
        {
            ApprovedGatewayDigest = new string('a', 64),
            ApprovedRegion = "integration-region",
            ApprovedBrokerServer = fixture.Server,
            ApprovedBrokerProfileId = fixture.BrokerProfileId,
            ApprovedRuntimeImageDigest = $"sha256:{new string('b', 64)}",
            SecretIngestionOrigin = new Uri("https://ingestion.example/"),
            ApprovedCredentialClientOrigin = new Uri("https://portal.example/")
        };
        options.Validate();
        var application = new PostgresControlPlaneApplication(
            database.ControlApi,
            options,
            SystemClock.Instance,
            trustStore);
        var request = new CreateBrokerAccount(
            fixture.BrokerProfileId,
            $"  {fixture.Server}  ",
            "  ****1234  ",
            new string('C', 64),
            BrokerAccountEnvironment.Demo);
        var metadata = new RequestMetadata(
            new string('1', 32),
            fixture.ActorCorrelationId,
            null,
            "Register an approved demo binding.");

        BrokerAccountView created = await application.CreateBrokerAccountAsync(
            fixture.Actor,
            request,
            metadata,
            CancellationToken.None);
        BrokerAccountView replayed = await application.CreateBrokerAccountAsync(
            fixture.Actor,
            request,
            metadata,
            CancellationToken.None);

        Assert.Equal(created, replayed);
        Assert.Equal(fixture.BrokerId, created.BrokerId);
        Assert.Equal(fixture.Server, created.Server);
        Assert.Equal("****1234", created.MaskedLogin);
        Assert.Equal(BrokerAccountEnvironment.Demo, created.Environment);
        Assert.Null(created.AccountMode);
        Assert.Equal("UNKNOWN", created.CapabilityState);
        Assert.Equal(0, created.Version);

        await using NpgsqlConnection verification = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                count(*),
                bool_and(broker_profile_id = @broker_profile_id),
                bool_and(server = @server),
                bool_and(masked_login = '****1234'),
                bool_and(binding_fingerprint = repeat('c', 64)),
                bool_and(environment = 'demo'),
                bool_and(credential_reference is null),
                bool_and(credential_state = 'absent'),
                bool_and(state = 'pending'),
                bool_and(account_mode is null),
                bool_and(capability_evidence_sha256 is null),
                bool_and(row_version = 0)
            from operations.broker_accounts
            where tenant_id = @tenant_id
              and user_id = @user_id
            """,
            verification);
        command.Parameters.AddWithValue("broker_profile_id", NpgsqlDbType.Uuid, fixture.BrokerProfileId);
        command.Parameters.AddWithValue("server", NpgsqlDbType.Text, fixture.Server);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.Actor.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, fixture.Actor.UserId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        for (int index = 1; index < reader.FieldCount; index++)
        {
            Assert.True(reader.GetBoolean(index));
        }
        Assert.False(await reader.ReadAsync());

        ResourceConflictException changedRequest =
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                application.CreateBrokerAccountAsync(
                    fixture.Actor,
                    request with { MaskedLogin = "*****4321" },
                    metadata,
                    CancellationToken.None));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", changedRequest.Code);
    }

    [PostgresFact]
    public async Task PendingBrokerAccountRegistrationRejectsLiveUnapprovedAndUnmaskedInputs()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        BrokerRegistrationFixture fixture = await SeedBrokerRegistrationFixtureAsync(database);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = new PolicySignatureTrustStore(
            new Dictionary<string, byte[]>
            {
                ["broker-registration-negative-test"] = policyKey.ExportSubjectPublicKeyInfo()
            });
        var options = new ControlPlanePostgresOptions
        {
            ApprovedGatewayDigest = new string('a', 64),
            ApprovedRegion = "integration-region",
            ApprovedBrokerServer = fixture.Server,
            ApprovedBrokerProfileId = fixture.BrokerProfileId,
            ApprovedRuntimeImageDigest = $"sha256:{new string('b', 64)}",
            SecretIngestionOrigin = new Uri("https://ingestion.example/"),
            ApprovedCredentialClientOrigin = new Uri("https://portal.example/")
        };
        options.Validate();
        var application = new PostgresControlPlaneApplication(
            database.ControlApi,
            options,
            SystemClock.Instance,
            trustStore);
        var baseline = new CreateBrokerAccount(
            fixture.BrokerProfileId,
            fixture.Server,
            "****1234",
            new string('d', 64),
            BrokerAccountEnvironment.Demo);

        DomainException live = await Assert.ThrowsAsync<DomainException>(() =>
            application.CreateBrokerAccountAsync(
                fixture.Actor,
                baseline with { Environment = BrokerAccountEnvironment.Live },
                Metadata('2', fixture.ActorCorrelationId),
                CancellationToken.None));
        Assert.Equal("BROKER_ACCOUNT_REGISTRATION_INVALID", live.Code);

        DomainException rawLogin = await Assert.ThrowsAsync<DomainException>(() =>
            application.CreateBrokerAccountAsync(
                fixture.Actor,
                baseline with { MaskedLogin = "12345678" },
                Metadata('3', fixture.ActorCorrelationId),
                CancellationToken.None));
        Assert.Equal("BROKER_ACCOUNT_REGISTRATION_INVALID", rawLogin.Code);

        AuthorizationDeniedException unapproved =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(() =>
                application.CreateBrokerAccountAsync(
                    fixture.Actor,
                    baseline with { BrokerProfileId = Guid.CreateVersion7() },
                    Metadata('4', fixture.ActorCorrelationId),
                    CancellationToken.None));
        Assert.Equal("BROKER_PROFILE_NOT_APPROVED", unapproved.Code);

        await SetBrokerRegistrationUserUnverifiedAsync(database, fixture.Actor);
        AuthorizationDeniedException noOracle =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(() =>
                application.CreateBrokerAccountAsync(
                    fixture.Actor,
                    baseline with
                    {
                        BrokerProfileId = Guid.CreateVersion7(),
                        Server = "Unapproved-Demo"
                    },
                    Metadata('5', fixture.ActorCorrelationId),
                    CancellationToken.None));
        Assert.Equal("EMAIL_VERIFICATION_REQUIRED", noOracle.Code);
    }

    [PostgresFact]
    public async Task PendingDemoBrokerAccountDatabaseGuardFailsClosedWithoutPolicyOrActorOracles()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        BrokerRegistrationFixture fixture = await SeedBrokerRegistrationFixtureAsync(database);
        var context = new TenantExecutionContext(
            fixture.Actor.TenantId,
            fixture.Actor.UserId,
            fixture.ActorCorrelationId,
            fixture.Actor.SessionId);
        var valid = new DirectBrokerRegistration(
            Guid.CreateVersion7(),
            fixture.Actor.TenantId,
            fixture.Actor.UserId,
            fixture.BrokerId,
            fixture.BrokerProfileId,
            fixture.Server,
            "****1234",
            new string('6', 64),
            "demo");
        (string Name, DirectBrokerRegistration Attempt)[] rejectedAttempts =
        [
            ("tenant", valid with { TenantId = Guid.CreateVersion7() }),
            ("actor", valid with { UserId = Guid.CreateVersion7() }),
            ("broker", valid with { BrokerId = Guid.CreateVersion7() }),
            ("profile", valid with { BrokerProfileId = Guid.CreateVersion7() }),
            ("server", valid with { Server = "Unapproved-Demo" }),
            ("raw-login", valid with { MaskedLogin = "12345678" }),
            ("live", valid with { Environment = "live" })
        ];

        foreach ((string name, DirectBrokerRegistration attempt) in rejectedAttempts)
        {
            PostgresException rejected = await ExecuteDirectRegistrationRejectedAsync(
                database.ControlApi,
                context,
                attempt);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
            Assert.True(
                string.Equals(
                    "Pending demo broker-account registration is not authorized.",
                    rejected.MessageText,
                    StringComparison.Ordinal),
                $"{name}: {rejected.MessageText}");
        }

        PostgresException missingContext = await ExecuteDirectRegistrationWithoutContextRejectedAsync(
            database.ControlApi,
            valid);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, missingContext.SqlState);
        Assert.Equal(
            "A tenant context is required for U0 authority locking.",
            missingContext.MessageText);

        await using TenantPostgresTransaction credentialAttempt =
            await database.ControlApi.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand forbiddenCredential = credentialAttempt.CreateCommand(
            """
            insert into operations.broker_accounts
            (
                id, tenant_id, user_id, broker_id, broker_profile_id,
                server, masked_login, binding_fingerprint, environment,
                credential_reference
            )
            values
            (
                @id, @tenant_id, @user_id, @broker_id, @broker_profile_id,
                @server, @masked_login, @binding_fingerprint, @environment,
                'forbidden-reference'
            )
            """);
        AddDirectRegistrationParameters(forbiddenCredential, valid);
        PostgresException credentialRejected = await Assert.ThrowsAsync<PostgresException>(
            () => forbiddenCredential.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, credentialRejected.SqlState);
    }

    private static RequestMetadata Metadata(char value, Guid correlationId) =>
        new(new string(value, 32), correlationId, null);

    private const string DirectRegistrationSql =
        """
        insert into operations.broker_accounts
        (
            id, tenant_id, user_id, broker_id, broker_profile_id,
            server, masked_login, binding_fingerprint, environment
        )
        values
        (
            @id, @tenant_id, @user_id, @broker_id, @broker_profile_id,
            @server, @masked_login, @binding_fingerprint, @environment
        )
        """;

    private static async Task<PostgresException> ExecuteDirectRegistrationRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        DirectBrokerRegistration attempt)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(DirectRegistrationSql);
        AddDirectRegistrationParameters(command, attempt);
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<PostgresException> ExecuteDirectRegistrationWithoutContextRejectedAsync(
        PostgresDatabase database,
        DirectBrokerRegistration attempt)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(DirectRegistrationSql, connection);
        AddDirectRegistrationParameters(command, attempt);
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private static void AddDirectRegistrationParameters(
        NpgsqlCommand command,
        DirectBrokerRegistration attempt)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, attempt.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, attempt.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, attempt.UserId);
        command.Parameters.AddWithValue("broker_id", NpgsqlDbType.Uuid, attempt.BrokerId);
        command.Parameters.AddWithValue(
            "broker_profile_id",
            NpgsqlDbType.Uuid,
            attempt.BrokerProfileId);
        command.Parameters.AddWithValue("server", NpgsqlDbType.Text, attempt.Server);
        command.Parameters.AddWithValue("masked_login", NpgsqlDbType.Text, attempt.MaskedLogin);
        command.Parameters.AddWithValue(
            "binding_fingerprint",
            NpgsqlDbType.Text,
            attempt.BindingFingerprint);
        command.Parameters.AddWithValue("environment", NpgsqlDbType.Text, attempt.Environment);
    }

    private static async Task SetBrokerRegistrationUserUnverifiedAsync(
        PostgresTestDatabase database,
        UserActor actor)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (var fixtureMode = new NpgsqlCommand(
            "set local session_replication_role = replica",
            connection,
            transaction))
        {
            await fixtureMode.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update identity.user_identities
            set email_verified_at = null
            where tenant_id = @tenant_id and id = @user_id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task<BrokerRegistrationFixture> SeedBrokerRegistrationFixtureAsync(
        PostgresTestDatabase database)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
        Guid brokerId = Guid.CreateVersion7();
        Guid brokerProfileId = Guid.CreateVersion7();
        Guid correlationId = Guid.CreateVersion7();
        const string server = "Broker-Demo";

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (var fixtureMode = new NpgsqlCommand(
            "set local session_replication_role = replica",
            connection,
            transaction))
        {
            await fixtureMode.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@tenant_id, @tenant_slug, 'Broker registration tenant');

            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state, email_verified_at)
            values
                (@user_id, @tenant_id, @email, 'active', statement_timestamp());

            insert into identity.user_session_families
                (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
            values
                (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                 'active', statement_timestamp() + interval '1 hour');

            insert into governance.broker_profiles
                (id, broker_id, profile_version, broker_company, server_name,
                 environment_support, capabilities, evidence_sha256, tested_at, state)
            values
                (@broker_profile_id, @broker_id, 1, 'Broker', @server,
                 array['demo'], '{}'::jsonb, repeat('e', 64),
                 statement_timestamp(), 'approved');
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("tenant_slug", NpgsqlDbType.Text, $"broker-{tenantId:N}");
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"broker-{userId:N}@example.test");
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
        command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('f', 64));
        command.Parameters.AddWithValue("broker_id", NpgsqlDbType.Uuid, brokerId);
        command.Parameters.AddWithValue("broker_profile_id", NpgsqlDbType.Uuid, brokerProfileId);
        command.Parameters.AddWithValue("server", NpgsqlDbType.Text, server);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return new BrokerRegistrationFixture(
            new UserActor(
                tenantId,
                userId,
                sessionId,
                AuthenticationAssurance.Totp),
            correlationId,
            brokerId,
            brokerProfileId,
            server);
    }

    private sealed record BrokerRegistrationFixture(
        UserActor Actor,
        Guid ActorCorrelationId,
        Guid BrokerId,
        Guid BrokerProfileId,
        string Server);

    private sealed record DirectBrokerRegistration(
        Guid Id,
        Guid TenantId,
        Guid UserId,
        Guid BrokerId,
        Guid BrokerProfileId,
        string Server,
        string MaskedLogin,
        string BindingFingerprint,
        string Environment);
}
