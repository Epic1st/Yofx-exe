using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Conversion.Worker;
using YO4X.Identity;
using YO4X.Persistence.Postgres;
using YO4X.SecretCoordination;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class ProofKeyRotationPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task CredentialGrantCreateRotateReplayAndConsumeUsesPersistedKeyId()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ActorFixture fixture = await SeedActorAsync(database, includeBrokerAccount: true);
        byte[] previousKey = KeyBytes(1);
        byte[] currentKey = KeyBytes(33);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = CreateTrustStore(policyKey);
        ControlPlanePostgresOptions options = CreateOptions();
        var request = new CreateCredentialIngestionSession(
            fixture.BrokerAccountId!.Value,
            CredentialIngestionOperation.Create,
            new Uri("https://portal.example/"));
        var metadata = new RequestMetadata(
            new string('1', 32),
            fixture.CorrelationId,
            0,
            "Create a rotation-safe credential ingestion grant.");

        CredentialIngestionSessionView created;
        string previousKeyId;
        using (var originalRing = new CredentialProofKeyRing(previousKey))
        {
            var originalIssuer = new CredentialIngestionProofIssuer(originalRing);
            previousKeyId = originalIssuer.CurrentKeyId;
            var originalApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                originalIssuer);
            created = await originalApplication.CreateCredentialIngestionSessionAsync(
                fixture.Actor,
                request,
                metadata,
                CancellationToken.None);
        }

        using (var currentOnlyRing = new CredentialProofKeyRing(currentKey))
        {
            var unavailableApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                new CredentialIngestionProofIssuer(currentOnlyRing));
            BackendCapabilityUnavailableException removed =
                await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
                    () => unavailableApplication.CreateCredentialIngestionSessionAsync(
                        fixture.Actor,
                        request,
                        metadata,
                        CancellationToken.None));
            Assert.Equal("credential-ingestion-proof-key-unavailable", removed.Capability);
        }

        CredentialIngestionSessionView replayed;
        using (var rotatedRing = new CredentialProofKeyRing(
            currentKey,
            previousKey,
            DateTimeOffset.UtcNow.AddHours(1)))
        {
            var rotatedApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                new CredentialIngestionProofIssuer(rotatedRing));
            replayed = await rotatedApplication.CreateCredentialIngestionSessionAsync(
                fixture.Actor,
                request,
                metadata,
                CancellationToken.None);
        }

        Assert.Equal(created.GrantId, replayed.GrantId);
        Assert.Equal(created.SingleUseBearer, replayed.SingleUseBearer);
        Assert.Equal(created.SingleUseNonce, replayed.SingleUseNonce);
        Assert.Equal(created.ExpiresAt, replayed.ExpiresAt);
        Assert.Equal(
            previousKeyId,
            await ReadProofKeyIdAsync(
                database,
                "control.credential_ingestion_grants",
                created.GrantId));

        var proof = new CredentialIngestionProof(
            fixture.Actor.TenantId,
            replayed.GrantId,
            "https://portal.example",
            CredentialIngestionProofIssuer.HashProof(replayed.SingleUseBearer),
            CredentialIngestionProofIssuer.HashProof(replayed.SingleUseNonce));
        var ingestionStore = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        CredentialIngestionReservation reservation = await ingestionStore.ReserveAsync(
            proof,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(CredentialIngestionReservationDisposition.Acquired, reservation.Disposition);
        var receipt = new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            reservation.ToWriteBinding(),
            $"vault://rotation-test/{Guid.CreateVersion7():N}",
            SecretWriteReceiptState.Stored,
            "ed25519",
            "rotation-test-key",
            Convert.ToBase64String(new byte[64]));
        CredentialIngestionCompletion completion = await ingestionStore.CompleteAsync(
            reservation,
            receipt,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        Assert.Equal(created.GrantId, completion.GrantId);
        Assert.Equal(
            "consumed",
            await ReadStateAsync(
                database,
                "control.credential_ingestion_grants",
                created.GrantId));
    }

    [PostgresFact]
    public async Task StrategyImportCreateRotateReplayAndConsumeUsesPersistedKeyId()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ActorFixture fixture = await SeedActorAsync(database, includeBrokerAccount: false);
        byte[] previousKey = KeyBytes(1);
        byte[] currentKey = KeyBytes(33);
        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustStore = CreateTrustStore(policyKey);
        ControlPlanePostgresOptions options = CreateOptions();
        var request = new CreateStrategyImportSession("rotation-safe-ea");
        var metadata = new RequestMetadata(
            new string('2', 32),
            fixture.CorrelationId,
            null,
            "Create a rotation-safe strategy import job.");

        StrategyImportSessionView created;
        string previousKeyId;
        using (var originalRing = new StrategyImportProofKeyRing(previousKey))
        {
            var originalIssuer = new StrategyImportProofIssuer(originalRing);
            previousKeyId = originalIssuer.CurrentKeyId;
            var originalApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                strategyImportProofIssuer: originalIssuer);
            created = await originalApplication.CreateStrategyImportSessionAsync(
                fixture.Actor,
                request,
                metadata,
                CancellationToken.None);
        }

        using (var currentOnlyRing = new StrategyImportProofKeyRing(currentKey))
        {
            var unavailableApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                strategyImportProofIssuer: new StrategyImportProofIssuer(currentOnlyRing));
            BackendCapabilityUnavailableException removed =
                await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
                    () => unavailableApplication.CreateStrategyImportSessionAsync(
                        fixture.Actor,
                        request,
                        metadata,
                        CancellationToken.None));
            Assert.Equal("strategy-import-proof-key-unavailable", removed.Capability);
        }

        StrategyImportSessionView replayed;
        using (var rotatedRing = new StrategyImportProofKeyRing(
            currentKey,
            previousKey,
            DateTimeOffset.UtcNow.AddHours(1)))
        {
            var rotatedApplication = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore,
                strategyImportProofIssuer: new StrategyImportProofIssuer(rotatedRing));
            replayed = await rotatedApplication.CreateStrategyImportSessionAsync(
                fixture.Actor,
                request,
                metadata,
                CancellationToken.None);
        }

        Assert.Equal(created.ImportJobId, replayed.ImportJobId);
        Assert.Equal(created.SingleUseCapability, replayed.SingleUseCapability);
        Assert.Equal(created.ExpiresAt, replayed.ExpiresAt);
        Assert.Equal(
            previousKeyId,
            await ReadProofKeyIdAsync(
                database,
                "control.strategy_import_jobs",
                created.ImportJobId));

        byte[] capability = DecodeCapability(replayed.SingleUseCapability);
        try
        {
            byte[] source = Encoding.UTF8.GetBytes("void OnTick(){ int value = 1; }");
            var documents = new[]
            {
                new Mql5SourceDocument("Experts/RotationSafe.mq5", source)
            };
            using var corpus = new Mql5AnalyzedCorpus(
                new Mql5StaticInventoryAnalyzer().Analyze(documents),
                documents);
            using var persistenceRequest = new Mql5CorpusPersistenceRequest(
                replayed.ImportJobId,
                capability);
            Mql5CorpusPersistenceResult persisted = await new PostgresMql5CorpusStore(
                    database.ConversionWorker)
                .PersistAsync(persistenceRequest, corpus);
            Assert.False(persisted.Replayed);
            Assert.Equal(replayed.ImportJobId, persisted.ImportId);
            Assert.Equal(1, persisted.FileCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }

        Assert.Equal(
            "consumed",
            await ReadStateAsync(
                database,
                "control.strategy_import_jobs",
                created.ImportJobId));
    }

    private static async Task<ActorFixture> SeedActorAsync(
        PostgresTestDatabase database,
        bool includeBrokerAccount)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
        Guid correlationId = Guid.CreateVersion7();
        Guid? brokerAccountId = includeBrokerAccount ? Guid.CreateVersion7() : null;
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        // This is a trusted fixture seed, not an application authorization
        // path. The production broker-account transition trigger now requires
        // a transaction-bound tenant capability, which the bootstrap
        // administrator deliberately does not possess. Disable triggers only
        // for this transaction while inserting a constraint-valid baseline;
        // all exercised public mutations run with normal trigger enforcement.
        await using (var fixtureSeed = new NpgsqlCommand(
            "set local session_replication_role = replica",
            connection,
            transaction))
        {
            await fixtureSeed.ExecuteNonQueryAsync();
        }

        string brokerAccountSql = includeBrokerAccount
            ? """

              insert into operations.broker_accounts
                  (id, tenant_id, user_id, broker_id, server, masked_login,
                   binding_fingerprint, environment, credential_state, state)
              values
                  (@broker_account_id, @tenant_id, @user_id, @broker_id,
                   'u0-demo', '***1234', @binding_fingerprint,
                   'demo', 'absent', 'pending');
              """
            : string.Empty;
        await using var command = new NpgsqlCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@tenant_id, @tenant_slug, 'Proof rotation tenant');

            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state, email_verified_at)
            values
                (@user_id, @tenant_id, @email, 'active', statement_timestamp());

            insert into identity.user_session_families
                (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
            values
                (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                 'active', statement_timestamp() + interval '1 hour');
            """ + brokerAccountSql,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("tenant_slug", NpgsqlDbType.Text, $"rotation-{tenantId:N}");
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"rotation-{userId:N}@example.test");
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
        command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('a', 64));
        if (brokerAccountId is not null)
        {
            command.Parameters.AddWithValue(
                "broker_account_id",
                NpgsqlDbType.Uuid,
                brokerAccountId.Value);
            command.Parameters.AddWithValue("broker_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue(
                "binding_fingerprint",
                NpgsqlDbType.Text,
                new string('b', 64));
        }

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new ActorFixture(
            new UserActor(
                tenantId,
                userId,
                sessionId,
                AuthenticationAssurance.Totp),
            correlationId,
            brokerAccountId);
    }

    private static ControlPlanePostgresOptions CreateOptions()
    {
        var options = new ControlPlanePostgresOptions
        {
            ApprovedGatewayDigest = new string('a', 64),
            ApprovedRegion = "integration-region",
            ApprovedBrokerServer = "u0-demo",
            ApprovedBrokerProfileId = Guid.CreateVersion7(),
            ApprovedRuntimeImageDigest = $"sha256:{new string('b', 64)}",
            SecretIngestionOrigin = new Uri("https://ingestion.example/"),
            ApprovedCredentialClientOrigin = new Uri("https://portal.example/"),
            IngestionGrantLifetime = TimeSpan.FromMinutes(5),
            StrategyImportJobLifetime = TimeSpan.FromMinutes(10)
        };
        options.Validate();
        return options;
    }

    private static PolicySignatureTrustStore CreateTrustStore(ECDsa key) =>
        new(new Dictionary<string, byte[]>
        {
            ["proof-rotation-test"] = key.ExportSubjectPublicKeyInfo()
        });

    private static async Task<string> ReadProofKeyIdAsync(
        PostgresTestDatabase database,
        string relation,
        Guid id)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"select proof_key_id from {relation} where id = @id",
            connection);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadStateAsync(
        PostgresTestDatabase database,
        string relation,
        Guid id)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"select state from {relation} where id = @id",
            connection);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static byte[] DecodeCapability(string capability) =>
        Convert.FromBase64String(capability.Replace('-', '+').Replace('_', '/') + "=");

    private static byte[] KeyBytes(int start) =>
        Enumerable.Range(start, 32).Select(static value => (byte)value).ToArray();

    private sealed record ActorFixture(
        UserActor Actor,
        Guid CorrelationId,
        Guid? BrokerAccountId);
}
