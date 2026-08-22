using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Conversion.Worker;
using YO4X.Persistence.Postgres;
using YO4X.Risk;
using YO4X.Runtime.Contracts;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Postgres;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class BrokerCommandAuthorizationPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task SignedVerificationCapabilityIsRequiredForPromotionAndStart()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);

        await AssertRawAdminPromotionRejectedAsync(database, fixture);
        await AssertWrongVerificationMetadataRejectedAsync(database, fixture);
        await RecordVerificationAsync(database, fixture);
        await AssertWrongBindingPromotionRejectedAsync(database, fixture);
        await PromoteAsync(database, fixture);
        await SeedRuntimeAuthorityAsync(database, fixture, desiredState: "ready");

        await SuspendStrategyAsync(database, fixture);
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
        await using NpgsqlCommand start = transaction.CreateCommand(
            """
            update operations.deployments
            set desired_state = 'starting', fence_generation = 1,
                row_version = row_version + 1, updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @deployment_id
            """);
        AddUuid(start, "tenant_id", fixture.TenantId);
        AddUuid(start, "deployment_id", fixture.DeploymentId);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await start.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    [PostgresFact]
    public async Task DurableAuthorizationDispatchAndReconciliationAreAtomicAndFailClosed()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        SignedExecutionLease lease = leaseFixture.Lease;

        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(fixture, lease);
        var store = new PostgresBrokerCommandStore(
            database.TradeAuthorizer,
            database.GatewayRuntime,
            new P256ExecutionLeaseTrustVerifier(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
                {
                    [lease.SigningKeyId] = leaseFixture.SubjectPublicKeyInfo
                }));
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization = await store.AuthorizeAsync(
            authorizerContext,
            request);
        Assert.False(authorization.Replayed);

        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        Guid dispatchClaimToken = Guid.CreateVersion7();
        var dispatchReference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            ExecutionLeaseEnvelopeDigest.Sha256(lease));
        YO4X.Trading.Application.BrokerCommandDispatchClaim claim =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                dispatchReference,
                dispatchClaimToken,
                Guid.CreateVersion7());
        Assert.False(claim.Replayed);

        YO4X.Trading.Application.BrokerCommandDispatchClaim claimReplay =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                dispatchReference,
                dispatchClaimToken,
                Guid.CreateVersion7());
        Assert.True(claimReplay.Replayed);

        var unknown = new GatewaySendResult(
            GatewayCommandDisposition.Unknown,
            "transport_outcome_unknown",
            "request-1",
            null,
            null,
            UtcNow());
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt submission =
            await lifecycle.RecordSubmissionAsync(
                gatewayContext,
                claim,
                unknown,
                Guid.CreateVersion7());
        Assert.Equal("unknown", submission.State);

        BrokerCommandAuthorizationReceipt replay = await store.AuthorizeAsync(
            authorizerContext,
            request);
        Assert.True(replay.Replayed);
        Assert.Equal(authorization.AuthorizationSha256, replay.AuthorizationSha256);

        Guid reconciliationClaim = Guid.CreateVersion7();
        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliation =
            await lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                reconciliationClaim,
                Guid.CreateVersion7());
        Assert.True(reconciliation.ClaimExpiresAtUtc > reconciliation.StartedAtUtc);

        DateTimeOffset unavailableAt = UtcNow();
        var unavailableObservation =
            new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
                null,
                Digest("reconciliation-attempt-unavailable"),
                reconciliation.QueryWindowStartUtc,
                unavailableAt,
                null);
        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation inconclusive =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliation,
                unavailableObservation,
                unavailableAt);
        Assert.False(inconclusive.IsConclusive);
        Assert.Null(inconclusive.SourceSequence);
        Assert.Null(inconclusive.Snapshot);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt inconclusiveReceipt =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaim,
                Guid.CreateVersion7(),
                inconclusive,
                Guid.CreateVersion7());
        Assert.Equal("unknown", inconclusiveReceipt.State);

        reconciliationClaim = Guid.CreateVersion7();
        reconciliation = await lifecycle.BeginReconciliationAsync(
            gatewayContext,
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            reconciliationClaim,
            Guid.CreateVersion7());
        Assert.Equal(2, reconciliation.Attempt);
        Assert.Equal(
            unavailableObservation.WindowStartUtc,
            reconciliation.QueryWindowStartUtc);

        DateTimeOffset observedAt = UtcNow();
        var snapshot = new BrokerReconciliationSnapshot(
            1,
            request.Exposure.SourceSequence + 1,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            request.Command.Generation,
            fixture.GatewayArtifactId,
            request.Provenance.GatewayArtifactSha256,
            reconciliation.QueryWindowStartUtc,
            observedAt,
            true,
            true,
            request.Exposure.Account with
            {
                Sequence = request.Exposure.SourceSequence + 1,
                ObservedAtUtc = observedAt
            },
            [],
            [
                new BrokerOrderSnapshot(
                    "order-1",
                    request.Command.Symbol,
                    request.Command.Side,
                    request.Command.OrderType,
                    request.Command.Volume,
                    0m,
                    request.Command.RequestedPrice,
                    request.Command.StopLoss,
                    request.Command.TakeProfit,
                    "filled",
                    request.Command.OwnershipTag,
                    observedAt)
            ],
            [
                new BrokerDealSnapshot(
                    "deal-1",
                    "order-1",
                    request.Command.Symbol,
                    request.Command.Side,
                    request.Command.Volume,
                    1.1m,
                    observedAt)
            ],
            [
                new BrokerCommandReconciliation(
                    request.Command.CommandId,
                    BrokerReconciliationMatch.Filled,
                    "deal_history_match",
                    "order-1",
                    "deal-1",
                    observedAt)
            ],
            observedAt);
        var sourceDocument = new YO4X.Trading.Application
            .BrokerCommandReconciliationValidator.BrokerReconciliationSourceDocument(
                snapshot.SourceSequence,
                snapshot.QueryWindowStartUtc,
                snapshot.QueryWindowEndUtc,
                snapshot);
        var observation = new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            CanonicalJson.Sha256(sourceDocument),
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);
        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliation,
                observation,
                observedAt);
        Assert.True(evidence.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Filled, evidence.Match);

        YO4X.Trading.Application.BrokerCommandLifecycleReceipt completed =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaim,
                Guid.CreateVersion7(),
                evidence,
                Guid.CreateVersion7());
        Assert.Equal("reconciled", completed.State);

        await AssertDurableRowsAsync(database, fixture, request.Command.CommandId);
        await AssertRawAuthorizerTableReadRejectedAsync(database, authorizerContext);

        await SuspendStrategyAsync(database, fixture);
        BrokerCommandAuthorizationRequest afterSuspension = CreateAuthorizationRequest(fixture, lease);
        var suspendedContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            afterSuspension.Command.CommandId);
        await Assert.ThrowsAnyAsync<Exception>(
            () => store.AuthorizeAsync(suspendedContext, afterSuspension));
    }

    private static async Task<VerificationFixture> SeedVerifiedStrategyAsync(
        PostgresTestDatabase database)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid importJobId = Guid.CreateVersion7();
        Guid importCorrelationId = Guid.CreateVersion7();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        byte[] capabilityDigest = SHA256.HashData(capability);
        DateTimeOffset now = UtcNow();
        try
        {
            var importContext = new TenantExecutionContext(
                tenantId,
                userId,
                importCorrelationId);
            await using (TenantPostgresTransaction seed =
                await database.Application.BeginTenantTransactionAsync(importContext))
            {
                await using NpgsqlCommand command = seed.CreateCommand(
                    """
                    insert into identity.tenants (id, slug, display_name)
                    values (@tenant_id, @slug, 'Durable authorization tenant');
                    insert into identity.user_identities
                        (id, tenant_id, normalized_email, security_state,
                         email_verified_at, created_at, updated_at)
                    values
                        (@user_id, @tenant_id, @email, 'active', @now, @now, @now);
                    """);
                AddUuid(command, "tenant_id", tenantId);
                AddUuid(command, "user_id", userId);
                command.Parameters.AddWithValue("slug", NpgsqlDbType.Text, $"tenant-{tenantId:N}");
                command.Parameters.AddWithValue(
                    "email",
                    NpgsqlDbType.Text,
                    $"user-{userId:N}@example.test");
                AddTimestamp(command, "now", now);
                await command.ExecuteNonQueryAsync();
                await seed.CommitAsync();
            }

            await using (TenantPostgresTransaction control =
                await database.ControlApi.BeginTenantTransactionAsync(importContext))
            {
                await using NpgsqlCommand command = control.CreateCommand(
                    """
                    insert into control.strategy_import_jobs
                        (id, tenant_id, user_id, correlation_id, source_label,
                         capability_sha256, expires_at)
                    values
                        (@id, @tenant_id, @user_id, @correlation_id,
                         'durable-auth-ea', @capability_sha256,
                         statement_timestamp() + interval '20 minutes')
                    """);
                AddUuid(command, "id", importJobId);
                AddUuid(command, "tenant_id", tenantId);
                AddUuid(command, "user_id", userId);
                AddUuid(command, "correlation_id", importCorrelationId);
                command.Parameters.AddWithValue(
                    "capability_sha256",
                    NpgsqlDbType.Bytea,
                    capabilityDigest);
                await command.ExecuteNonQueryAsync();
                await control.CommitAsync();
            }

            byte[] source = Encoding.UTF8.GetBytes(
                "void OnTick(){ MqlTradeRequest r={}; MqlTradeResult x={}; OrderSend(r,x); }");
            using var corpus = new Mql5AnalyzedCorpus(
                new Mql5StaticInventoryAnalyzer().Analyze(
                    [new Mql5SourceDocument("Experts/Durable.mq5", source)]),
                [new Mql5SourceDocument("Experts/Durable.mq5", source.ToArray())]);
            using var persistenceRequest = new Mql5CorpusPersistenceRequest(
                importJobId,
                capability);
            Mql5CorpusPersistenceResult persisted = await new PostgresMql5CorpusStore(
                database.ConversionWorker).PersistAsync(persistenceRequest, corpus);
            (string reportSha256, DateTimeOffset corpusCreatedAt) =
                await ReadPersistedCorpusBindingAsync(database, importJobId, persisted);

            Guid strategyId = Guid.CreateVersion7();
            Guid strategyVersionId = Guid.CreateVersion7();
            Guid bindingId = Guid.CreateVersion7();
            Guid verifierId = Guid.CreateVersion7();
            string packageSha256 = Digest("strategy-package");
            string signingKeyId = "strategy-verifier-key-1";
            var fixture = new VerificationFixture(
                tenantId,
                userId,
                strategyId,
                strategyVersionId,
                bindingId,
                importJobId,
                packageSha256,
                persisted.CorpusSha256,
                persisted.ManifestSha256,
                reportSha256,
                Digest("compiled-artifact"),
                Digest("compiler-artifact"),
                Digest("parse-typecheck-proof"),
                Digest("compile-proof"),
                Digest("semantic-conversion-proof"),
                Digest("reference-parity-proof"),
                Digest("demo-runtime-proof"),
                verifierId,
                signingKeyId,
                [],
                corpusCreatedAt.AddSeconds(1),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());

            byte[] verificationEvidence = VerificationEvidence(fixture, signingKeyId);
            try
            {
                using ECDsa verifierKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                fixture = fixture with
                {
                    VerificationSignature = verifierKey.SignData(
                        verificationEvidence,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence)
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verificationEvidence);
            }

            await using TenantPostgresTransaction governance =
                await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
            await using NpgsqlCommand strategy = governance.CreateCommand(
                """
                insert into governance.strategy_versions
                    (id, tenant_id, strategy_id, version_number, package_sha256,
                     manifest_sha256, schema_sha256, provenance, evidence, state,
                     created_at, updated_at)
                values
                    (@id, @tenant_id, @strategy_id, 1, @package_sha256,
                     @manifest_sha256, @schema_sha256,
                     '{"source":"verified-corpus"}'::jsonb, '{}'::jsonb,
                     'simulation_review', @now, @now)
                """);
            AddUuid(strategy, "id", strategyVersionId);
            AddUuid(strategy, "tenant_id", tenantId);
            AddUuid(strategy, "strategy_id", strategyId);
            strategy.Parameters.AddWithValue("package_sha256", NpgsqlDbType.Text, packageSha256);
            strategy.Parameters.AddWithValue("manifest_sha256", NpgsqlDbType.Text, Digest("manifest"));
            strategy.Parameters.AddWithValue("schema_sha256", NpgsqlDbType.Text, Digest("schema"));
            AddTimestamp(strategy, "now", UtcNow());
            await strategy.ExecuteNonQueryAsync();
            await governance.CommitAsync();
            return fixture;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
            CryptographicOperations.ZeroMemory(capabilityDigest);
        }
    }

    private static async Task AssertRawAdminPromotionRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update governance.strategy_versions
            set state = 'demo_approved', evidence = '{"forged":true}'::jsonb,
                row_version = row_version + 1, updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @id
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "id", fixture.StrategyVersionId);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task<(string ReportSha256, DateTimeOffset CreatedAt)>
        ReadPersistedCorpusBindingAsync(
            PostgresTestDatabase database,
            Guid corpusId,
            Mql5CorpusPersistenceResult persisted)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select corpus_sha256, manifest_sha256, report_sha256, state, created_at
            from governance.strategy_source_corpora
            where id = @corpus_id
            """,
            connection);
        AddUuid(command, "corpus_id", corpusId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(persisted.CorpusSha256, reader.GetString(0));
        Assert.Equal(persisted.ManifestSha256, reader.GetString(1));
        Assert.Equal("static_analyzed", reader.GetString(3));
        string reportSha256 = reader.GetString(2);
        DateTimeOffset createdAt = reader.GetFieldValue<DateTimeOffset>(4);
        Assert.False(await reader.ReadAsync());
        return (reportSha256, createdAt);
    }

    private static async Task AssertWrongVerificationMetadataRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        byte[] wrongEvidence = VerificationEvidence(fixture, "wrong-signing-key");
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.StrategyVerifier.BeginTenantTransactionAsync(fixture.VerifierContext);
            await using NpgsqlCommand command = VerificationCommand(transaction, fixture, wrongEvidence);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () => await command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongEvidence);
        }
    }

    private static async Task RecordVerificationAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await AssertVerificationEligibilityAsync(database, fixture);
        byte[] evidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.StrategyVerifier.BeginTenantTransactionAsync(fixture.VerifierContext);
            await using NpgsqlCommand command = VerificationCommand(transaction, fixture, evidence);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.BindingId, reader.GetGuid(0));
            Assert.Equal(Digest(evidence), reader.GetString(1));
            Assert.Equal(Digest(fixture.VerificationSignature), reader.GetString(2));
            Assert.False(reader.GetBoolean(4));
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(evidence);
        }
    }

    private static async Task AssertVerificationEligibilityAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select strategy.state, strategy.package_sha256,
                   corpus.state, corpus.corpus_sha256, corpus.manifest_sha256,
                   corpus.report_sha256, corpus.created_at
            from governance.strategy_versions as strategy
            cross join governance.strategy_source_corpora as corpus
            where strategy.tenant_id = @tenant_id
              and strategy.id = @strategy_version_id
              and corpus.tenant_id = @tenant_id
              and corpus.id = @corpus_id
            """,
            connection);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "corpus_id", fixture.CorpusId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("simulation_review", reader.GetString(0));
        Assert.Equal(fixture.PackageSha256, reader.GetString(1));
        Assert.Equal("static_analyzed", reader.GetString(2));
        Assert.Equal(fixture.CorpusSha256, reader.GetString(3));
        Assert.Equal(fixture.SourceManifestSha256, reader.GetString(4));
        Assert.Equal(fixture.SourceReportSha256, reader.GetString(5));
        Assert.True(fixture.VerifiedAtUtc >= reader.GetFieldValue<DateTimeOffset>(6));
        Assert.False(await reader.ReadAsync());
    }

    private static NpgsqlCommand VerificationCommand(
        TenantPostgresTransaction transaction,
        VerificationFixture fixture,
        byte[] evidence)
    {
        NpgsqlCommand command = transaction.CreateCommand(
            """
            select * from control.record_strategy_version_source_binding(
                @binding_id, @strategy_version_id, @corpus_id,
                @package_sha256, @corpus_sha256, @manifest_sha256,
                @report_sha256, @compiled_sha256, @compiler_sha256,
                @parse_sha256, @compile_sha256, @semantic_sha256,
                @parity_sha256, @demo_sha256, @evidence_content,
                @signature_bytes, @signing_key_id, @verified_at, @audit_id)
            """);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "corpus_id", fixture.CorpusId);
        AddText(command, "package_sha256", fixture.PackageSha256);
        AddText(command, "corpus_sha256", fixture.CorpusSha256);
        AddText(command, "manifest_sha256", fixture.SourceManifestSha256);
        AddText(command, "report_sha256", fixture.SourceReportSha256);
        AddText(command, "compiled_sha256", fixture.CompiledArtifactSha256);
        AddText(command, "compiler_sha256", fixture.CompilerArtifactSha256);
        AddText(command, "parse_sha256", fixture.ParseProofSha256);
        AddText(command, "compile_sha256", fixture.CompileProofSha256);
        AddText(command, "semantic_sha256", fixture.SemanticProofSha256);
        AddText(command, "parity_sha256", fixture.ParityProofSha256);
        AddText(command, "demo_sha256", fixture.DemoProofSha256);
        command.Parameters.AddWithValue("evidence_content", NpgsqlDbType.Bytea, evidence);
        command.Parameters.AddWithValue(
            "signature_bytes",
            NpgsqlDbType.Bytea,
            fixture.VerificationSignature);
        AddText(command, "signing_key_id", fixture.SigningKeyId);
        AddTimestamp(command, "verified_at", fixture.VerifiedAtUtc);
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        return command;
    }

    private static async Task AssertWrongBindingPromotionRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.promote_strategy_version_to_demo_approved(@strategy_id, @binding_id, 0, @audit_id)");
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", Guid.CreateVersion7());
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task PromoteAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.promote_strategy_version_to_demo_approved(@strategy_id, @binding_id, 0, @audit_id)");
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("demo_approved", reader.GetString(2));
        Assert.Equal(1L, reader.GetInt64(3));
        await reader.DisposeAsync();
        await transaction.CommitAsync();
    }

    private static async Task<LeaseFixture> SeedRuntimeAuthorityAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        string desiredState)
    {
        DateTimeOffset now = UtcNow();
        string gatewaySha256 = Digest("gateway-artifact");
        string policySha256 = Digest("risk-policy");
        string accountBindingSha256 = Digest("broker-account-binding");
        var claims = new ExecutionLeaseClaims(
                RuntimeContractVersions.ExecutionLeaseV1,
                fixture.LeaseId,
                new ExecutionLeaseBinding(
                    fixture.TenantId,
                    fixture.EntitlementId,
                    fixture.UserId,
                    fixture.DeploymentId,
                    fixture.BrokerAccountId,
                    accountBindingSha256,
                    fixture.StrategyId,
                    fixture.StrategyVersionId,
                    1,
                    fixture.PackageSha256,
                    ExecutionMode.CloudDemo,
                    fixture.RiskPolicyVersionId,
                    policySha256,
                    fixture.WorkerAssignmentId,
                    fixture.WorkerNodeId,
                    fixture.SupervisorWorkloadId,
                    fixture.StrategyHostWorkloadId,
                    fixture.GatewayHostWorkloadId,
                    1,
                    "test-region"),
                now,
                now,
                now.AddMinutes(5),
                now.AddMinutes(10),
                new ExecutionLeaseActionPolicy(
                    LeaseActionClass.Increase | LeaseActionClass.Reduce |
                        LeaseActionClass.EmergencyClose,
                    LeaseActionClass.Reduce | LeaseActionClass.EmergencyClose,
                    LeaseActionClass.None,
                    LeaseActionClass.None));
        using ECDsa leaseSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] canonicalLeasePayload = ExecutionLeaseCanonicalizer.Serialize(claims);
        byte[] leaseSignature = leaseSigningKey.SignData(
            canonicalLeasePayload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var lease = new SignedExecutionLease(
            claims,
            Digest(canonicalLeasePayload),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            "execution-lease-key-1",
            Convert.ToBase64String(leaseSignature)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'));
        byte[] leasePublicKey = leaseSigningKey.ExportSubjectPublicKeyInfo();
        CryptographicOperations.ZeroMemory(canonicalLeasePayload);
        CryptographicOperations.ZeroMemory(leaseSignature);
        byte[] riskSignature = new byte[72];
        riskSignature[0] = 0x30;

        byte[] verificationEvidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        string verificationEvidenceSha256 = Digest(verificationEvidence);
        CryptographicOperations.ZeroMemory(verificationEvidence);
        string verificationSignatureSha256 = Digest(fixture.VerificationSignature);

        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into governance.broker_profiles
                (id, broker_id, profile_version, broker_company, server_name,
                 environment_support, capabilities, cloud_rules, limitations,
                 evidence_sha256, tested_at, state)
            values
                (@profile_id, @broker_id, 1, 'YO4X Test Broker', 'Demo-1',
                 array['demo'], '{"hedging":true}'::jsonb,
                 '{"dedicated":true}'::jsonb, '{}'::jsonb,
                 @profile_evidence, @now, 'approved');

            insert into governance.gateway_artifacts
                (id, vendor_name, vendor_version, sha256, signature_state,
                 quarantine_reference, provenance, licence_evidence,
                 sbom_reference, network_evidence, state, created_at, updated_at)
            values
                (@gateway_id, 'YO4X', 'proof-only', @gateway_sha256, 'valid',
                 'quarantine://gateway/proof-only', '{"build":"verified"}'::jsonb,
                 '{"licence":"test"}'::jsonb, 'sbom://gateway/proof-only',
                 '{"egress":"allowlisted"}'::jsonb, 'approved', @now, @now);

            insert into governance.risk_policy_versions
                (id, tenant_id, policy_id, version_number, normalized_policy,
                 policy_digest, signature_algorithm, signature_bytes,
                 signature_sha256, signing_key_id, state, effective_at,
                 created_at, updated_at)
            values
                (@policy_version_id, @tenant_id, @policy_id, 1,
                 '{"kind":"numeric"}'::jsonb, @policy_sha256,
                 'ECDSA_P256_SHA256_DER', @risk_signature,
                 @risk_signature_sha256, 'risk-key-1', 'active', @now, @now, @now);

            insert into operations.broker_accounts
                (id, tenant_id, user_id, broker_id, broker_profile_id, server,
                 masked_login, binding_fingerprint, environment, account_mode,
                 dedicated_cloud_use, manual_or_external_trading_detected,
                 trading_allowed, broker_hosted_stop_loss,
                 broker_hosted_take_profit, supports_position_query,
                 supports_order_query, supports_deal_history,
                 capability_observed_at, capability_valid_until,
                 capability_evidence_sha256, credential_reference,
                 credential_state, state, created_at, updated_at)
            values
                (@account_id, @tenant_id, @user_id, @broker_id, @profile_id,
                 'Demo-1', '***4242', @account_binding, 'demo', 'hedging',
                 true, false, true, true, true, true, true, true,
                 @now, @now + interval '10 minutes', @account_capability,
                 'vault://yo4x/test-account', 'ready', 'active', @now, @now);

            insert into operations.deployments
                (id, tenant_id, user_id, broker_account_id, strategy_version_id,
                 strategy_source_binding_id,
                 strategy_verification_evidence_sha256,
                 strategy_verification_signature_sha256,
                 strategy_verification_signing_key_id,
                 risk_policy_version_id, risk_policy_digest,
                 gateway_artifact_id, gateway_digest, runtime_digest,
                 strategy_package_digest, region, dedicated_account,
                 hedging_account, broker_hosted_stop_loss,
                 broker_hosted_take_profit, manual_or_external_trading_detected,
                 binding_evidence, binding_evidence_sha256,
                 creation_effective_policy_digest,
                 creation_policy_version_watermark, creation_policy_input_sha256,
                 configuration_sha256, environment, desired_state, observed_state,
                 fence_generation, created_at, updated_at)
            values
                (@deployment_id, @tenant_id, @user_id, @account_id,
                 @strategy_version_id, @binding_id,
                 @verification_evidence_sha256, @verification_signature_sha256,
                 @verification_signing_key_id, @policy_version_id, @policy_sha256,
                 @gateway_id, @gateway_sha256, @runtime_sha256,
                 @package_sha256, 'test-region', true, true, true, true, false,
                 '{"verification":"exact"}'::jsonb, @binding_evidence_sha256,
                 @effective_policy_sha256, @policy_watermark_sha256,
                 @policy_input_sha256, @configuration_sha256, 'demo',
                 @desired_state, @observed_state, @generation, @now, @now);

            insert into operations.worker_nodes
                (id, region, node_name, image_digest, state, capacity,
                 last_heartbeat_at, created_at, updated_at)
            values
                (@worker_id, 'test-region', @worker_name, @runtime_sha256,
                 'ready', '{"slots":1}'::jsonb, @now, @now, @now);

            insert into operations.worker_assignments
                (id, tenant_id, deployment_id, worker_node_id,
                 supervisor_identity, strategy_host_identity,
                 gateway_host_identity, fence_generation, runtime_digest,
                 gateway_artifact_id, state, assigned_at, lease_expires_at)
            values
                (@assignment_id, @tenant_id, @deployment_id, @worker_id,
                 @supervisor_id, @strategy_host_id, @gateway_host_id,
                 @generation, @runtime_sha256, @gateway_id, 'active', @now,
                 @now + interval '10 minutes');

            insert into operations.execution_leases
                (id, tenant_id, entitlement_id, user_id, deployment_id,
                 broker_account_id, broker_binding_sha256, strategy_id,
                 strategy_version_id, strategy_version_number,
                 strategy_package_sha256, execution_mode,
                 risk_policy_version_id, risk_policy_sha256,
                 worker_assignment_id, worker_instance_id,
                 supervisor_workload_id, strategy_host_workload_id,
                 gateway_host_workload_id, region, generation, contract_version,
                 active_actions, grace_actions, expired_actions, revoked_actions,
                 signature_algorithm, signing_key_id, lease_token_sha256,
                  lease_payload_sha256, lease_signature_sha256,
                  signed_envelope, signed_envelope_content, state,
                 issued_at, not_before, expires_at, grace_expires_at,
                 created_at, updated_at)
            values
                (@lease_id, @tenant_id, @entitlement_id, @user_id,
                 @deployment_id, @account_id, @account_binding, @strategy_id,
                 @strategy_version_id, 1, @package_sha256, 'cloud_demo',
                 @policy_version_id, @policy_sha256, @assignment_id, @worker_id,
                 @supervisor_id, @strategy_host_id, @gateway_host_id,
                 'test-region', @generation, @lease_contract_version,
                 @active_actions, @grace_actions, 0, 0, @lease_algorithm,
                 @lease_signing_key, @lease_token_sha256, @lease_payload_sha256,
                  @lease_signature_sha256,
                  convert_from(@signed_envelope_content, 'UTF8')::jsonb,
                  @signed_envelope_content, 'active', @issued_at, @not_before,
                 @expires_at, @grace_expires_at, @now, @now);
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "user_id", fixture.UserId);
        AddUuid(command, "profile_id", fixture.BrokerProfileId);
        AddUuid(command, "broker_id", fixture.BrokerId);
        AddUuid(command, "gateway_id", fixture.GatewayArtifactId);
        AddUuid(command, "policy_version_id", fixture.RiskPolicyVersionId);
        AddUuid(command, "policy_id", Guid.CreateVersion7());
        AddUuid(command, "account_id", fixture.BrokerAccountId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "worker_id", fixture.WorkerNodeId);
        AddUuid(command, "assignment_id", fixture.WorkerAssignmentId);
        AddUuid(command, "lease_id", fixture.LeaseId);
        AddUuid(command, "entitlement_id", fixture.EntitlementId);
        AddUuid(command, "strategy_id", fixture.StrategyId);
        AddUuid(command, "supervisor_id", fixture.SupervisorWorkloadId);
        AddUuid(command, "strategy_host_id", fixture.StrategyHostWorkloadId);
        AddUuid(command, "gateway_host_id", fixture.GatewayHostWorkloadId);
        AddText(command, "profile_evidence", Digest("profile-evidence"));
        AddText(command, "gateway_sha256", gatewaySha256);
        AddText(command, "policy_sha256", policySha256);
        command.Parameters.AddWithValue("risk_signature", NpgsqlDbType.Bytea, riskSignature);
        AddText(command, "risk_signature_sha256", Digest(riskSignature));
        AddText(command, "account_binding", accountBindingSha256);
        AddText(command, "account_capability", Digest("account-capability"));
        AddText(command, "verification_evidence_sha256", verificationEvidenceSha256);
        AddText(command, "verification_signature_sha256", verificationSignatureSha256);
        AddText(command, "verification_signing_key_id", fixture.SigningKeyId);
        AddText(command, "runtime_sha256", $"sha256:{Digest("runtime-image")}");
        AddText(command, "package_sha256", fixture.PackageSha256);
        AddText(command, "binding_evidence_sha256", Digest("binding-evidence"));
        AddText(command, "effective_policy_sha256", Digest("effective-policy"));
        AddText(command, "policy_watermark_sha256", Digest("policy-watermark"));
        AddText(command, "policy_input_sha256", Digest("policy-input"));
        AddText(command, "configuration_sha256", Digest("configuration"));
        AddText(command, "desired_state", desiredState);
        AddText(command, "observed_state", desiredState == "running" ? "running" : "unknown");
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, 1L);
        AddText(command, "worker_name", $"worker-{fixture.WorkerNodeId:N}");
        command.Parameters.AddWithValue(
            "lease_contract_version",
            NpgsqlDbType.Integer,
            lease.Claims.ContractVersion);
        command.Parameters.AddWithValue(
            "active_actions",
            NpgsqlDbType.Integer,
            (int)lease.Claims.ActionPolicy.Active);
        command.Parameters.AddWithValue(
            "grace_actions",
            NpgsqlDbType.Integer,
            (int)lease.Claims.ActionPolicy.Grace);
        AddText(command, "lease_algorithm", lease.SignatureAlgorithm);
        AddText(command, "lease_signing_key", lease.SigningKeyId);
        AddText(command, "lease_token_sha256", ExecutionLeaseEnvelopeDigest.Sha256(lease));
        AddText(command, "lease_payload_sha256", lease.PayloadSha256);
        AddText(command, "lease_signature_sha256", ExecutionLeaseEnvelopeDigest.SignatureSha256(lease));
        command.Parameters.AddWithValue(
            "signed_envelope_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(CanonicalJson.Serialize(lease)));
        AddTimestamp(command, "issued_at", lease.Claims.IssuedAtUtc);
        AddTimestamp(command, "not_before", lease.Claims.NotBeforeUtc);
        AddTimestamp(command, "expires_at", lease.Claims.ExpiresAtUtc);
        AddTimestamp(command, "grace_expires_at", lease.Claims.GraceExpiresAtUtc);
        AddTimestamp(command, "now", now);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new LeaseFixture(lease, leasePublicKey);
    }

    private static BrokerCommandAuthorizationRequest CreateAuthorizationRequest(
        VerificationFixture fixture,
        SignedExecutionLease lease)
    {
        DateTimeOffset evaluatedAt = UtcNow();
        DateTimeOffset observedAt = evaluatedAt.AddMilliseconds(-50);
        Guid commandId = Guid.CreateVersion7();
        string gatewaySha256 = Digest("gateway-artifact");
        var command = new NormalizedBrokerCommand(
            1,
            commandId,
            Guid.CreateVersion7(),
            fixture.DeploymentId,
            1,
            $"place-{commandId:N}",
            BrokerCommandAction.Place,
            "EURUSD",
            BrokerOrderSide.Buy,
            BrokerOrderType.Market,
            0.10m,
            null,
            1.0900m,
            null,
            10,
            "yo4x-owned-position",
            null,
            null,
            null,
            null,
            null,
            null,
            evaluatedAt);
        var account = new BrokerAccountSnapshot(
            11,
            "***4242",
            "YO4X Test Broker",
            "Demo-1",
            YO4X.Trading.Abstractions.BrokerAccountMode.Hedging,
            BrokerEnvironment.Demo,
            BrokerTradingAccess.TradingAllowed,
            "USD",
            10_000m,
            10_000m,
            9_000m,
            observedAt);
        var exposure = new BrokerExposureSnapshotDocument(
            BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1,
            Guid.CreateVersion7(),
            fixture.TenantId,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            1,
            fixture.WorkerAssignmentId,
            fixture.WorkerNodeId,
            fixture.GatewayArtifactId,
            gatewaySha256,
            "gateway_reconciliation",
            12,
            Digest("gateway-exposure-source"),
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            account,
            [],
            [],
            [],
            []);
        var riskInput = new NumericRiskEvaluationInput(
            evaluatedAt,
            RiskActionClass.ExposureIncrease,
            new RiskSnapshotTimestamps(
                observedAt,
                observedAt,
                observedAt,
                observedAt,
                observedAt,
                observedAt),
            new MarketRiskSnapshot(1m, 1m, true, true, 0m),
            new AccountRiskSnapshot(
                BrokerAccountEnvironment.Demo,
                YO4X.Risk.BrokerAccountMode.Hedging,
                10_000m,
                true,
                false,
                true),
            new ExposureRiskSnapshot(0.10m, 0m, 0m, 0, 0, 0, observedAt, observedAt),
            new ProtectionRiskSnapshot(true, 100m, false, null, false, false),
            new RiskDayStateSnapshot("2026-08-22", observedAt, 10_000m, 10_000m, 0m, 0m));
        string riskInputSha256 = CanonicalJson.Sha256(riskInput);
        string policySha256 = Digest("risk-policy");
        var riskDecision = new NumericRiskDecision(
            NumericRiskDecisionDisposition.Allowed,
            RiskActionClass.ExposureIncrease,
            policySha256,
            riskInputSha256,
            Digest($"risk-decision-{commandId:N}"),
            "2026-08-22",
            10_000m,
            10_000m,
            0m,
            0m,
            [new NumericRiskRuleResult("risk.input.complete", RiskRuleOutcome.Passed, "true", "true")]);
        byte[] verificationEvidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        string verificationEvidenceSha256 = Digest(verificationEvidence);
        CryptographicOperations.ZeroMemory(verificationEvidence);
        var provenance = new BrokerCommandProvenance(
            fixture.TenantId,
            fixture.BrokerAccountId,
            fixture.StrategyId,
            fixture.StrategyVersionId,
            1,
            fixture.PackageSha256,
            fixture.BindingId,
            fixture.CorpusId,
            fixture.CorpusSha256,
            fixture.SourceManifestSha256,
            fixture.SourceReportSha256,
            fixture.CompiledArtifactSha256,
            fixture.CompilerArtifactSha256,
            fixture.ParseProofSha256,
            fixture.CompileProofSha256,
            fixture.SemanticProofSha256,
            fixture.ParityProofSha256,
            fixture.DemoProofSha256,
            verificationEvidenceSha256,
            Digest(fixture.VerificationSignature),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            fixture.SigningKeyId,
            fixture.VerifierWorkloadId,
            fixture.VerifiedAtUtc,
            true,
            fixture.GatewayArtifactId,
            gatewaySha256);
        return new BrokerCommandAuthorizationRequest(
            command,
            provenance,
            exposure,
            riskInput,
            riskDecision,
            lease,
            new ExecutionSafetyAuthorization(Digest("[]"), 0),
            new BrokerReconciliationCommitmentDocument(
                BrokerCommandAuthorizationContractVersions.ReconciliationV1,
                commandId,
                "orders_positions_deals",
                Digest($"reconciliation-scope-{commandId:N}"),
                evaluatedAt.AddMinutes(1),
                evaluatedAt.AddMinutes(2)),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
    }

    private static async Task AssertDurableRowsAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        Guid commandId)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select broker_command.state,
                (select count(*) from operations.broker_exposure_snapshots
                 where tenant_id = @tenant_id and deployment_id = @deployment_id),
                (select count(*) from operations.broker_command_risk_decisions
                 where tenant_id = @tenant_id and deployment_id = @deployment_id),
                (select count(*) from operations.broker_command_reconciliations
                 where tenant_id = @tenant_id and command_id = @command_id),
                (select count(*) from audit.audit_events
                 where tenant_id = @tenant_id and target_id = @command_id::text)
            from operations.broker_commands as broker_command
            where broker_command.tenant_id = @tenant_id
              and broker_command.id = @command_id
            """,
            connection);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        AddUuid(command, "command_id", commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("reconciled", reader.GetString(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(2L, reader.GetInt64(3));
        Assert.Equal(7L, reader.GetInt64(4));
    }

    private static async Task AssertRawAuthorizerTableReadRejectedAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context)
    {
        await using TenantPostgresTransaction transaction =
            await database.TradeAuthorizer.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select count(*) from operations.broker_commands");
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    private static async Task SuspendStrategyAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update governance.strategy_versions
            set state = 'suspended', row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @strategy_id
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static byte[] VerificationEvidence(
        VerificationFixture fixture,
        string signingKeyId) =>
        Encoding.UTF8.GetBytes(CanonicalJson.Serialize(new
        {
            contractVersion = 1,
            strategyVersionId = fixture.StrategyVersionId,
            strategyPackageSha256 = fixture.PackageSha256,
            sourceCorpusId = fixture.CorpusId,
            sourceCorpusSha256 = fixture.CorpusSha256,
            sourceManifestSha256 = fixture.SourceManifestSha256,
            sourceReportSha256 = fixture.SourceReportSha256,
            compiledArtifactSha256 = fixture.CompiledArtifactSha256,
            compilerArtifactSha256 = fixture.CompilerArtifactSha256,
            parseTypecheckProofSha256 = fixture.ParseProofSha256,
            compileProofSha256 = fixture.CompileProofSha256,
            semanticConversionProofSha256 = fixture.SemanticProofSha256,
            referenceParityProofSha256 = fixture.ParityProofSha256,
            demoRuntimeProofSha256 = fixture.DemoProofSha256,
            verifiedByWorkloadId = fixture.VerifierWorkloadId,
            verificationSignatureAlgorithm = P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            verificationSigningKeyId = signingKeyId,
            signatureCryptographicallyVerified = true,
            parsedAndTypeChecked = true,
            metaEditorCompileProven = true,
            semanticConversionProven = true,
            referenceParityProven = true,
            demoRuntimeProven = true
        }));

    private static DateTimeOffset UtcNow() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));

    private static string Digest(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static void AddText(NpgsqlCommand command, string name, string value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value);

    private static void AddTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value.ToUniversalTime());

    private sealed record VerificationFixture(
        Guid TenantId,
        Guid UserId,
        Guid StrategyId,
        Guid StrategyVersionId,
        Guid BindingId,
        Guid CorpusId,
        string PackageSha256,
        string CorpusSha256,
        string SourceManifestSha256,
        string SourceReportSha256,
        string CompiledArtifactSha256,
        string CompilerArtifactSha256,
        string ParseProofSha256,
        string CompileProofSha256,
        string SemanticProofSha256,
        string ParityProofSha256,
        string DemoProofSha256,
        Guid VerifierWorkloadId,
        string SigningKeyId,
        byte[] VerificationSignature,
        DateTimeOffset VerifiedAtUtc,
        Guid BrokerId,
        Guid BrokerProfileId,
        Guid GatewayArtifactId,
        Guid BrokerAccountId,
        Guid RiskPolicyVersionId,
        Guid DeploymentId,
        Guid WorkerNodeId,
        Guid WorkerAssignmentId,
        Guid EntitlementId,
        Guid LeaseId,
        Guid SupervisorWorkloadId,
        Guid StrategyHostWorkloadId,
        Guid GatewayHostWorkloadId)
    {
        public TenantExecutionContext UserContext => new(
            TenantId,
            UserId,
            Guid.CreateVersion7());

        public TenantExecutionContext VerifierContext => new(
            TenantId,
            VerifierWorkloadId,
            BindingId);

        public TenantExecutionContext AdminContext => new(
            TenantId,
            Guid.Parse("27b342e1-2ea3-46fd-8700-ff7de98cb3c6"),
            Guid.CreateVersion7());
    }

    private sealed record LeaseFixture(
        SignedExecutionLease Lease,
        byte[] SubjectPublicKeyInfo);
}
