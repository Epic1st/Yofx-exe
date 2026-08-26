using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Identity;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class PostgresFoundationTests
{
    [PostgresFact]
    public async Task BrokerAccountDiscoveryIsActorScopedRedactedAndLimitedToApprovedDemoMetadata()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        BrokerRegistrationFixture fixture = await SeedBrokerRegistrationFixtureAsync(database);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = new PolicySignatureTrustStore(
            new Dictionary<string, byte[]>
            {
                ["broker-discovery-test"] = policyKey.ExportSubjectPublicKeyInfo()
            });
        ControlPlanePostgresOptions options = BrokerDiscoveryOptions(fixture);
        var application = new PostgresControlPlaneApplication(
            database.ControlApi,
            options,
            SystemClock.Instance,
            trustStore);

        BrokerAccountView created = await application.CreateBrokerAccountAsync(
            fixture.Actor,
            new CreateBrokerAccount(
                fixture.BrokerProfileId,
                fixture.Server,
                "******42",
                new string('a', 64),
                BrokerAccountEnvironment.Demo),
            new RequestMetadata(new string('7', 32), fixture.ActorCorrelationId, null),
            CancellationToken.None);

        IReadOnlyList<BrokerAccountView> ownerAccounts = await application.GetBrokerAccountsAsync(
            fixture.Actor,
            CancellationToken.None);
        BrokerAccountView listed = Assert.Single(ownerAccounts);
        Assert.Equal(created, listed);
        Assert.Equal("******42", listed.MaskedLogin);

        UserActor otherActor = await SeedBrokerDiscoveryUserAsync(database, fixture.Actor.TenantId);
        Assert.Empty(await application.GetBrokerAccountsAsync(otherActor, CancellationToken.None));

        BrokerAccountRegistrationOption option = Assert.Single(
            await application.GetBrokerAccountRegistrationOptionsAsync(
                fixture.Actor,
                query: null,
                CancellationToken.None));
        Assert.Equal(fixture.BrokerProfileId, option.BrokerProfileId);
        Assert.Equal(fixture.Server, option.Server);
        Assert.Equal(BrokerAccountEnvironment.Demo, option.Environment);

        UnauthorizedAccessException rejected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            application.GetBrokerAccountRegistrationOptionsAsync(
                fixture.Actor with { SessionId = Guid.CreateVersion7() },
                query: null,
                CancellationToken.None));
        Assert.Equal("The authenticated user session is not active.", rejected.Message);
    }

    [PostgresFact]
    public async Task RegistrationOptionsDisappearWhenConfiguredGovernanceProfileIsNotApproved()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        BrokerRegistrationFixture fixture = await SeedBrokerRegistrationFixtureAsync(database);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = new PolicySignatureTrustStore(
            new Dictionary<string, byte[]>
            {
                ["broker-discovery-revocation-test"] = policyKey.ExportSubjectPublicKeyInfo()
            });
        var application = new PostgresControlPlaneApplication(
            database.ControlApi,
            BrokerDiscoveryOptions(fixture),
            SystemClock.Instance,
            trustStore);

        await SetBrokerProfileStateAsync(database, fixture.BrokerProfileId, "revoked");

        Assert.Empty(await application.GetBrokerAccountRegistrationOptionsAsync(
            fixture.Actor,
            query: null,
            CancellationToken.None));
    }

    private static ControlPlanePostgresOptions BrokerDiscoveryOptions(BrokerRegistrationFixture fixture)
    {
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
        return options;
    }

    private static async Task<UserActor> SeedBrokerDiscoveryUserAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
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
            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state, email_verified_at)
            values
                (@user_id, @tenant_id, @email, 'active', statement_timestamp());

            insert into identity.user_session_families
                (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
            values
                (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                 'active', statement_timestamp() + interval '1 hour');
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
            command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"discovery-{userId:N}@example.test");
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
            command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return new UserActor(tenantId, userId, sessionId, AuthenticationAssurance.Totp);
    }

    private static async Task SetBrokerProfileStateAsync(
        PostgresTestDatabase database,
        Guid brokerProfileId,
        string state)
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
            "update governance.broker_profiles set state = @state where id = @id",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, brokerProfileId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }
}
