using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.Conversion.Worker;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.SecretCoordination;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class PostgresFoundationTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture _postgres = postgres;

    [PostgresFact]
    public async Task FreshDatabaseMigratesAllSchemasWithNoDomainSeedRows()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();

        // Running the migrator twice must be a checksum-verified no-op.
        await database.Administrator.MigrateAsync();

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using (var command = new NpgsqlCommand(
            """
            select
                (select count(*) from pg_namespace where nspname in
                    ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')),
                (select count(*) from control.schema_migrations),
                (select bool_and(c.relrowsecurity and c.relforcerowsecurity)
                 from pg_class as c
                 join pg_namespace as n on n.oid = c.relnamespace
                 where (n.nspname, c.relname) in
                    (('identity', 'tenants'), ('control', 'idempotency_records'),
                     ('audit', 'audit_events'), ('messaging', 'outbox_messages'))),
                (select count(*)
                 from information_schema.columns
                 where table_schema in
                    ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')
                   and data_type = 'uuid'
                   and column_default is not null),
                (select count(*)
                 from information_schema.columns
                 where table_schema in
                    ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')
                   and column_name in
                    ('password', 'secret', 'secret_material', 'credential_material',
                     'credential_payload', 'private_key', 'raw_credential'))
            """,
            connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(8L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.True(reader.GetBoolean(2));
            Assert.Equal(0L, reader.GetInt64(3));
            Assert.Equal(0L, reader.GetInt64(4));
        }

        await AssertNoDomainRowsAsync(connection);
    }

    [PostgresFact]
    public async Task MissingContextFailsClosedForReadsAndWrites()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext context = NewContext();
        await SeedTenantAsync(database.Application, context);

        await using NpgsqlConnection connection = await database.Application.OpenConnectionAsync();
        await using (var read = new NpgsqlCommand("select count(*) from identity.tenants", connection))
        {
            Assert.Equal(0L, (long)(await read.ExecuteScalarAsync())!);
        }

        Guid forbiddenTenantId = Guid.CreateVersion7();
        await using var write = new NpgsqlCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@id, @slug, @display_name)
            """,
            connection);
        write.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, forbiddenTenantId);
        write.Parameters.AddWithValue("slug", NpgsqlDbType.Text, $"tenant-{forbiddenTenantId:N}");
        write.Parameters.AddWithValue("display_name", NpgsqlDbType.Text, "Forbidden tenant");

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await write.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [PostgresFact]
    public async Task ForceRlsDeniesCrossTenantRows()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext first = NewContext();
        TenantExecutionContext second = NewContext();
        await SeedTenantAsync(database.Application, first);
        await SeedTenantAsync(database.Application, second);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditEvent auditEvent = NewAuditEvent(first, now);
        OutboxMessage message = NewOutboxMessage(first, now);
        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(first))
        {
            await PostgresAuditOutboxWriter.AppendAsync(transaction, auditEvent, message);
            await transaction.CommitAsync();
        }

        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(second))
        {
            Assert.Equal(1L, await ScalarInt64Async(transaction, "select count(*) from identity.tenants"));
            Assert.Equal(0L, await ScalarInt64Async(transaction, "select count(*) from audit.audit_events"));
            Assert.Equal(0L, await ScalarInt64Async(transaction, "select count(*) from messaging.outbox_messages"));
            await transaction.CommitAsync();
        }

        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(first))
        {
            Assert.Equal(1L, await ScalarInt64Async(transaction, "select count(*) from audit.audit_events"));
            Assert.Equal(1L, await ScalarInt64Async(transaction, "select count(*) from messaging.outbox_messages"));
            await transaction.CommitAsync();
        }
    }

    [PostgresFact]
    public async Task TransactionLocalContextDoesNotLeakThroughThePool()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 0
        };
        await using var singleConnectionPool = new PostgresDatabase(connectionString.ConnectionString);
        TenantExecutionContext context = NewContext();
        await SeedTenantAsync(singleConnectionPool, context);

        await using (TenantPostgresTransaction transaction =
            await singleConnectionPool.BeginTenantTransactionAsync(context))
        {
            await using NpgsqlCommand readContext = transaction.CreateCommand(
                "select control.current_tenant_id(), control.current_actor_id()");
            await using (NpgsqlDataReader reader = await readContext.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(context.TenantId, reader.GetGuid(0));
                Assert.Equal(context.ActorId, reader.GetGuid(1));
            }

            await transaction.CommitAsync();
        }

        // MaxPoolSize=1 guarantees this checkout reuses the same physical pool slot.
        await using NpgsqlConnection connection = await singleConnectionPool.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select control.current_tenant_id(), control.current_actor_id(), control.current_correlation_id()",
            connection);
        await using NpgsqlDataReader contextReader = await command.ExecuteReaderAsync();
        Assert.True(await contextReader.ReadAsync());
        Assert.True(contextReader.IsDBNull(0));
        Assert.True(contextReader.IsDBNull(1));
        Assert.True(contextReader.IsDBNull(2));
    }

    [PostgresFact]
    public async Task AuditAndOutboxCommitOrRollbackTogether()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext context = NewContext();
        await SeedTenantAsync(database.Application, context);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context))
        {
            await PostgresAuditOutboxWriter.AppendAsync(
                transaction,
                NewAuditEvent(context, now),
                NewOutboxMessage(context, now));
            // Deliberately dispose without commit.
        }

        await AssertEvidenceCountsAsync(database.Application, context, 0, 0);

        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context))
        {
            await PostgresAuditOutboxWriter.AppendAsync(
                transaction,
                NewAuditEvent(context, now),
                NewOutboxMessage(context, now));
            await transaction.CommitAsync();
        }

        await AssertEvidenceCountsAsync(database.Application, context, 1, 1);
    }

    [PostgresFact]
    public async Task ConcurrentOutboxWorkersSkipLockedMessages()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext producer = NewContext();
        await SeedTenantAsync(database.Application, producer);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(producer))
        {
            await PostgresOutboxRepository.EnqueueAsync(transaction, NewOutboxMessage(producer, now));
            await PostgresOutboxRepository.EnqueueAsync(
                transaction,
                NewOutboxMessage(producer, now.AddMilliseconds(1)));
            await transaction.CommitAsync();
        }

        TenantExecutionContext workerOneContext = new(
            producer.TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        TenantExecutionContext workerTwoContext = new(
            producer.TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await using TenantPostgresTransaction workerOne =
            await database.Application.BeginTenantTransactionAsync(workerOneContext);
        IReadOnlyList<ClaimedOutboxMessage> firstClaim = await PostgresOutboxRepository.ClaimAsync(
            workerOne,
            "worker-one",
            1,
            now.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        // Worker one's update remains locked and uncommitted while worker two claims.
        await using TenantPostgresTransaction workerTwo =
            await database.Application.BeginTenantTransactionAsync(workerTwoContext);
        IReadOnlyList<ClaimedOutboxMessage> secondClaim = await PostgresOutboxRepository.ClaimAsync(
            workerTwo,
            "worker-two",
            1,
            now.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        Assert.Single(firstClaim);
        Assert.Single(secondClaim);
        Assert.NotEqual(firstClaim[0].Id, secondClaim[0].Id);
        Assert.Equal(1, firstClaim[0].Attempts);
        Assert.Equal(1, secondClaim[0].Attempts);

        await workerOne.CommitAsync();
        await workerTwo.CommitAsync();
    }

    [PostgresFact]
    public async Task CredentialGrantReservationIsHashedConcurrentAndLeaseFenced()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext context = NewContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeededCredentialGrant seeded = await SeedCredentialGrantAsync(database, context, now);
        var firstStore = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        var secondStore = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        await AssertSecretIngestionExecuteOnlyBoundaryAsync(database.SecretIngestion);
        Assert.True(await firstStore.IsReadyAsync(CancellationToken.None));

        var wrongProof = new CredentialIngestionProof(
            context.TenantId,
            seeded.GrantId,
            seeded.Origin,
            new string('f', 64),
            seeded.NonceHash);
        UnauthorizedAccessException mismatch = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => firstStore.ReserveAsync(wrongProof, now, TimeSpan.FromSeconds(30), CancellationToken.None));
        UnauthorizedAccessException missing = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => firstStore.ReserveAsync(
                new CredentialIngestionProof(
                    context.TenantId,
                    Guid.CreateVersion7(),
                    seeded.Origin,
                    seeded.BearerHash,
                    seeded.NonceHash),
                now,
                TimeSpan.FromSeconds(30),
                CancellationToken.None));
        Assert.Equal(mismatch.Message, missing.Message);

        var proof = new CredentialIngestionProof(
            context.TenantId,
            seeded.GrantId,
            seeded.Origin,
            seeded.BearerHash,
            seeded.NonceHash);
        CredentialIngestionReservation[] concurrent = await Task.WhenAll(
            firstStore.ReserveAsync(proof, now, TimeSpan.FromSeconds(1), CancellationToken.None),
            secondStore.ReserveAsync(proof, now, TimeSpan.FromSeconds(1), CancellationToken.None));
        CredentialIngestionReservation acquired = Assert.Single(
            concurrent,
            item => item.Disposition == CredentialIngestionReservationDisposition.Acquired);
        CredentialIngestionReservation inProgress = Assert.Single(
            concurrent,
            item => item.Disposition == CredentialIngestionReservationDisposition.InProgress);
        Assert.Equal(acquired.AttemptId, inProgress.AttemptId);

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));
        DateTimeOffset takeoverAt = DateTimeOffset.UtcNow;
        CredentialIngestionReservation takeover = await firstStore.ReserveAsync(
            proof,
            takeoverAt,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(CredentialIngestionReservationDisposition.Acquired, takeover.Disposition);
        Assert.NotEqual(acquired.AttemptId, takeover.AttemptId);

        ResourceConflictException staleRelease = await Assert.ThrowsAsync<ResourceConflictException>(
            () => secondStore.ReleaseBeforeWriteAsync(
                acquired,
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        Assert.Equal("INGESTION_RESERVATION_LOST", staleRelease.Code);
        (string stateAfterStaleRelease, Guid? reservationAfterStaleRelease) =
            await ReadGrantReservationStateAsync(database.Application, context, seeded.GrantId);
        Assert.Equal("reserved", stateAfterStaleRelease);
        Assert.Equal(takeover.AttemptId, reservationAfterStaleRelease);

        await firstStore.ReleaseBeforeWriteAsync(
            takeover,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        (string releasedState, Guid? releasedReservation) =
            await ReadGrantReservationStateAsync(database.Application, context, seeded.GrantId);
        Assert.Equal("active", releasedState);
        Assert.Null(releasedReservation);

        await using TenantPostgresTransaction verification =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand hashes = verification.CreateCommand(
            "select bearer_hash, nonce_hash from control.credential_ingestion_grants where id = @id");
        hashes.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, seeded.GrantId);
        await using (NpgsqlDataReader hashReader = await hashes.ExecuteReaderAsync())
        {
            Assert.True(await hashReader.ReadAsync());
            Assert.Equal(seeded.BearerHash, hashReader.GetString(0));
            Assert.Equal(seeded.NonceHash, hashReader.GetString(1));
        }

        await verification.CommitAsync();
    }

    [PostgresFact]
    public async Task CredentialGrantCompletionIsAtomicIdempotentAndRedacted()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext context = NewContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeededCredentialGrant seeded = await SeedCredentialGrantAsync(database, context, now);
        var store = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        var proof = new CredentialIngestionProof(
            context.TenantId,
            seeded.GrantId,
            seeded.Origin,
            seeded.BearerHash,
            seeded.NonceHash);
        CredentialIngestionReservation reservation = await store.ReserveAsync(
            proof,
            now,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        var receipt = new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            reservation.ToWriteBinding(),
            $"vault://test/{Guid.CreateVersion7():N}",
            SecretWriteReceiptState.Stored,
            "ed25519",
            "integration-key-v1",
            Convert.ToBase64String(new byte[64]));

        DateTimeOffset completionStartedAt = DateTimeOffset.UtcNow;
        CredentialIngestionCompletion[] completions = await Task.WhenAll(
            store.CompleteAsync(reservation, receipt, now.AddSeconds(2), CancellationToken.None),
            store.CompleteAsync(reservation, receipt, now.AddSeconds(3), CancellationToken.None));
        DateTimeOffset completionFinishedAt = DateTimeOffset.UtcNow;
        Assert.All(completions, completion => Assert.Equal(seeded.GrantId, completion.GrantId));
        Assert.Equal(completions[0].CompletedAt, completions[1].CompletedAt);
        Assert.InRange(
            completions[0].CompletedAt,
            completionStartedAt.AddSeconds(-1),
            completionFinishedAt.AddSeconds(1));

        CredentialIngestionReservation replay = await store.ReserveAsync(
            proof,
            now.AddSeconds(4),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(CredentialIngestionReservationDisposition.Completed, replay.Disposition);
        Assert.Equal(reservation.AttemptId, replay.AttemptId);

        ResourceConflictException conflict = await Assert.ThrowsAsync<ResourceConflictException>(
            () => store.CompleteAsync(
                reservation,
                new SecretWriteReceipt(
                    SecretBrokerProvider.HashiCorpVault,
                    reservation.ToWriteBinding(),
                    receipt.OpaqueReference,
                    SecretWriteReceiptState.Stored,
                    "ed25519",
                    "integration-key-v2",
                    Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray())),
                now.AddSeconds(5),
                CancellationToken.None));
        Assert.Equal("INGESTION_COMPLETION_CONFLICT", conflict.Code);

        await using TenantPostgresTransaction verification =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = verification.CreateCommand(
            """
            select
                ingestion_grant.state,
                ingestion_grant.completion_digest,
                ingestion_grant.row_version,
                account.credential_state,
                account.credential_reference,
                account.row_version,
                (select count(*) from audit.audit_events),
                (select count(*) from messaging.outbox_messages),
                (select count(*) from audit.audit_events
                    where payload::text like '%' || @opaque_reference || '%'
                       or payload::text like '%' || @completion_digest || '%'
                       or payload::text like '%' || @bearer_hash || '%'),
                (select count(*) from messaging.outbox_messages
                    where payload::text like '%' || @opaque_reference || '%'
                       or payload::text like '%' || @completion_digest || '%'
                       or payload::text like '%' || @bearer_hash || '%')
            from control.credential_ingestion_grants as ingestion_grant
            join operations.broker_accounts as account
              on account.tenant_id = ingestion_grant.tenant_id
             and account.id = ingestion_grant.broker_account_id
            where ingestion_grant.id = @grant_id
            """);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, seeded.GrantId);
        command.Parameters.AddWithValue("opaque_reference", NpgsqlDbType.Text, receipt.OpaqueReference);
        command.Parameters.AddWithValue("completion_digest", NpgsqlDbType.Text, receipt.CompletionDigest);
        command.Parameters.AddWithValue("bearer_hash", NpgsqlDbType.Text, seeded.BearerHash);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("consumed", reader.GetString(0));
            Assert.Equal(receipt.CompletionDigest, reader.GetString(1));
            Assert.Equal(2L, reader.GetInt64(2));
            Assert.Equal("ready", reader.GetString(3));
            Assert.Equal(receipt.OpaqueReference, reader.GetString(4));
            Assert.Equal(2L, reader.GetInt64(5));
            Assert.Equal(1L, reader.GetInt64(6));
            Assert.Equal(1L, reader.GetInt64(7));
            Assert.Equal(0L, reader.GetInt64(8));
            Assert.Equal(0L, reader.GetInt64(9));
        }

        await verification.CommitAsync();
    }

    [PostgresFact]
    public async Task CredentialGrantControlBoundaryRejectsForgedAuthorityAndState()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture fixture = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);

        Guid createGrantId = await CreateControlCredentialGrantAsync(
            database.ControlApi,
            ownerContext,
            fixture.CreateAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            advanceAccountState: true);
        Guid rotateGrantId = await CreateControlCredentialGrantAsync(
            database.ControlApi,
            ownerContext,
            fixture.RotateAccountId,
            "rotate",
            TimeSpan.FromMinutes(5),
            advanceAccountState: true);
        Assert.NotEqual(Guid.Empty, createGrantId);
        Assert.NotEqual(Guid.Empty, rotateGrantId);
        Assert.Equal(
            ("active", "ingestion_pending"),
            await ReadGrantAndAccountStateAsync(database, createGrantId));
        Assert.Equal(
            ("active", "rotation_pending"),
            await ReadGrantAndAccountStateAsync(database, rotateGrantId));

        var wrongActorContext = new TenantExecutionContext(
            ownerContext.TenantId,
            fixture.OtherUserId,
            Guid.CreateVersion7());
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            wrongActorContext,
            fixture.UnusedValidAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            new TenantExecutionContext(
                Guid.CreateVersion7(),
                ownerContext.ActorId,
                Guid.CreateVersion7()),
            fixture.UnusedValidAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            PostgresErrorCodes.InsufficientPrivilege,
            insertedTenantId: ownerContext.TenantId);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.LiveAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.DisabledAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.DeletedAccountId,
            "create",
            TimeSpan.FromMinutes(5),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.UnusedValidAccountId,
            "create",
            TimeSpan.FromMinutes(11),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantCreationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.UnusedValidAccountId,
            "create",
            TimeSpan.FromSeconds(-1),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertCredentialGrantInsertShapeRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.UnusedValidAccountId);

        await AssertCredentialGrantTransitionRejectedAsync(
            database.ControlApi,
            ownerContext,
            createGrantId,
            "reserved",
            includeFabricatedReservation: true,
            expectedSqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await AssertCredentialGrantTransitionRejectedAsync(
            database.ControlApi,
            ownerContext,
            createGrantId,
            "consumed",
            includeFabricatedReservation: false,
            expectedSqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await AssertCredentialGrantTransitionRejectedAsync(
            database.ControlApi,
            ownerContext,
            createGrantId,
            "expired",
            includeFabricatedReservation: false,
            expectedSqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState);

        var ingestionContext = new TenantExecutionContext(
            ownerContext.TenantId,
            Guid.Parse("9fda7b52-620b-4eb9-a34c-632163a6078f"),
            createGrantId);
        await AssertSecretReferenceFabricationRejectedAsync(
            database.SecretIngestion,
            ingestionContext,
            fixture.CreateAccountId);
        await AssertControlAccountReactivationRejectedAsync(
            database.ControlApi,
            ownerContext,
            fixture.DisabledAccountId);

        await UpdateCredentialGrantStateAsync(
            database.ControlApi,
            ownerContext,
            createGrantId,
            "revoked");
        await AssertCredentialGrantTransitionRejectedAsync(
            database.ControlApi,
            ownerContext,
            createGrantId,
            "active",
            includeFabricatedReservation: false,
            expectedSqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState);
        Assert.Equal(
            ("revoked", "ingestion_pending"),
            await ReadGrantAndAccountStateAsync(database, createGrantId));
    }

    [PostgresFact]
    public async Task CredentialGrantRotationCompletionRetainsStableOpaqueReference()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture fixture = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        string bearerHash = RandomHexDigest();
        string nonceHash = RandomHexDigest();
        Guid grantId = await CreateControlCredentialGrantAsync(
            database.ControlApi,
            ownerContext,
            fixture.RotateAccountId,
            "rotate",
            TimeSpan.FromMinutes(5),
            advanceAccountState: true,
            bearerHash: bearerHash,
            nonceHash: nonceHash);
        var proof = new CredentialIngestionProof(
            ownerContext.TenantId,
            grantId,
            "https://ingest.test",
            bearerHash,
            nonceHash);
        var store = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        CredentialIngestionReservation reservation = await store.ReserveAsync(
            proof,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        const string stableReference = "vault://test/existing";
        var receipt = new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            reservation.ToWriteBinding(),
            stableReference,
            SecretWriteReceiptState.Stored,
            "ed25519",
            "integration-key-v1",
            Convert.ToBase64String(new byte[64]));

        CredentialIngestionCompletion first = await store.CompleteAsync(
            reservation,
            receipt,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        CredentialIngestionCompletion replay = await store.CompleteAsync(
            reservation,
            receipt,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        Assert.Equal(first, replay);

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select ingestion_grant.state, account.state, account.credential_state,
                   account.credential_reference, account.row_version,
                   (select count(*) from audit.audit_events),
                   (select count(*) from messaging.outbox_messages)
            from control.credential_ingestion_grants as ingestion_grant
            join operations.broker_accounts as account
              on account.tenant_id = ingestion_grant.tenant_id
             and account.id = ingestion_grant.broker_account_id
            where ingestion_grant.id = @grant_id
            """,
            connection);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("consumed", reader.GetString(0));
        Assert.Equal("active", reader.GetString(1));
        Assert.Equal("ready", reader.GetString(2));
        Assert.Equal(stableReference, reader.GetString(3));
        Assert.Equal(2L, reader.GetInt64(4));
        Assert.Equal(1L, reader.GetInt64(5));
        Assert.Equal(1L, reader.GetInt64(6));
    }

    [PostgresFact]
    public async Task CredentialGrantWorkerCleanupRecoversCreateAndRotateExpiryExactly()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture fixture = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        Guid createGrantId = await CreateControlCredentialGrantAsync(
            database.ControlApi,
            ownerContext,
            fixture.CreateAccountId,
            "create",
            TimeSpan.FromMilliseconds(250),
            advanceAccountState: true);
        Guid rotateGrantId = await CreateControlCredentialGrantAsync(
            database.ControlApi,
            ownerContext,
            fixture.RotateAccountId,
            "rotate",
            TimeSpan.FromMilliseconds(250),
            advanceAccountState: true);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        CredentialCleanupResult create = await ClaimAndCompleteCredentialCleanupAsync(
            database.Worker,
            ownerContext.TenantId,
            createGrantId,
            fixture.CreateAccountId);
        CredentialCleanupResult rotate = await ClaimAndCompleteCredentialCleanupAsync(
            database.Worker,
            ownerContext.TenantId,
            rotateGrantId,
            fixture.RotateAccountId);
        Assert.Equal("expired", create.NextState);
        Assert.Equal("expired", rotate.NextState);
        Assert.False(create.Replayed);
        Assert.False(rotate.Replayed);

        CredentialCleanupResult createReplay = await ReplayCredentialCleanupAsync(
            database.Worker,
            ownerContext.TenantId,
            createGrantId,
            create);
        CredentialCleanupResult rotateReplay = await ReplayCredentialCleanupAsync(
            database.Worker,
            ownerContext.TenantId,
            rotateGrantId,
            rotate);
        Assert.True(createReplay.Replayed);
        Assert.True(rotateReplay.Replayed);
        Assert.Equal(create.GrantVersion, createReplay.GrantVersion);
        Assert.Equal(rotate.GrantVersion, rotateReplay.GrantVersion);

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select row(grant_row.state, account.credential_state,
                            account.credential_reference is null,
                            grant_row.cleanup_claim_token is null)::text
                 from control.credential_ingestion_grants as grant_row
                 join operations.broker_accounts as account
                   on account.tenant_id = grant_row.tenant_id
                  and account.id = grant_row.broker_account_id
                 where grant_row.id = @create_grant_id),
                (select row(grant_row.state, account.credential_state,
                            account.credential_reference,
                            grant_row.cleanup_claim_token is null)::text
                 from control.credential_ingestion_grants as grant_row
                 join operations.broker_accounts as account
                   on account.tenant_id = grant_row.tenant_id
                  and account.id = grant_row.broker_account_id
                 where grant_row.id = @rotate_grant_id),
                (select count(*) from audit.audit_events),
                (select count(*) from messaging.outbox_messages)
            """,
            connection);
        command.Parameters.AddWithValue("create_grant_id", NpgsqlDbType.Uuid, createGrantId);
        command.Parameters.AddWithValue("rotate_grant_id", NpgsqlDbType.Uuid, rotateGrantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("(expired,absent,t,t)", reader.GetString(0));
        Assert.Equal("(expired,ready,vault://test/existing,t)", reader.GetString(1));
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal(2L, reader.GetInt64(3));

        await AssertWorkerDirectBrokerAccountMutationRejectedAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.RotateAccountId,
            rotateGrantId);
    }

    [PostgresFact]
    public async Task ConfirmedBrokerDeleteAndRotationProjectThroughWorkerCapabilityOnly()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture accounts = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        BrokerOperationFixture fixture = await SeedConfirmedBrokerOperationFixtureAsync(
            database,
            ownerContext,
            accounts.RotateAccountId);

        Assert.False(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.DeleteOperationId,
            Guid.CreateVersion7(),
            fixture.CorrelationId));
        Assert.True(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.DeleteOperationId,
            fixture.DeleteResultId,
            fixture.CorrelationId));
        Assert.True(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.DeleteOperationId,
            fixture.DeleteResultId,
            fixture.CorrelationId));
        Assert.True(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.RotateOperationId,
            fixture.RotateResultId,
            fixture.CorrelationId));
        Assert.True(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.RotateOperationId,
            fixture.RotateResultId,
            fixture.CorrelationId));

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select row(state, credential_state, credential_reference is null, row_version)::text
                 from operations.broker_accounts where id = @delete_account_id),
                (select row(state, credential_state, credential_reference, row_version)::text
                 from operations.broker_accounts where id = @rotate_account_id),
                (select count(*) from operations.user_operation_results)
            """,
            connection);
        command.Parameters.AddWithValue(
            "delete_account_id",
            NpgsqlDbType.Uuid,
            fixture.DeleteAccountId);
        command.Parameters.AddWithValue(
            "rotate_account_id",
            NpgsqlDbType.Uuid,
            accounts.RotateAccountId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("(disabled,deleted,t,1)", reader.GetString(0));
        Assert.Equal("(active,ready,vault://test/existing,2)", reader.GetString(1));
        Assert.Equal(2L, reader.GetInt64(2));

        await AssertWorkerDirectBrokerAccountMutationRejectedAsync(
            database.Worker,
            ownerContext.TenantId,
            accounts.RotateAccountId,
            fixture.CorrelationId);
    }

    [PostgresFact]
    public async Task RuntimeTransactionRejectsMigrationOwnerRole()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await database.Administrator.BeginTenantTransactionAsync(NewContext()));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static TenantExecutionContext NewContext() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7());

    private static async Task SeedTenantAsync(
        PostgresDatabase database,
        TenantExecutionContext context)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@id, @slug, @display_name)
            """
        );
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("slug", NpgsqlDbType.Text, $"tenant-{context.TenantId:N}");
        command.Parameters.AddWithValue("display_name", NpgsqlDbType.Text, $"Tenant {context.TenantId:N}");
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<SeededCredentialGrant> SeedCredentialGrantAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        DateTimeOffset createdAt)
    {
        await SeedTenantAsync(database.Application, context);
        Guid userId = context.ActorId;
        Guid brokerAccountId = Guid.CreateVersion7();
        Guid grantId = Guid.CreateVersion7();
        Guid brokerId = Guid.CreateVersion7();
        string bearerHash = new string('a', 64);
        string nonceHash = new string('b', 64);
        const string origin = "https://ingest.test";

        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state, email_verified_at, created_at, updated_at)
            values
                (@user_id, @tenant_id, @email, 'active', @created_at, @created_at, @created_at);

            insert into operations.broker_accounts
                (id, tenant_id, user_id, broker_id, server, masked_login, binding_fingerprint,
                 environment, credential_state, state, created_at, updated_at)
            values
                (@broker_account_id, @tenant_id, @user_id, @broker_id, 'u0-demo', '***1234',
                 @binding_fingerprint, 'demo', 'absent', 'pending', @created_at, @created_at);
            """);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"user-{userId:N}@example.test");
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt.ToUniversalTime());
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        command.Parameters.AddWithValue("broker_id", NpgsqlDbType.Uuid, brokerId);
        command.Parameters.AddWithValue("binding_fingerprint", NpgsqlDbType.Text, new string('e', 64));
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        TenantExecutionContext grantContext = new(
            context.TenantId,
            context.ActorId,
            grantId);
        await using TenantPostgresTransaction control =
            await database.ControlApi.BeginTenantTransactionAsync(grantContext);
        await using NpgsqlCommand createGrant = control.CreateCommand(
            """
            insert into control.credential_ingestion_grants
                (id, tenant_id, broker_account_id, operation, allowed_origin,
                 bearer_hash, nonce_hash, expires_at)
            values
                (@grant_id, @tenant_id, @broker_account_id, 'create', @origin,
                 @bearer_hash, @nonce_hash, statement_timestamp() + interval '5 minutes');

            update operations.broker_accounts
            set credential_state = 'ingestion_pending',
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @broker_account_id
              and tenant_id = @tenant_id;
            """);
        createGrant.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        createGrant.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        createGrant.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        createGrant.Parameters.AddWithValue("origin", NpgsqlDbType.Text, origin);
        createGrant.Parameters.AddWithValue("bearer_hash", NpgsqlDbType.Text, bearerHash);
        createGrant.Parameters.AddWithValue("nonce_hash", NpgsqlDbType.Text, nonceHash);
        await createGrant.ExecuteNonQueryAsync();
        await control.CommitAsync();
        return new SeededCredentialGrant(
            grantId,
            brokerAccountId,
            origin,
            bearerHash,
            nonceHash);
    }

    private static async Task<CredentialBoundaryFixture> SeedCredentialBoundaryFixtureAsync(
        PostgresDatabase database,
        TenantExecutionContext context)
    {
        await SeedTenantAsync(database, context);
        Guid otherUserId = Guid.CreateVersion7();
        Guid createAccountId = Guid.CreateVersion7();
        Guid rotateAccountId = Guid.CreateVersion7();
        Guid unusedValidAccountId = Guid.CreateVersion7();
        Guid liveAccountId = Guid.CreateVersion7();
        Guid disabledAccountId = Guid.CreateVersion7();
        Guid deletedAccountId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state,
                 email_verified_at, created_at, updated_at)
            values
                (@owner_id, @tenant_id, @owner_email, 'active', @now, @now, @now),
                (@other_id, @tenant_id, @other_email, 'active', @now, @now, @now);

            insert into operations.broker_accounts
                (id, tenant_id, user_id, broker_id, server, masked_login,
                 binding_fingerprint, environment, credential_reference,
                 credential_state, state, created_at, updated_at)
            values
                (@create_id, @tenant_id, @owner_id, @create_broker, 'demo-create',
                 '***1001', @create_fingerprint, 'demo', null, 'absent', 'pending', @now, @now),
                (@rotate_id, @tenant_id, @owner_id, @rotate_broker, 'demo-rotate',
                 '***1002', @rotate_fingerprint, 'demo', 'vault://test/existing',
                 'ready', 'active', @now, @now),
                (@unused_id, @tenant_id, @owner_id, @unused_broker, 'demo-unused',
                 '***1003', @unused_fingerprint, 'demo', null, 'absent', 'pending', @now, @now),
                (@live_id, @tenant_id, @owner_id, @live_broker, 'live-account',
                 '***1004', @live_fingerprint, 'live', null, 'absent', 'pending', @now, @now),
                (@disabled_id, @tenant_id, @owner_id, @disabled_broker, 'demo-disabled',
                 '***1005', @disabled_fingerprint, 'demo', null, 'absent', 'disabled', @now, @now),
                (@deleted_id, @tenant_id, @owner_id, @deleted_broker, 'demo-deleted',
                 '***1006', @deleted_fingerprint, 'demo', null, 'deleted', 'deleted', @now, @now);
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("owner_id", NpgsqlDbType.Uuid, context.ActorId);
        command.Parameters.AddWithValue("other_id", NpgsqlDbType.Uuid, otherUserId);
        command.Parameters.AddWithValue("owner_email", NpgsqlDbType.Text, $"owner-{context.ActorId:N}@example.test");
        command.Parameters.AddWithValue("other_email", NpgsqlDbType.Text, $"other-{otherUserId:N}@example.test");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        AddAccountParameters(command, "create", createAccountId);
        AddAccountParameters(command, "rotate", rotateAccountId);
        AddAccountParameters(command, "unused", unusedValidAccountId);
        AddAccountParameters(command, "live", liveAccountId);
        AddAccountParameters(command, "disabled", disabledAccountId);
        AddAccountParameters(command, "deleted", deletedAccountId);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new CredentialBoundaryFixture(
            otherUserId,
            createAccountId,
            rotateAccountId,
            unusedValidAccountId,
            liveAccountId,
            disabledAccountId,
            deletedAccountId);
    }

    private static async Task AssertSecretIngestionExecuteOnlyBoundaryAsync(
        PostgresDatabase database)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            with protected_relations as
            (
                select
                    (max(relation.oid::bigint) filter
                        (where namespace.nspname = 'control'
                           and relation.relname = 'credential_ingestion_grants'))::oid as grants_oid,
                    (max(relation.oid::bigint) filter
                        (where namespace.nspname = 'operations'
                           and relation.relname = 'broker_accounts'))::oid as accounts_oid,
                    (max(relation.oid::bigint) filter
                        (where namespace.nspname = 'audit'
                           and relation.relname = 'audit_events'))::oid as audit_oid,
                    (max(relation.oid::bigint) filter
                        (where namespace.nspname = 'messaging'
                           and relation.relname = 'outbox_messages'))::oid as outbox_oid
                from pg_catalog.pg_class as relation
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
            )
            select
                current_user = 'yo4x_secret_ingestion',
                has_function_privilege(current_user,
                    'control.reserve_credential_ingestion_grant(uuid,uuid,text,text,text,integer,uuid,uuid)',
                    'EXECUTE'),
                has_function_privilege(current_user,
                    'control.release_credential_ingestion_grant(uuid,uuid,bigint,uuid,uuid)',
                    'EXECUTE'),
                has_function_privilege(current_user,
                    'control.complete_credential_ingestion_grant(uuid,uuid,bigint,text,text,uuid,uuid)',
                    'EXECUTE'),
                not has_function_privilege(current_user,
                    'control.expire_secret_credential_ingestion_grant(uuid,bigint,uuid,uuid)',
                    'EXECUTE'),
                not has_function_privilege(current_user,
                    'control.acquire_u0_authority_lock()', 'EXECUTE'),
                not has_any_column_privilege(current_user, grants_oid, 'SELECT'),
                not has_any_column_privilege(current_user, grants_oid, 'UPDATE'),
                not has_any_column_privilege(current_user, accounts_oid, 'SELECT'),
                not has_any_column_privilege(current_user, accounts_oid, 'UPDATE'),
                not has_table_privilege(current_user, audit_oid, 'INSERT'),
                not has_table_privilege(current_user, outbox_oid, 'INSERT')
            from protected_relations
            """,
            connection);
        string[] assertions =
        {
            "exact runtime identity",
            "reserve capability",
            "release capability",
            "complete capability",
            "no secret expiry capability",
            "no raw authority lock",
            "no grant SELECT",
            "no grant UPDATE",
            "no account SELECT",
            "no account UPDATE",
            "no direct audit INSERT",
            "no direct outbox INSERT"
        };
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (int index = 0; index < assertions.Length; index++)
        {
            Assert.True(reader.GetBoolean(index), assertions[index]);
        }
    }

    private static void AddAccountParameters(
        NpgsqlCommand command,
        string prefix,
        Guid accountId)
    {
        command.Parameters.AddWithValue($"{prefix}_id", NpgsqlDbType.Uuid, accountId);
        command.Parameters.AddWithValue($"{prefix}_broker", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue(
            $"{prefix}_fingerprint",
            NpgsqlDbType.Text,
            RandomHexDigest());
    }

    private static async Task<Guid> CreateControlCredentialGrantAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid brokerAccountId,
        string operation,
        TimeSpan lifetime,
        bool advanceAccountState,
        Guid? insertedTenantId = null,
        string? bearerHash = null,
        string? nonceHash = null)
    {
        Guid grantId = Guid.CreateVersion7();
        Guid targetTenantId = insertedTenantId ?? context.TenantId;
        TenantExecutionContext transactionContext = advanceAccountState
            ? new TenantExecutionContext(context.TenantId, context.ActorId, grantId)
            : context;
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(transactionContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.credential_ingestion_grants
                (id, tenant_id, broker_account_id, operation, allowed_origin,
                 bearer_hash, nonce_hash, expires_at)
            values
                (@grant_id, @tenant_id, @broker_account_id, @operation,
                 'https://ingest.test', @bearer_hash, @nonce_hash,
                 statement_timestamp() + @lifetime);
            """);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, targetTenantId);
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue(
            "bearer_hash",
            NpgsqlDbType.Text,
            bearerHash ?? RandomHexDigest());
        command.Parameters.AddWithValue(
            "nonce_hash",
            NpgsqlDbType.Text,
            nonceHash ?? RandomHexDigest());
        command.Parameters.AddWithValue("lifetime", NpgsqlDbType.Interval, lifetime);
        await command.ExecuteNonQueryAsync();

        if (advanceAccountState)
        {
            string targetCredentialState = operation == "create"
                ? "ingestion_pending"
                : "rotation_pending";
            await using NpgsqlCommand advance = transaction.CreateCommand(
                """
                update operations.broker_accounts
                set credential_state = @credential_state,
                    row_version = row_version + 1,
                    updated_at = statement_timestamp()
                where id = @broker_account_id
                  and tenant_id = @tenant_id
                """);
            advance.Parameters.AddWithValue(
                "credential_state",
                NpgsqlDbType.Text,
                targetCredentialState);
            advance.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
            advance.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
            Assert.Equal(1, await advance.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        return grantId;
    }

    private static async Task AssertCredentialGrantCreationRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid brokerAccountId,
        string operation,
        TimeSpan lifetime,
        string expectedSqlState,
        Guid? insertedTenantId = null)
    {
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            () => CreateControlCredentialGrantAsync(
                database,
                context,
                brokerAccountId,
                operation,
                lifetime,
                advanceAccountState: false,
                insertedTenantId: insertedTenantId));
        Assert.Equal(expectedSqlState, rejected.SqlState);
    }

    private static async Task AssertCredentialGrantInsertShapeRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid brokerAccountId)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.credential_ingestion_grants
                (id, tenant_id, broker_account_id, operation, allowed_origin,
                 bearer_hash, nonce_hash, state, reservation_id, reserved_at,
                 reservation_expires_at, expires_at, row_version)
            values
                (@grant_id, @tenant_id, @broker_account_id, 'create',
                 'https://ingest.test', @bearer_hash, @nonce_hash, 'reserved',
                 @reservation_id, statement_timestamp(),
                 statement_timestamp() + interval '1 minute',
                 statement_timestamp() + interval '5 minutes', 9)
            """);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        command.Parameters.AddWithValue("bearer_hash", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("nonce_hash", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("reservation_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    private static async Task AssertCredentialGrantTransitionRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid grantId,
        string state,
        bool includeFabricatedReservation,
        string expectedSqlState)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        string sql = includeFabricatedReservation
            ? """
              update control.credential_ingestion_grants
              set state = @state,
                  reservation_id = @reservation_id,
                  reserved_at = statement_timestamp(),
                  reservation_expires_at = statement_timestamp() + interval '1 minute',
                  row_version = row_version + 1,
                  updated_at = statement_timestamp()
              where id = @grant_id
              """
            : """
              update control.credential_ingestion_grants
              set state = @state,
                  row_version = row_version + 1,
                  updated_at = statement_timestamp()
              where id = @grant_id
              """;
        await using NpgsqlCommand command = transaction.CreateCommand(sql);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        if (includeFabricatedReservation)
        {
            command.Parameters.AddWithValue("reservation_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        }

        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(expectedSqlState, rejected.SqlState);
    }

    private static async Task AssertSecretReferenceFabricationRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid brokerAccountId)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update operations.broker_accounts
            set credential_reference = 'vault://test/forged',
                credential_state = 'ready',
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @broker_account_id
              and tenant_id = @tenant_id
            """);
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Contains(
            rejected.SqlState,
            new[]
            {
                PostgresErrorCodes.InsufficientPrivilege,
                PostgresErrorCodes.ObjectNotInPrerequisiteState
            });
    }

    private static async Task AssertControlAccountReactivationRejectedAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid brokerAccountId)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update operations.broker_accounts
            set state = 'active',
                credential_state = 'absent',
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @broker_account_id
              and tenant_id = @tenant_id
            """);
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, rejected.SqlState);
    }

    private static async Task UpdateCredentialGrantStateAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid grantId,
        string state)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.credential_ingestion_grants
            set state = @state,
                reservation_id = null,
                reserved_at = null,
                reservation_expires_at = null,
                cleanup_claim_token = null,
                cleanup_claimed_by = null,
                cleanup_claim_expires_at = null,
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @grant_id
            """);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static async Task<(string GrantState, string CredentialState)> ReadGrantAndAccountStateAsync(
        PostgresTestDatabase database,
        Guid grantId)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select ingestion_grant.state, account.credential_state
            from control.credential_ingestion_grants as ingestion_grant
            join operations.broker_accounts as account
              on account.tenant_id = ingestion_grant.tenant_id
             and account.id = ingestion_grant.broker_account_id
            where ingestion_grant.id = @grant_id
            """,
            connection);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<CredentialCleanupResult> ClaimAndCompleteCredentialCleanupAsync(
        PostgresDatabase database,
        Guid tenantId,
        Guid grantId,
        Guid brokerAccountId)
    {
        const string workerIdentity = "postgres-integration-expiry-worker";
        Guid cleanupToken = Guid.CreateVersion7();
        TenantExecutionContext context = CredentialCleanupContext(tenantId, grantId);
        long candidateVersion;
        await using (TenantPostgresTransaction candidateRead =
            await database.BeginTenantTransactionAsync(context))
        {
            await using NpgsqlCommand candidate = candidateRead.CreateCommand(
                """
                select row_version
                from control.credential_ingestion_grants
                where tenant_id = @tenant_id
                  and id = @grant_id
                  and broker_account_id = @broker_account_id
                  and state in ('active', 'reserved')
                  and expires_at <= clock_timestamp()
                """);
            candidate.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            candidate.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
            candidate.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, brokerAccountId);
            candidateVersion = Assert.IsType<long>(await candidate.ExecuteScalarAsync());
            await candidateRead.CommitAsync();
        }

        long claimedVersion;
        await using (TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context))
        {
            await using NpgsqlCommand claim = transaction.CreateCommand(
                """
                select grant_id, tenant_id, broker_account_id, grant_version,
                       cleanup_claim_expires_at
                from control.claim_credential_grant_cleanup(
                    @grant_id,
                    @cleanup_token,
                    @expected_version,
                    @claimed_by,
                    30)
                """);
            claim.Parameters.AddWithValue("cleanup_token", NpgsqlDbType.Uuid, cleanupToken);
            claim.Parameters.AddWithValue("claimed_by", NpgsqlDbType.Text, workerIdentity);
            claim.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, grantId);
            claim.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, candidateVersion);
            await using (NpgsqlDataReader reader = await claim.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(grantId, reader.GetGuid(0));
                Assert.Equal(tenantId, reader.GetGuid(1));
                Assert.Equal(brokerAccountId, reader.GetGuid(2));
                claimedVersion = reader.GetInt64(3);
                Assert.True(reader.GetFieldValue<DateTimeOffset>(4) > DateTimeOffset.UtcNow);
            }

            await transaction.CommitAsync();
        }

        return await ExecuteCredentialCleanupAsync(
            database,
            context,
            cleanupToken,
            claimedVersion,
            workerIdentity);
    }

    private static Task<CredentialCleanupResult> ReplayCredentialCleanupAsync(
        PostgresDatabase database,
        Guid tenantId,
        Guid grantId,
        CredentialCleanupResult completed) =>
        ExecuteCredentialCleanupAsync(
            database,
            CredentialCleanupContext(tenantId, grantId),
            completed.CleanupToken,
            completed.ExpectedVersion,
            completed.ClaimedBy);

    private static async Task<CredentialCleanupResult> ExecuteCredentialCleanupAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid cleanupToken,
        long expectedVersion,
        string claimedBy)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select grant_version, account_version, completed_at, next_state, replayed
            from control.complete_credential_grant_cleanup(
                @grant_id,
                @cleanup_token,
                @expected_version,
                @claimed_by,
                @audit_event_id,
                @outbox_message_id)
            """);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, context.CorrelationId);
        command.Parameters.AddWithValue("cleanup_token", NpgsqlDbType.Uuid, cleanupToken);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);
        command.Parameters.AddWithValue("claimed_by", NpgsqlDbType.Text, claimedBy);
        command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("outbox_message_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        CredentialCleanupResult result;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            result = new CredentialCleanupResult(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                cleanupToken,
                expectedVersion,
                claimedBy);
        }

        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertWorkerDirectBrokerAccountMutationRejectedAsync(
        PostgresDatabase database,
        Guid tenantId,
        Guid brokerAccountId,
        Guid correlationId)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                CredentialCleanupContext(tenantId, correlationId));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update operations.broker_accounts
            set credential_state = 'rotation_pending'
            where tenant_id = @tenant_id and id = @account_id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("account_id", NpgsqlDbType.Uuid, brokerAccountId);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    private static TenantExecutionContext CredentialCleanupContext(Guid tenantId, Guid grantId) =>
        new(
            tenantId,
            Guid.Parse("21e67e5a-daec-46eb-84af-f97244508616"),
            grantId);

    private static async Task<ExactStrategyProofFixture> SeedExactStrategyProofAsync(
        PostgresTestDatabase database,
        TenantExecutionContext ownerContext,
        Guid strategyId,
        Guid strategyVersionId,
        string strategyPackageSha256,
        DateTimeOffset observedAt)
    {
        Guid importJobId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        Guid verifierWorkloadId = Guid.CreateVersion7();
        const string signingKeyId = "foundation-strategy-verifier-key-1";
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        byte[] capabilitySha256 = SHA256.HashData(capability);
        try
        {
            await using (TenantPostgresTransaction transaction =
                await database.ControlApi.BeginTenantTransactionAsync(ownerContext))
            {
                await using NpgsqlCommand command = transaction.CreateCommand(
                    """
                    insert into control.strategy_import_jobs
                        (id, tenant_id, user_id, correlation_id, source_label,
                         capability_sha256, expires_at)
                    values
                        (@id, @tenant_id, @user_id, @correlation_id,
                         'foundation-operation-ea', @capability_sha256,
                         statement_timestamp() + interval '20 minutes')
                    """);
                command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, importJobId);
                command.Parameters.AddWithValue(
                    "tenant_id",
                    NpgsqlDbType.Uuid,
                    ownerContext.TenantId);
                command.Parameters.AddWithValue(
                    "user_id",
                    NpgsqlDbType.Uuid,
                    ownerContext.ActorId);
                command.Parameters.AddWithValue(
                    "correlation_id",
                    NpgsqlDbType.Uuid,
                    ownerContext.CorrelationId);
                command.Parameters.AddWithValue(
                    "capability_sha256",
                    NpgsqlDbType.Bytea,
                    capabilitySha256);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
                await transaction.CommitAsync();
            }

            byte[] source = Encoding.UTF8.GetBytes(
                "void OnTick(){ MqlTradeRequest r={}; MqlTradeResult x={}; OrderSend(r,x); }");
            using var corpus = new Mql5AnalyzedCorpus(
                new Mql5StaticInventoryAnalyzer().Analyze(
                    [new Mql5SourceDocument("Experts/Foundation.mq5", source)]),
                [new Mql5SourceDocument("Experts/Foundation.mq5", source.ToArray())]);
            using var persistenceRequest = new Mql5CorpusPersistenceRequest(importJobId, capability);
            Mql5CorpusPersistenceResult persisted = await new PostgresMql5CorpusStore(
                database.ConversionWorker).PersistAsync(persistenceRequest, corpus);

            string sourceReportSha256;
            DateTimeOffset corpusCreatedAt;
            await using (NpgsqlConnection connection =
                await database.Administrator.OpenConnectionAsync())
            await using (var readCorpus = new NpgsqlCommand(
                """
                select report_sha256, created_at
                from governance.strategy_source_corpora
                where tenant_id = @tenant_id and id = @corpus_id
                """,
                connection))
            {
                readCorpus.Parameters.AddWithValue(
                    "tenant_id",
                    NpgsqlDbType.Uuid,
                    ownerContext.TenantId);
                readCorpus.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, importJobId);
                await using NpgsqlDataReader reader = await readCorpus.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                sourceReportSha256 = reader.GetString(0);
                corpusCreatedAt = reader.GetFieldValue<DateTimeOffset>(1);
                Assert.False(await reader.ReadAsync());
            }

            await using (TenantPostgresTransaction transaction =
                await database.Application.BeginTenantTransactionAsync(ownerContext))
            {
                await using NpgsqlCommand command = transaction.CreateCommand(
                    """
                    insert into governance.strategy_versions
                        (id, tenant_id, strategy_id, version_number, package_sha256,
                         manifest_sha256, schema_sha256, provenance, evidence, state,
                         created_at, updated_at)
                    values
                        (@id, @tenant_id, @strategy_id, 1, @package_sha256,
                         @manifest_sha256, @schema_sha256,
                         '{"source":"verified-foundation-corpus"}'::jsonb,
                         '{}'::jsonb, 'simulation_review', @now, @now)
                    """);
                command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, strategyVersionId);
                command.Parameters.AddWithValue(
                    "tenant_id",
                    NpgsqlDbType.Uuid,
                    ownerContext.TenantId);
                command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyId);
                command.Parameters.AddWithValue(
                    "package_sha256",
                    NpgsqlDbType.Text,
                    strategyPackageSha256);
                command.Parameters.AddWithValue(
                    "manifest_sha256",
                    NpgsqlDbType.Text,
                    RandomHexDigest());
                command.Parameters.AddWithValue(
                    "schema_sha256",
                    NpgsqlDbType.Text,
                    RandomHexDigest());
                command.Parameters.AddWithValue(
                    "now",
                    NpgsqlDbType.TimestampTz,
                    observedAt.ToUniversalTime());
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
                await transaction.CommitAsync();
            }

            string compiledArtifactSha256 = RandomHexDigest();
            string compilerArtifactSha256 = RandomHexDigest();
            string parseProofSha256 = RandomHexDigest();
            string compileProofSha256 = RandomHexDigest();
            string semanticProofSha256 = RandomHexDigest();
            string parityProofSha256 = RandomHexDigest();
            string demoProofSha256 = RandomHexDigest();
            DateTimeOffset verifiedAt = corpusCreatedAt;
            byte[] evidence = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(new
            {
                contractVersion = 1,
                strategyVersionId,
                strategyPackageSha256,
                sourceCorpusId = importJobId,
                sourceCorpusSha256 = persisted.CorpusSha256,
                sourceManifestSha256 = persisted.ManifestSha256,
                sourceReportSha256,
                compiledArtifactSha256,
                compilerArtifactSha256,
                parseTypecheckProofSha256 = parseProofSha256,
                compileProofSha256,
                semanticConversionProofSha256 = semanticProofSha256,
                referenceParityProofSha256 = parityProofSha256,
                demoRuntimeProofSha256 = demoProofSha256,
                verifiedByWorkloadId = verifierWorkloadId,
                verificationSignatureAlgorithm = "ECDSA_P256_SHA256_DER",
                verificationSigningKeyId = signingKeyId,
                signatureCryptographicallyVerified = true,
                parsedAndTypeChecked = true,
                metaEditorCompileProven = true,
                semanticConversionProven = true,
                referenceParityProven = true,
                demoRuntimeProven = true
            }));
            byte[] signature;
            using (ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                signature = signingKey.SignData(
                    evidence,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }

            try
            {
                var verifierContext = new TenantExecutionContext(
                    ownerContext.TenantId,
                    verifierWorkloadId,
                    bindingId);
                await using (TenantPostgresTransaction transaction =
                    await database.StrategyVerifier.BeginTenantTransactionAsync(verifierContext))
                {
                    await using NpgsqlCommand command = transaction.CreateCommand(
                        """
                        select * from control.record_strategy_version_source_binding(
                            @binding_id, @strategy_version_id, @corpus_id,
                            @package_sha256, @corpus_sha256, @manifest_sha256,
                            @report_sha256, @compiled_sha256, @compiler_sha256,
                            @parse_sha256, @compile_sha256, @semantic_sha256,
                            @parity_sha256, @demo_sha256, @evidence_content,
                            @signature_bytes, @signing_key_id, @verified_at, @audit_id)
                        """);
                    command.Parameters.AddWithValue("binding_id", NpgsqlDbType.Uuid, bindingId);
                    command.Parameters.AddWithValue(
                        "strategy_version_id",
                        NpgsqlDbType.Uuid,
                        strategyVersionId);
                    command.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, importJobId);
                    command.Parameters.AddWithValue(
                        "package_sha256",
                        NpgsqlDbType.Text,
                        strategyPackageSha256);
                    command.Parameters.AddWithValue(
                        "corpus_sha256",
                        NpgsqlDbType.Text,
                        persisted.CorpusSha256);
                    command.Parameters.AddWithValue(
                        "manifest_sha256",
                        NpgsqlDbType.Text,
                        persisted.ManifestSha256);
                    command.Parameters.AddWithValue(
                        "report_sha256",
                        NpgsqlDbType.Text,
                        sourceReportSha256);
                    command.Parameters.AddWithValue(
                        "compiled_sha256",
                        NpgsqlDbType.Text,
                        compiledArtifactSha256);
                    command.Parameters.AddWithValue(
                        "compiler_sha256",
                        NpgsqlDbType.Text,
                        compilerArtifactSha256);
                    command.Parameters.AddWithValue(
                        "parse_sha256",
                        NpgsqlDbType.Text,
                        parseProofSha256);
                    command.Parameters.AddWithValue(
                        "compile_sha256",
                        NpgsqlDbType.Text,
                        compileProofSha256);
                    command.Parameters.AddWithValue(
                        "semantic_sha256",
                        NpgsqlDbType.Text,
                        semanticProofSha256);
                    command.Parameters.AddWithValue(
                        "parity_sha256",
                        NpgsqlDbType.Text,
                        parityProofSha256);
                    command.Parameters.AddWithValue(
                        "demo_sha256",
                        NpgsqlDbType.Text,
                        demoProofSha256);
                    command.Parameters.AddWithValue(
                        "evidence_content",
                        NpgsqlDbType.Bytea,
                        evidence);
                    command.Parameters.AddWithValue(
                        "signature_bytes",
                        NpgsqlDbType.Bytea,
                        signature);
                    command.Parameters.AddWithValue(
                        "signing_key_id",
                        NpgsqlDbType.Text,
                        signingKeyId);
                    command.Parameters.AddWithValue(
                        "verified_at",
                        NpgsqlDbType.TimestampTz,
                        verifiedAt);
                    command.Parameters.AddWithValue(
                        "audit_id",
                        NpgsqlDbType.Uuid,
                        Guid.CreateVersion7());
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(bindingId, reader.GetGuid(0));
                    Assert.False(await reader.ReadAsync());
                    await reader.DisposeAsync();
                    await transaction.CommitAsync();
                }

                var adminContext = new TenantExecutionContext(
                    ownerContext.TenantId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7());
                await using (TenantPostgresTransaction transaction =
                    await database.AdminBff.BeginTenantTransactionAsync(adminContext))
                {
                    await using NpgsqlCommand command = transaction.CreateCommand(
                        """
                        select * from control.promote_strategy_version_to_demo_approved(
                            @strategy_version_id, @binding_id, 0, @audit_id)
                        """);
                    command.Parameters.AddWithValue(
                        "strategy_version_id",
                        NpgsqlDbType.Uuid,
                        strategyVersionId);
                    command.Parameters.AddWithValue("binding_id", NpgsqlDbType.Uuid, bindingId);
                    command.Parameters.AddWithValue(
                        "audit_id",
                        NpgsqlDbType.Uuid,
                        Guid.CreateVersion7());
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal("demo_approved", reader.GetString(2));
                    Assert.False(await reader.ReadAsync());
                    await reader.DisposeAsync();
                    await transaction.CommitAsync();
                }

                return new ExactStrategyProofFixture(
                    bindingId,
                    Sha256Hex(evidence),
                    Sha256Hex(signature),
                    signingKeyId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(evidence);
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
            CryptographicOperations.ZeroMemory(capabilitySha256);
        }
    }

    private static async Task<BrokerOperationFixture> SeedConfirmedBrokerOperationFixtureAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        Guid rotateAccountId)
    {
        Guid deleteAccountId = Guid.CreateVersion7();
        Guid gatewayArtifactId = Guid.CreateVersion7();
        Guid strategyId = Guid.CreateVersion7();
        Guid strategyVersionId = Guid.CreateVersion7();
        Guid riskPolicyVersionId = Guid.CreateVersion7();
        Guid deploymentId = Guid.CreateVersion7();
        Guid workerNodeId = Guid.CreateVersion7();
        Guid workerAssignmentId = Guid.CreateVersion7();
        Guid sessionFamilyId = Guid.CreateVersion7();
        Guid deleteIdempotencyId = Guid.CreateVersion7();
        Guid rotateIdempotencyId = Guid.CreateVersion7();
        Guid deleteOperationId = Guid.CreateVersion7();
        Guid rotateOperationId = Guid.CreateVersion7();
        Guid deleteDispatchId = Guid.CreateVersion7();
        Guid rotateDispatchId = Guid.CreateVersion7();
        Guid deleteResultId = Guid.CreateVersion7();
        Guid rotateResultId = Guid.CreateVersion7();
        string gatewayDigest = RandomHexDigest();
        string strategyDigest = RandomHexDigest();
        string riskDigest = RandomHexDigest();
        string policySnapshotDigest = RandomHexDigest();
        string runtimeDigest = $"sha256:{RandomHexDigest()}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExactStrategyProofFixture strategyProof = await SeedExactStrategyProofAsync(
            database,
            context,
            strategyId,
            strategyVersionId,
            strategyDigest,
            now);

        var seedContext = new TenantExecutionContext(
            context.TenantId,
            context.ActorId,
            context.CorrelationId,
            sessionFamilyId);
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(seedContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into operations.broker_accounts
                (id, tenant_id, user_id, broker_id, server, masked_login,
                 binding_fingerprint, environment, credential_reference,
                 credential_state, state, created_at, updated_at)
            values
                (@delete_account_id, @tenant_id, @user_id, @delete_broker_id,
                 'demo-delete', '***2001', @delete_fingerprint, 'demo',
                 'vault://test/delete', 'deletion_pending', 'disabled', @now, @now);

            update operations.broker_accounts
            set credential_state = 'rotation_pending',
                row_version = row_version + 1,
                updated_at = @now
            where tenant_id = @tenant_id and id = @rotate_account_id;

            insert into governance.gateway_artifacts
                (id, vendor_name, vendor_version, sha256, signature_state,
                 quarantine_reference, provenance, licence_evidence,
                 sbom_reference, network_evidence, state, created_at, updated_at)
            values
                (@gateway_id, 'integration-vendor', '1.0', @gateway_digest, 'valid',
                 'quarantine://integration', '{"source":"integration"}'::jsonb,
                 '{"licence":"test"}'::jsonb, 'sbom://integration',
                 '{"network":"isolated"}'::jsonb, 'approved', @now, @now);

            insert into governance.risk_policy_versions
                (id, tenant_id, policy_id, version_number, normalized_policy,
                 policy_digest, signature_algorithm, signature_bytes,
                 signature_sha256, signing_key_id, state, effective_at,
                 created_at, updated_at)
            values
                (@risk_version_id, @tenant_id, @policy_id, 1, '{}'::jsonb,
                 @risk_digest, 'ECDSA_P256_SHA256_DER', @signature_bytes,
                 @signature_digest, 'integration-key', 'active', @now, @now, @now);

            insert into operations.deployments
                (id, tenant_id, user_id, broker_account_id, strategy_version_id,
                 strategy_source_binding_id,
                 strategy_verification_evidence_sha256,
                 strategy_verification_signature_sha256,
                 strategy_verification_signing_key_id,
                 risk_policy_version_id, risk_policy_digest, gateway_artifact_id,
                 gateway_digest, runtime_digest, strategy_package_digest, region,
                 dedicated_account, hedging_account, broker_hosted_stop_loss,
                 broker_hosted_take_profit, manual_or_external_trading_detected,
                 binding_evidence, binding_evidence_sha256,
                 creation_effective_policy_digest, creation_policy_version_watermark,
                 creation_policy_input_sha256, configuration_sha256, environment,
                 desired_state, observed_state, fence_generation, created_at, updated_at)
            values
                (@deployment_id, @tenant_id, @user_id, @rotate_account_id,
                 @strategy_version_id, @strategy_source_binding_id,
                 @strategy_verification_evidence_sha256,
                 @strategy_verification_signature_sha256,
                 @strategy_verification_signing_key_id,
                 @risk_version_id, @risk_digest, @gateway_id,
                 @gateway_digest, @runtime_digest, @strategy_digest, 'test-region',
                 true, true, true, true, false, '{}'::jsonb, @binding_digest,
                 @effective_policy_digest, @policy_watermark, @policy_input_digest,
                 @configuration_digest, 'demo', 'running', 'running', 1, @now, @now);

            insert into operations.worker_nodes
                (id, region, node_name, image_digest, state, capacity,
                 last_heartbeat_at, created_at, updated_at)
            values
                (@worker_node_id, 'test-region', @worker_node_name, @runtime_digest,
                 'ready', '{}'::jsonb, @now, @now, @now);

            insert into operations.worker_assignments
                (id, tenant_id, deployment_id, worker_node_id, supervisor_identity,
                 strategy_host_identity, gateway_host_identity, fence_generation,
                 runtime_digest, gateway_artifact_id, state, assigned_at,
                 lease_expires_at)
            values
                (@assignment_id, @tenant_id, @deployment_id, @worker_node_id,
                 'integration-supervisor', 'integration-strategy-host',
                 'integration-gateway-host', 1, @runtime_digest, @gateway_id,
                 'active', @now, @now + interval '10 minutes');

            insert into identity.user_session_families
                (id, tenant_id, user_id, device_id, current_token_hash, state,
                 expires_at, created_at, updated_at)
            values
                (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                 'active', @now + interval '10 minutes', @now, @now);

            insert into control.idempotency_records
                (id, tenant_id, actor_id, operation, idempotency_key,
                 request_sha256, state, created_at, expires_at)
            values
                (@delete_idempotency_id, @tenant_id, @user_id,
                 'broker_account.delete', @delete_idempotency_key,
                 @delete_request_digest, 'processing', @now, @now + interval '10 minutes'),
                (@rotate_idempotency_id, @tenant_id, @user_id,
                 'broker_account.credential_rotation', @rotate_idempotency_key,
                 @rotate_request_digest, 'processing', @now, @now + interval '10 minutes');

            insert into messaging.outbox_messages
                (id, tenant_id, message_type, aggregate_type, aggregate_id,
                 payload, payload_sha256, correlation_id, causation_id,
                 occurred_at, available_at, state, attempts)
            values
                (@delete_dispatch_id, @tenant_id, 'user_operation.dispatched.v1',
                 'user_operation', @delete_operation_id::text, '{}'::jsonb,
                 @delete_dispatch_payload_digest, @correlation_id,
                 @delete_operation_id, @now, @now, 'pending', 0),
                (@rotate_dispatch_id, @tenant_id, 'user_operation.dispatched.v1',
                 'user_operation', @rotate_operation_id::text, '{}'::jsonb,
                 @rotate_dispatch_payload_digest, @correlation_id,
                 @rotate_operation_id, @now, @now, 'pending', 0);

            insert into control.user_operations
                (id, tenant_id, user_id, session_family_id, operation_type,
                 target_type, target_id, state, idempotency_record_id,
                 expected_resource_version, submitted_resource_version,
                 requested_target_state, reason, correlation_id,
                 dispatch_message_id, dispatch_route_deployment_id,
                 dispatch_fence_generation, dispatch_worker_assignment_id,
                 dispatch_worker_instance_id, dispatch_target_binding_sha256,
                 dispatch_policy_snapshot_sha256, dispatch_attempts, dispatched_at,
                 created_at, updated_at)
            values
                (@delete_operation_id, @tenant_id, @user_id, @session_id,
                 'broker_account.delete', 'broker_account', @delete_account_id,
                 'propagating', @delete_idempotency_id, 0, 0, 'disabled:deleted',
                 'integration confirmed delete', @correlation_id,
                 @delete_dispatch_id, @deployment_id, 1, @assignment_id,
                 @worker_node_id, @delete_binding_digest, @policy_snapshot_digest,
                 1, @now, @now, @now),
                (@rotate_operation_id, @tenant_id, @user_id, @session_id,
                 'broker_account.credential_rotation', 'broker_account', @rotate_account_id,
                 'propagating', @rotate_idempotency_id, 1, 1, 'active:ready',
                 'integration confirmed rotation', @correlation_id,
                 @rotate_dispatch_id, @deployment_id, 1, @assignment_id,
                 @worker_node_id, @rotate_binding_digest, @policy_snapshot_digest,
                 1, @now, @now, @now);

            insert into operations.user_operation_results
                (id, tenant_id, result_id, operation_id, dispatch_message_id,
                 broker_account_id, route_deployment_id, generation,
                 worker_assignment_id, worker_instance_id, operation_type,
                 submitted_resource_version, requested_target_state,
                 policy_snapshot_sha256, proof_kind, outcome, broker_confirmed,
                 account_state, credential_state, evidence_sha256,
                 request_sha256, observed_at)
            values
                (@delete_result_id, @tenant_id, @delete_result_id,
                 @delete_operation_id, @delete_dispatch_id, @delete_account_id,
                 @deployment_id, 1, @assignment_id, @worker_node_id,
                 'broker_account.delete', 0, 'disabled:deleted', @policy_snapshot_digest,
                 'credential_deleted', 'succeeded', true, 'disabled', 'deleted',
                 @delete_evidence_digest, @delete_request_digest, @now),
                (@rotate_result_id, @tenant_id, @rotate_result_id,
                 @rotate_operation_id, @rotate_dispatch_id, @rotate_account_id,
                 @deployment_id, 1, @assignment_id, @worker_node_id,
                 'broker_account.credential_rotation', 1, 'active:ready', @policy_snapshot_digest,
                 'credential_rotated', 'succeeded', true, 'active', 'ready',
                 @rotate_evidence_digest, @rotate_request_digest, @now);
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, context.ActorId);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, context.CorrelationId);
        command.Parameters.AddWithValue("delete_account_id", NpgsqlDbType.Uuid, deleteAccountId);
        command.Parameters.AddWithValue("delete_broker_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("delete_fingerprint", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("rotate_account_id", NpgsqlDbType.Uuid, rotateAccountId);
        command.Parameters.AddWithValue("gateway_id", NpgsqlDbType.Uuid, gatewayArtifactId);
        command.Parameters.AddWithValue("gateway_digest", NpgsqlDbType.Text, gatewayDigest);
        command.Parameters.AddWithValue("strategy_version_id", NpgsqlDbType.Uuid, strategyVersionId);
        command.Parameters.AddWithValue("strategy_digest", NpgsqlDbType.Text, strategyDigest);
        command.Parameters.AddWithValue(
            "strategy_source_binding_id",
            NpgsqlDbType.Uuid,
            strategyProof.BindingId);
        command.Parameters.AddWithValue(
            "strategy_verification_evidence_sha256",
            NpgsqlDbType.Text,
            strategyProof.VerificationEvidenceSha256);
        command.Parameters.AddWithValue(
            "strategy_verification_signature_sha256",
            NpgsqlDbType.Text,
            strategyProof.VerificationSignatureSha256);
        command.Parameters.AddWithValue(
            "strategy_verification_signing_key_id",
            NpgsqlDbType.Text,
            strategyProof.SigningKeyId);
        command.Parameters.AddWithValue("risk_version_id", NpgsqlDbType.Uuid, riskPolicyVersionId);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("risk_digest", NpgsqlDbType.Text, riskDigest);
        command.Parameters.AddWithValue("signature_bytes", NpgsqlDbType.Bytea, new byte[64]);
        command.Parameters.AddWithValue("signature_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deploymentId);
        command.Parameters.AddWithValue("runtime_digest", NpgsqlDbType.Text, runtimeDigest);
        command.Parameters.AddWithValue("binding_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("effective_policy_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("policy_watermark", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("policy_input_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("configuration_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("worker_node_id", NpgsqlDbType.Uuid, workerNodeId);
        command.Parameters.AddWithValue("worker_node_name", NpgsqlDbType.Text, $"worker-{workerNodeId:N}");
        command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, workerAssignmentId);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionFamilyId);
        command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('t', 64));
        command.Parameters.AddWithValue("delete_idempotency_id", NpgsqlDbType.Uuid, deleteIdempotencyId);
        command.Parameters.AddWithValue("rotate_idempotency_id", NpgsqlDbType.Uuid, rotateIdempotencyId);
        command.Parameters.AddWithValue("delete_idempotency_key", NpgsqlDbType.Text, $"delete-{deleteOperationId:N}");
        command.Parameters.AddWithValue("rotate_idempotency_key", NpgsqlDbType.Text, $"rotate-{rotateOperationId:N}");
        command.Parameters.AddWithValue("delete_operation_id", NpgsqlDbType.Uuid, deleteOperationId);
        command.Parameters.AddWithValue("rotate_operation_id", NpgsqlDbType.Uuid, rotateOperationId);
        command.Parameters.AddWithValue("delete_dispatch_id", NpgsqlDbType.Uuid, deleteDispatchId);
        command.Parameters.AddWithValue("rotate_dispatch_id", NpgsqlDbType.Uuid, rotateDispatchId);
        command.Parameters.AddWithValue("delete_dispatch_payload_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("rotate_dispatch_payload_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("delete_binding_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("rotate_binding_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("policy_snapshot_digest", NpgsqlDbType.Text, policySnapshotDigest);
        command.Parameters.AddWithValue("delete_result_id", NpgsqlDbType.Uuid, deleteResultId);
        command.Parameters.AddWithValue("rotate_result_id", NpgsqlDbType.Uuid, rotateResultId);
        command.Parameters.AddWithValue("delete_evidence_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("rotate_evidence_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("delete_request_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("rotate_request_digest", NpgsqlDbType.Text, RandomHexDigest());
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        Assert.Equal(16, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        return new BrokerOperationFixture(
            deleteAccountId,
            deleteOperationId,
            deleteResultId,
            rotateOperationId,
            rotateResultId,
            context.CorrelationId);
    }

    private static async Task<bool> ApplyConfirmedBrokerOperationResultAsync(
        PostgresDatabase database,
        Guid tenantId,
        Guid operationId,
        Guid resultId,
        Guid correlationId)
    {
        var context = new TenantExecutionContext(
            tenantId,
            Guid.Parse("21e67e5a-daec-46eb-84af-f97244508616"),
            correlationId);
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select control.apply_confirmed_broker_operation_result(@tenant_id, @operation_id, @result_id)");
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
        command.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, resultId);
        bool applied = Assert.IsType<bool>(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return applied;
    }

    private static string RandomHexDigest()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string Sha256Hex(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static async Task<(string State, Guid? ReservationId)> ReadGrantReservationStateAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        Guid grantId)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select state, reservation_id from control.credential_ingestion_grants where id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, grantId);
        string state;
        Guid? reservationId;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            state = reader.GetString(0);
            reservationId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }

        await transaction.CommitAsync();
        return (state, reservationId);
    }

    private static AuditEvent NewAuditEvent(TenantExecutionContext context, DateTimeOffset occurredAt) =>
        AuditEvent.Create(
            context.TenantId,
            context.ActorId,
            AuditCategory.Operations,
            "deployment.close_only.requested",
            "deployment",
            Guid.CreateVersion7().ToString("D"),
            AuditOutcome.Accepted,
            "integration test",
            context.CorrelationId,
            null,
            new { redacted = true },
            occurredAt);

    private static OutboxMessage NewOutboxMessage(TenantExecutionContext context, DateTimeOffset occurredAt) =>
        OutboxMessage.Create(
            context.TenantId,
            "deployment.close_only.requested.v1",
            "deployment",
            Guid.CreateVersion7().ToString("D"),
            new { desiredState = "close_only" },
            context.CorrelationId,
            null,
            occurredAt);

    private static async Task<long> ScalarInt64Async(
        TenantPostgresTransaction transaction,
        string sql)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertNoDomainRowsAsync(NpgsqlConnection connection)
    {
        var tables = new List<(string Schema, string Table)>();
        await using (var catalog = new NpgsqlCommand(
            """
            select namespace.nspname, relation.relname
            from pg_class as relation
            join pg_namespace as namespace on namespace.oid = relation.relnamespace
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p')
              and not (namespace.nspname = 'control' and relation.relname = 'schema_migrations')
            order by namespace.nspname, relation.relname
            """,
            connection))
        await using (NpgsqlDataReader reader = await catalog.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.NotEmpty(tables);
        using var quoter = new NpgsqlCommandBuilder();
        foreach ((string schema, string table) in tables)
        {
            string qualifiedName = $"{quoter.QuoteIdentifier(schema)}.{quoter.QuoteIdentifier(table)}";
            await using var count = new NpgsqlCommand($"select count(*) from {qualifiedName}", connection);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }
    }

    private static async Task AssertEvidenceCountsAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        long expectedAudit,
        long expectedOutbox)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        Assert.Equal(expectedAudit, await ScalarInt64Async(transaction, "select count(*) from audit.audit_events"));
        Assert.Equal(expectedOutbox, await ScalarInt64Async(transaction, "select count(*) from messaging.outbox_messages"));
        await transaction.CommitAsync();
    }

    private sealed record SeededCredentialGrant(
        Guid GrantId,
        Guid BrokerAccountId,
        string Origin,
        string BearerHash,
        string NonceHash);

    private sealed record CredentialBoundaryFixture(
        Guid OtherUserId,
        Guid CreateAccountId,
        Guid RotateAccountId,
        Guid UnusedValidAccountId,
        Guid LiveAccountId,
        Guid DisabledAccountId,
        Guid DeletedAccountId);

    private sealed record CredentialCleanupResult(
        long GrantVersion,
        long AccountVersion,
        DateTimeOffset CompletedAt,
        string NextState,
        bool Replayed,
        Guid CleanupToken,
        long ExpectedVersion,
        string ClaimedBy);

    private sealed record ExactStrategyProofFixture(
        Guid BindingId,
        string VerificationEvidenceSha256,
        string VerificationSignatureSha256,
        string SigningKeyId);

    private sealed record BrokerOperationFixture(
        Guid DeleteAccountId,
        Guid DeleteOperationId,
        Guid DeleteResultId,
        Guid RotateOperationId,
        Guid RotateResultId,
        Guid CorrelationId);
}
