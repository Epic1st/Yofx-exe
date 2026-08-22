using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class StrategyImportPostgresTests(PostgresContainerFixture postgres)
{
    private const string SuppliedCorpusSha256 =
        "8052d74d395516aef01f221bf1a663b775ed02ccccbfa0476704d52112ee43b6";

    private readonly PostgresContainerFixture _postgres = postgres;

    [PostgresFact]
    public async Task ProductionStorePersistsReplaysAndRejectsTamperingWithoutPartialEvidence()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        byte[] wrongCapability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "representative-ea");
            using Mql5AnalyzedCorpus corpus = CreateRepresentativeCorpus();
            Assert.Contains(corpus.Manifest.Files, file => file.Includes.Count > 0);
            Assert.Contains(corpus.Manifest.Files, file => file.Features.Count > 0);
            Assert.All(corpus.Manifest.Files, file => Assert.NotEmpty(file.Findings));
            Assert.Contains(
                corpus.Manifest.Files.SelectMany(file => file.Features),
                feature => feature.Support == Mql5FeatureSupport.ReviewRequired);
            Assert.Contains(
                corpus.Manifest.Files.SelectMany(file => file.Findings),
                finding => finding.Severity == Mql5FindingSeverity.Warning);

            await AssertSessionUserGuardRejectsAdministratorAsync(
                database,
                binding.ImportJobId,
                capability);

            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using (var wrongRequest = new Mql5CorpusPersistenceRequest(
                       binding.ImportJobId,
                       wrongCapability))
            {
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => store.PersistAsync(wrongRequest, corpus));
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, rejected.SqlState);
            }

            ImportEvidenceSnapshot rejectedSnapshot = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("active", rejectedSnapshot.State);
            Assert.Equal(0, rejectedSnapshot.CorpusCount);
            Assert.Equal(0, rejectedSnapshot.FileCount);
            Assert.Equal(0, rejectedSnapshot.AuditCount);
            Assert.Equal(0, rejectedSnapshot.OutboxCount);

            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);
            Mql5CorpusPersistenceResult persisted = await store.PersistAsync(request, corpus);
            Assert.False(persisted.Replayed);
            Assert.Equal(binding.ImportJobId, persisted.ImportId);
            Assert.Equal(corpus.Manifest.CorpusSha256, persisted.CorpusSha256);
            Assert.Equal(corpus.Manifest.FileCount, persisted.FileCount);

            Mql5CorpusPersistenceResult replayed = await store.PersistAsync(request, corpus);
            Assert.True(replayed.Replayed);
            Assert.Equal(persisted, replayed with { Replayed = false });

            ImportEvidenceSnapshot completed = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("consumed", completed.State);
            Assert.Equal(binding.TenantId, completed.TenantId);
            Assert.Equal(binding.UserId, completed.UserId);
            Assert.Equal(binding.CorrelationId, completed.CorrelationId);
            Assert.True(completed.CapabilityDigestMatches);
            Assert.Equal(corpus.Manifest.CorpusSha256, completed.CorpusSha256);
            Assert.Equal(corpus.Manifest.FileCount, completed.PersistedFileCount);
            Assert.Equal(corpus.Manifest.TotalBytes, completed.TotalBytes);
            Assert.Equal(1, completed.CorpusCount);
            Assert.Equal(corpus.Manifest.FileCount, completed.FileCount);
            Assert.Equal(1, completed.AuditCount);
            Assert.Equal(1, completed.OutboxCount);
            Assert.True(completed.HasResolvedLocalInclude);
            Assert.True(completed.HasReviewRequiredFeature);
            Assert.True(completed.HasWarningFinding);
            Assert.True(completed.HasOnTickEntrypoint);
            Assert.True(completed.HasExactStaticVerification);
            Assert.True(completed.HasSafeAuditPayload);
            Assert.True(completed.HasSafeOutboxPayload);

            using Mql5AnalyzedCorpus tamperedCorpus = CreateRepresentativeCorpus(
                additionalExpertSource: "\n// immutable replay tamper\ninput int Changed = 1;\n");
            InvalidOperationException replayMismatch = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.PersistAsync(request, tamperedCorpus));
            Assert.Contains("different immutable corpus evidence", replayMismatch.Message);

            ImportEvidenceSnapshot afterTamper = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal(completed, afterTamper);

            await AssertFailedMultiFilePersistenceRollsBackAsync(database);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
            CryptographicOperations.ZeroMemory(wrongCapability);
        }
    }

    [PostgresFact]
    public async Task ConcurrentExactPersistenceProducesOneCommitAndOneReplay()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        Task<Mql5CorpusPersistenceResult>? firstPersistence = null;
        Task<Mql5CorpusPersistenceResult>? secondPersistence = null;
        await using NpgsqlConnection lockConnection =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction lockTransaction =
            await lockConnection.BeginTransactionAsync();
        bool lockReleased = false;
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "concurrent-replay");
            await AcquireImportPersistenceLockAsync(
                lockConnection,
                lockTransaction,
                binding);

            using Mql5AnalyzedCorpus firstCorpus = CreateRepresentativeCorpus();
            using Mql5AnalyzedCorpus secondCorpus = CreateRepresentativeCorpus();
            using var firstRequest = new Mql5CorpusPersistenceRequest(
                binding.ImportJobId,
                capability);
            using var secondRequest = new Mql5CorpusPersistenceRequest(
                binding.ImportJobId,
                capability);
            var firstStore = new PostgresMql5CorpusStore(database.ConversionWorker);
            var secondStore = new PostgresMql5CorpusStore(database.ConversionWorker);

            firstPersistence = firstStore.PersistAsync(firstRequest, firstCorpus);
            await WaitForImportStateAsync(database, binding.ImportJobId, "reserved");
            await WaitForBlockedImportWorkersAsync(database, expectedCount: 1);

            secondPersistence = secondStore.PersistAsync(secondRequest, secondCorpus);
            int blockedWorkers = await WaitForBlockedImportWorkersOrCompletionAsync(
                database,
                secondPersistence,
                expectedCount: 2);
            Assert.False(
                secondPersistence.IsCompleted,
                "A concurrent exact replay must wait for the in-flight persistence outcome.");
            Assert.True(blockedWorkers >= 2);

            await lockTransaction.CommitAsync();
            lockReleased = true;
            Mql5CorpusPersistenceResult[] results = await Task.WhenAll(
                firstPersistence,
                secondPersistence);

            Assert.Single(results, result => !result.Replayed);
            Assert.Single(results, result => result.Replayed);
            Mql5CorpusPersistenceResult committed = Assert.Single(
                results,
                result => !result.Replayed);
            Mql5CorpusPersistenceResult replayed = Assert.Single(
                results,
                result => result.Replayed);
            Assert.Equal(committed, replayed with { Replayed = false });

            ImportEvidenceSnapshot evidence = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("consumed", evidence.State);
            Assert.Equal(1, evidence.CorpusCount);
            Assert.Equal(firstCorpus.Manifest.FileCount, evidence.FileCount);
            Assert.Equal(1, evidence.AuditCount);
            Assert.Equal(1, evidence.OutboxCount);
        }
        finally
        {
            if (!lockReleased)
            {
                await lockTransaction.RollbackAsync();
            }

            await ObservePersistenceAsync(firstPersistence);
            await ObservePersistenceAsync(secondPersistence);
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    [PostgresFact]
    public async Task ControlRoleRejectsCrossAuthorityImportCreationAndRevocation()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "authority-bound-import");
            Guid otherUserId = Guid.CreateVersion7();
            await SeedAdditionalActiveUserAsync(database, binding, otherUserId);

            var ownerContext = new TenantExecutionContext(
                binding.TenantId,
                binding.UserId,
                binding.CorrelationId);
            await AssertImportCreationRejectedAsync(
                database,
                ownerContext,
                binding.TenantId,
                otherUserId,
                binding.CorrelationId,
                TimeSpan.FromMinutes(20),
                capability,
                PostgresErrorCodes.InsufficientPrivilege);
            await AssertImportCreationRejectedAsync(
                database,
                ownerContext,
                binding.TenantId,
                binding.UserId,
                Guid.CreateVersion7(),
                TimeSpan.FromMinutes(20),
                capability,
                PostgresErrorCodes.InsufficientPrivilege);
            await AssertImportCreationRejectedAsync(
                database,
                ownerContext,
                Guid.CreateVersion7(),
                binding.UserId,
                binding.CorrelationId,
                TimeSpan.FromMinutes(20),
                capability,
                PostgresErrorCodes.InsufficientPrivilege);
            await AssertImportCreationRejectedAsync(
                database,
                ownerContext,
                binding.TenantId,
                binding.UserId,
                binding.CorrelationId,
                TimeSpan.FromMinutes(31),
                capability,
                PostgresErrorCodes.InsufficientPrivilege);

            var otherActorContext = new TenantExecutionContext(
                binding.TenantId,
                otherUserId,
                Guid.CreateVersion7());
            await AssertImportStateUpdateRejectedAsync(
                database,
                otherActorContext,
                binding.ImportJobId,
                "revoked",
                PostgresErrorCodes.InsufficientPrivilege,
                allowRowLevelSecurityDenial: true);

            await UpdateImportStateAsync(
                database,
                ownerContext,
                binding.ImportJobId,
                "revoked");
            Assert.Equal(
                "revoked",
                await ReadImportStateAsAdministratorAsync(database, binding.ImportJobId));

            await AssertImportStateUpdateRejectedAsync(
                database,
                ownerContext,
                binding.ImportJobId,
                "active",
                PostgresErrorCodes.ObjectNotInPrerequisiteState);
            Assert.Equal(
                "revoked",
                await ReadImportStateAsAdministratorAsync(database, binding.ImportJobId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    [PostgresFact]
    public async Task SuppliedMql5CorpusPersistsWithExactInventoryEvidence()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "supplied-mq5-corpus");
            string corpusRoot = FindSuppliedCorpusRoot();
            var inventory = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
            using Mql5AnalyzedCorpus corpus = await inventory.AnalyzeDirectoryForPersistenceAsync(corpusRoot);

            Assert.Equal(198, corpus.Manifest.FileCount);
            Assert.Equal(SuppliedCorpusSha256, corpus.Manifest.CorpusSha256);
            Assert.All(
                corpus.Manifest.Files,
                file => Assert.True(file.Verification.StaticInventoryCompleted));
            Assert.All(
                corpus.Manifest.Files,
                file => Assert.False(file.Verification.DemoRuntimeProven));

            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);
            Mql5CorpusPersistenceResult result = await store.PersistAsync(request, corpus);

            Assert.False(result.Replayed);
            Assert.Equal(198, result.FileCount);
            Assert.Equal(SuppliedCorpusSha256, result.CorpusSha256);

            ImportEvidenceSnapshot evidence = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("consumed", evidence.State);
            Assert.Equal(1, evidence.CorpusCount);
            Assert.Equal(198, evidence.FileCount);
            Assert.Equal(198, evidence.PersistedFileCount);
            Assert.Equal(corpus.Manifest.TotalBytes, evidence.TotalBytes);
            Assert.Equal(1, evidence.AuditCount);
            Assert.Equal(1, evidence.OutboxCount);
            Assert.True(evidence.HasExactStaticVerification);
            Assert.True(evidence.HasSafeAuditPayload);
            Assert.True(evidence.HasSafeOutboxPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private static async Task AssertFailedMultiFilePersistenceRollsBackAsync(
        PostgresTestDatabase database)
    {
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "partial-rollback");
            byte[] firstSource = Encoding.UTF8.GetBytes("void OnTick() { }\n");
            byte[] secondSource = Encoding.UTF8.GetBytes("input int HeaderValue = 1;\n");
            string oversizedPath = new string('z', 1_997) + ".mqh";
            var documents = new[]
            {
                new Mql5SourceDocument("a.mq5", firstSource),
                new Mql5SourceDocument(oversizedPath, secondSource)
            };
            var analyzer = new Mql5StaticInventoryAnalyzer();
            using var corpus = new Mql5AnalyzedCorpus(analyzer.Analyze(documents), documents);
            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);

            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => store.PersistAsync(request, corpus));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);

            ImportEvidenceSnapshot evidence = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("reserved", evidence.State);
            Assert.Equal(0, evidence.CorpusCount);
            Assert.Equal(0, evidence.FileCount);
            Assert.Equal(0, evidence.AuditCount);
            Assert.Equal(0, evidence.OutboxCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private static async Task AcquireImportPersistenceLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportBinding binding)
    {
        await using var command = new NpgsqlCommand(
            """
            select pg_catalog.pg_advisory_xact_lock(
                pg_catalog.hashtextextended(
                    'yo4x:strategy-import:' || @tenant_id::text || ':' || @job_id::text,
                    0))
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, binding.ImportJobId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAdditionalActiveUserAsync(
        PostgresTestDatabase database,
        ImportBinding binding,
        Guid userId)
    {
        var context = new TenantExecutionContext(
            binding.TenantId,
            binding.UserId,
            binding.CorrelationId);
        await using var transaction = await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state, email_verified_at)
            values
                (@user_id, @tenant_id, @email, 'active', statement_timestamp())
            """);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
        command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"user-{userId:N}@example.test");
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task AssertImportCreationRejectedAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        Guid tenantId,
        Guid userId,
        Guid correlationId,
        TimeSpan expiryInterval,
        byte[] capability,
        string expectedSqlState)
    {
        byte[] digest = SHA256.HashData(capability);
        try
        {
            await using var transaction = await database.ControlApi.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                insert into control.strategy_import_jobs
                    (id, tenant_id, user_id, correlation_id, source_label,
                     capability_sha256, expires_at)
                values
                    (@job_id, @tenant_id, @user_id, @correlation_id, @source_label,
                     @capability_sha256, statement_timestamp() + @expiry_interval)
                """);
            command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
            command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, correlationId);
            command.Parameters.AddWithValue(
                "source_label",
                NpgsqlDbType.Text,
                $"negative-{Guid.CreateVersion7():N}");
            command.Parameters.AddWithValue("capability_sha256", NpgsqlDbType.Bytea, digest);
            command.Parameters.AddWithValue(
                "expiry_interval",
                NpgsqlDbType.Interval,
                expiryInterval);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                async () => await command.ExecuteNonQueryAsync());
            Assert.Equal(expectedSqlState, rejected.SqlState);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static async Task AssertImportStateUpdateRejectedAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        Guid importJobId,
        string state,
        string expectedSqlState,
        bool allowRowLevelSecurityDenial = false)
    {
        await using var transaction = await database.ControlApi.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.strategy_import_jobs
            set state = @state,
                reservation_id = null,
                reservation_expires_at = null,
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @job_id
            """);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
        try
        {
            int affected = await command.ExecuteNonQueryAsync();
            Assert.True(
                allowRowLevelSecurityDenial && affected == 0,
                "The strategy import state mutation unexpectedly reached a row.");
        }
        catch (PostgresException rejected)
        {
            Assert.Equal(expectedSqlState, rejected.SqlState);
        }
    }

    private static async Task UpdateImportStateAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        Guid importJobId,
        string state)
    {
        await using var transaction = await database.ControlApi.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.strategy_import_jobs
            set state = @state,
                reservation_id = null,
                reservation_expires_at = null,
                row_version = row_version + 1,
                updated_at = statement_timestamp()
            where id = @job_id
            """);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static async Task<string> ReadImportStateAsAdministratorAsync(
        PostgresTestDatabase database,
        Guid importJobId)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select state from control.strategy_import_jobs where id = @job_id",
            connection);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The strategy import job was not found."));
    }

    private static async Task WaitForImportStateAsync(
        PostgresTestDatabase database,
        Guid importJobId,
        string expectedState)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using NpgsqlConnection connection =
                await database.Administrator.OpenConnectionAsync(timeout.Token);
            await using var command = new NpgsqlCommand(
                "select state from control.strategy_import_jobs where id = @job_id",
                connection);
            command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
            string? state = (string?)await command.ExecuteScalarAsync(timeout.Token);
            if (string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    private static async Task WaitForBlockedImportWorkersAsync(
        PostgresTestDatabase database,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await CountBlockedImportWorkersAsync(database, timeout.Token) < expectedCount)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    private static async Task<int> WaitForBlockedImportWorkersOrCompletionAsync(
        PostgresTestDatabase database,
        Task persistence,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!persistence.IsCompleted)
        {
            int blockedWorkers = await CountBlockedImportWorkersAsync(database, timeout.Token);
            if (blockedWorkers >= expectedCount)
            {
                return blockedWorkers;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }

        return await CountBlockedImportWorkersAsync(database, timeout.Token);
    }

    private static async Task<int> CountBlockedImportWorkersAsync(
        PostgresTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select count(*)::integer
            from pg_catalog.pg_stat_activity
            where datname = pg_catalog.current_database()
              and usename = 'yo4x_conversion_worker'
              and wait_event_type = 'Lock'
              and wait_event = 'advisory'
            """,
            connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("PostgreSQL did not return an activity count."));
    }

    private static async Task ObservePersistenceAsync(Task? persistence)
    {
        if (persistence is null)
        {
            return;
        }

        try
        {
            await persistence;
        }
        catch
        {
            // The primary assertion reports the concurrency failure. This observes
            // cleanup faults without masking it or leaving an unobserved task.
        }
    }

    private static Mql5AnalyzedCorpus CreateRepresentativeCorpus(
        string additionalExpertSource = "")
    {
        byte[] expert = Encoding.UTF8.GetBytes(
            """
            #include "lib/Signals.mqh"
            input double Lots = 0.10;

            void OnTick()
            {
                MqlTradeRequest request = {};
                MqlTradeResult result = {};
                OrderSend(request, result);
                TimeCurrent();
            }
            """ + additionalExpertSource);
        byte[] header = Encoding.UTF8.GetBytes(
            """
            input int SignalPeriod = 14;
            bool HasSignal()
            {
                return Bars(_Symbol, PERIOD_CURRENT) > SignalPeriod;
            }
            """);
        var documents = new[]
        {
            new Mql5SourceDocument("Experts/Representative.mq5", expert),
            new Mql5SourceDocument("Experts/lib/Signals.mqh", header)
        };
        var analyzer = new Mql5StaticInventoryAnalyzer();
        return new Mql5AnalyzedCorpus(analyzer.Analyze(documents), documents);
    }

    private static async Task<ImportBinding> SeedImportJobAsync(
        PostgresTestDatabase database,
        byte[] capability,
        string sourceLabel)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid correlationId = Guid.CreateVersion7();
        Guid importJobId = Guid.CreateVersion7();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        byte[] capabilityDigest = SHA256.HashData(capability);
        try
        {
            var context = new TenantExecutionContext(tenantId, userId, correlationId);
            await using var transaction = await database.Application.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                insert into identity.tenants (id, slug, display_name)
                values (@tenant_id, @slug, @display_name);

                insert into identity.user_identities
                    (id, tenant_id, normalized_email, security_state,
                     email_verified_at, created_at, updated_at)
                values
                    (@user_id, @tenant_id, @email, 'active',
                     @created_at, @created_at, @created_at);
                """);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
            command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, correlationId);
            command.Parameters.AddWithValue("slug", NpgsqlDbType.Text, $"tenant-{tenantId:N}");
            command.Parameters.AddWithValue("display_name", NpgsqlDbType.Text, "Strategy import test tenant");
            command.Parameters.AddWithValue("email", NpgsqlDbType.Text, $"user-{userId:N}@example.test");
            command.Parameters.AddWithValue(
                "created_at",
                NpgsqlDbType.TimestampTz,
                createdAt.ToUniversalTime());
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            await using var control = await database.ControlApi.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand createJob = control.CreateCommand(
                """
                insert into control.strategy_import_jobs
                    (id, tenant_id, user_id, correlation_id, source_label,
                     capability_sha256, expires_at)
                values
                    (@job_id, @tenant_id, @user_id, @correlation_id, @source_label,
                     @capability_sha256, statement_timestamp() + interval '20 minutes')
                """);
            createJob.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
            createJob.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            createJob.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
            createJob.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, correlationId);
            createJob.Parameters.AddWithValue("source_label", NpgsqlDbType.Text, sourceLabel);
            createJob.Parameters.AddWithValue(
                "capability_sha256",
                NpgsqlDbType.Bytea,
                capabilityDigest);
            await createJob.ExecuteNonQueryAsync();
            await control.CommitAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capabilityDigest);
        }

        return new ImportBinding(tenantId, userId, correlationId, importJobId);
    }

    private static async Task AssertSessionUserGuardRejectsAdministratorAsync(
        PostgresTestDatabase database,
        Guid importJobId,
        byte[] capability)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select * from control.acquire_strategy_import_job(@job_id, @capability)",
            connection);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, importJobId);
        command.Parameters.AddWithValue("capability", NpgsqlDbType.Bytea, capability);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, rejected.SqlState);
    }

    private static async Task<ImportEvidenceSnapshot> ReadImportEvidenceAsync(
        PostgresTestDatabase database,
        ImportBinding binding,
        byte[] capability)
    {
        byte[] capabilityDigest = SHA256.HashData(capability);
        try
        {
            await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                select
                    job.state,
                    job.tenant_id,
                    job.user_id,
                    job.correlation_id,
                    job.capability_sha256 = @capability_sha256,
                    job.corpus_sha256,
                    job.file_count,
                    job.total_bytes,
                    (select count(*) from governance.strategy_source_corpora as corpus
                     where corpus.import_job_id = job.id),
                    (select count(*) from governance.strategy_source_files as file
                     where file.import_job_id = job.id),
                    (select count(*) from audit.audit_events as audit_event
                     where audit_event.target_type = 'strategy_source_corpus'
                       and audit_event.target_id = job.id::text),
                    (select count(*) from messaging.outbox_messages as message
                     where message.aggregate_type = 'strategy_source_corpus'
                       and message.aggregate_id = job.id::text),
                    exists
                    (
                        select 1
                        from governance.strategy_source_files as file
                        where file.import_job_id = job.id
                          and file.includes @>
                            '[{"kind":"local","resolution":"resolvedInCorpus"}]'::jsonb
                    ),
                    exists
                    (
                        select 1
                        from governance.strategy_source_files as file
                        where file.import_job_id = job.id
                          and file.features @> '[{"support":"reviewRequired"}]'::jsonb
                    ),
                    exists
                    (
                        select 1
                        from governance.strategy_source_files as file
                        where file.import_job_id = job.id
                          and file.findings @> '[{"severity":"warning"}]'::jsonb
                    ),
                    exists
                    (
                        select 1
                        from governance.strategy_source_files as file
                        where file.import_job_id = job.id
                          and 'OnTick' = any(file.entrypoints)
                    ),
                    coalesce
                    (
                        (select bool_and(file.verification =
                            '{"demoRuntimeProven":false,"metaEditorCompileProven":false,"parsedAndTypeChecked":false,"referenceParityProven":false,"semanticConversionProven":false,"staticInventoryCompleted":true}'::jsonb)
                         from governance.strategy_source_files as file
                         where file.import_job_id = job.id),
                        false
                    ),
                    exists
                    (
                        select 1
                        from audit.audit_events as audit_event
                        where audit_event.target_type = 'strategy_source_corpus'
                          and audit_event.target_id = job.id::text
                          and audit_event.payload = pg_catalog.jsonb_build_object(
                              'importJobId', job.id::text,
                              'verification', 'static-inventory-only')
                    ),
                    exists
                    (
                        select 1
                        from messaging.outbox_messages as message
                        where message.aggregate_type = 'strategy_source_corpus'
                          and message.aggregate_id = job.id::text
                          and message.payload = pg_catalog.jsonb_build_object(
                              'importJobId', job.id::text,
                              'verification', 'static-inventory-only')
                    )
                from control.strategy_import_jobs as job
                where job.id = @job_id
                  and job.tenant_id = @tenant_id
                """,
                connection);
            command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, binding.ImportJobId);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            command.Parameters.AddWithValue("capability_sha256", NpgsqlDbType.Bytea, capabilityDigest);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var result = new ImportEvidenceSnapshot(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.GetBoolean(12),
                reader.GetBoolean(13),
                reader.GetBoolean(14),
                reader.GetBoolean(15),
                reader.GetBoolean(16),
                reader.GetBoolean(17),
                reader.GetBoolean(18));
            Assert.False(await reader.ReadAsync());
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capabilityDigest);
        }
    }

    private static string FindSuppliedCorpusRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string solution = Path.Combine(current.FullName, "YO4X.sln");
            string corpus = Path.Combine(current.FullName, "Testing", "Mq5");
            if (File.Exists(solution) && Directory.Exists(corpus))
            {
                return corpus;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The supplied Testing/Mq5 corpus was not found.");
    }

    private sealed record ImportBinding(
        Guid TenantId,
        Guid UserId,
        Guid CorrelationId,
        Guid ImportJobId);

    private sealed record ImportEvidenceSnapshot(
        string State,
        Guid TenantId,
        Guid UserId,
        Guid CorrelationId,
        bool CapabilityDigestMatches,
        string? CorpusSha256,
        int? PersistedFileCount,
        long? TotalBytes,
        long CorpusCount,
        long FileCount,
        long AuditCount,
        long OutboxCount,
        bool HasResolvedLocalInclude,
        bool HasReviewRequiredFeature,
        bool HasWarningFinding,
        bool HasOnTickEntrypoint,
        bool HasExactStaticVerification,
        bool HasSafeAuditPayload,
        bool HasSafeOutboxPayload);
}
