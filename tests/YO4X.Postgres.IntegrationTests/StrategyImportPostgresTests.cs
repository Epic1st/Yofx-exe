using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Conversion.Worker;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Identity;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class StrategyImportPostgresTests(PostgresContainerFixture postgres)
{
    private const string SuppliedCorpusSha256 =
        "9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47";
    private const string SuppliedDependencyGraphSha256 =
        "c463d3a6de0eaef29b912cfb9af5bd949c0591b26896d866acb2c088943ba10a";
    private const string SuppliedEmbeddedEvidenceSha256 =
        "e191d8a5b1e572f08b16d420edfef5a8f386b003dbc0e2b122ae201a16c065b7";
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

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
            Mql5ConversionCorpusEvidence expectedClassification =
                new Mql5ConversionEvidenceAnalyzer().Analyze(corpus.Documents);
            Assert.Equal(
                expectedClassification.EvidenceSha256,
                persisted.ConversionEmbeddedEvidenceSha256);
            Assert.Equal(
                Sha256Utf8(Mql5ConversionEvidenceFormatter.ToJson(expectedClassification)),
                persisted.ConversionFormattedEvidenceSha256);
            Assert.Equal(
                CanonicalJson.Sha256(expectedClassification),
                persisted.ConversionCanonicalEvidenceSha256);

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

            ConversionClassificationSnapshot completedClassification =
                await ReadConversionClassificationAsync(database, binding);
            Assert.Equal(1, completedClassification.Count);
            Assert.Equal(corpus.Manifest.FileCount, completedClassification.FileCount);
            Assert.Equal(corpus.Manifest.TotalBytes, completedClassification.TotalBytes);
            Assert.Equal(corpus.Manifest.FileCount, completedClassification.ExactBoundFileCount);
            Assert.Equal(expectedClassification.DependencyGraphSha256,
                completedClassification.DependencyGraphSha256);
            Assert.Equal(expectedClassification.EvidenceSha256,
                completedClassification.EmbeddedEvidenceSha256);
            Assert.Equal(persisted.ConversionFormattedEvidenceSha256,
                completedClassification.FormattedEvidenceSha256);
            Assert.Equal(persisted.ConversionCanonicalEvidenceSha256,
                completedClassification.CanonicalEvidenceSha256);
            Assert.False(completedClassification.HasLaterProofClaim);
            Assert.Equal(1, completedClassification.AuditCount);
            Assert.Equal(1, completedClassification.OutboxCount);
            Assert.True(completedClassification.HasSafeAuditPayload);
            Assert.True(completedClassification.HasSafeOutboxPayload);
            Assert.Equal(0, completedClassification.PromotionCount);

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
            // Reservation and evidence writes share one transaction, so the
            // reserved state is invisible to other connections until commit.
            // The advisory-parked worker proves the reservation was taken.
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
            ConversionClassificationSnapshot classification =
                await ReadConversionClassificationAsync(database, binding);
            Assert.Equal(1, classification.Count);
            Assert.Equal(1, classification.AuditCount);
            Assert.Equal(1, classification.OutboxCount);
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
            Assert.Equal(
                SuppliedEmbeddedEvidenceSha256,
                result.ConversionEmbeddedEvidenceSha256);

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
            ConversionClassificationSnapshot classification =
                await ReadConversionClassificationAsync(database, binding);
            Assert.Equal(1, classification.Count);
            Assert.Equal(198, classification.FileCount);
            Assert.Equal(198, classification.ExactBoundFileCount);
            Assert.Equal(SuppliedDependencyGraphSha256, classification.DependencyGraphSha256);
            Assert.Equal(SuppliedEmbeddedEvidenceSha256,
                classification.EmbeddedEvidenceSha256);
            Assert.False(classification.HasLaterProofClaim);
            Assert.Equal(1, classification.AuditCount);
            Assert.Equal(1, classification.OutboxCount);
            Assert.Equal(0, classification.PromotionCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    [PostgresFact]
    public async Task ControlApplicationProjectsExactSuppliedCorpusWithoutSensitiveEvidence()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "typed-compatibility-projection");
            string corpusRoot = FindSuppliedCorpusRoot();
            var inventory = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
            using Mql5AnalyzedCorpus corpus = await inventory
                .AnalyzeDirectoryForPersistenceAsync(corpusRoot);
            Assert.Equal(198, corpus.Manifest.FileCount);

            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);
            Mql5CorpusPersistenceResult persisted = await store.PersistAsync(request, corpus);
            Assert.Equal(198, persisted.FileCount);

            (UserActor owner, UserActor otherUser, UserActor otherTenant) =
                await SeedCompatibilityActorsAsync(database, binding);
            var options = new ControlPlanePostgresOptions
            {
                ApprovedGatewayDigest = new string('a', 64),
                ApprovedRegion = "integration-region",
                ApprovedBrokerServer = "integration-demo",
                ApprovedBrokerProfileId = Guid.CreateVersion7(),
                ApprovedRuntimeImageDigest = $"sha256:{new string('b', 64)}",
                SecretIngestionOrigin = new Uri("https://ingestion.example"),
                ApprovedCredentialClientOrigin = new Uri("https://portal.example")
            };
            options.Validate();
            using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var trustStore = new PolicySignatureTrustStore(
                new Dictionary<string, byte[]>
                {
                    ["integration-projection"] = policyKey.ExportSubjectPublicKeyInfo()
                });
            var application = new PostgresControlPlaneApplication(
                database.ControlApi,
                options,
                SystemClock.Instance,
                trustStore);

            StrategyCompatibilityProjection projection =
                Assert.IsType<StrategyCompatibilityProjection>(
                    await application.GetStrategyCompatibilityAsync(
                        owner,
                        binding.ImportJobId,
                        CancellationToken.None));

            Assert.Equal(198, projection.AnalyzedFileCount);
            Assert.Equal(198, projection.TotalFileCount);
            Assert.Equal(198, projection.Items.Count);
            var strategyIds = new HashSet<Guid>();
            for (int index = 0; index < corpus.Manifest.Files.Count; index++)
            {
                Mql5SourceManifest expected = corpus.Manifest.Files[index];
                StrategyCompatibilityItem actual = projection.Items[index];
                StrategyCompatibilitySourceType expectedSourceType = expected.Kind switch
                {
                    Mql5SourceKind.ExpertOrProgram => StrategyCompatibilitySourceType.Mq5,
                    Mql5SourceKind.Header => StrategyCompatibilitySourceType.Mqh,
                    _ => throw new ArgumentOutOfRangeException(nameof(expected.Kind))
                };
                StrategyCompatibilityAnalysisState expectedState = expected.Disposition switch
                {
                    Mql5StaticDisposition.NeedsSemanticValidation =>
                        StrategyCompatibilityAnalysisState.ReviewRequired,
                    Mql5StaticDisposition.NeedsSource =>
                        StrategyCompatibilityAnalysisState.Pending,
                    Mql5StaticDisposition.Unsupported or Mql5StaticDisposition.Rejected =>
                        StrategyCompatibilityAnalysisState.Unsupported,
                    _ => throw new ArgumentOutOfRangeException(nameof(expected.Disposition))
                };
                string expectedExtension = expectedSourceType == StrategyCompatibilitySourceType.Mq5
                    ? ".mq5"
                    : ".mqh";

                Assert.NotEqual(Guid.Empty, actual.StrategyId);
                Assert.True(strategyIds.Add(actual.StrategyId));
                Assert.Equal(expected.RelativePath, actual.Name + expectedExtension);
                Assert.Equal(expectedSourceType, actual.SourceType);
                Assert.Equal(expectedState, actual.AnalysisState);
                Assert.Equal(expected.Features.Count, actual.FeatureCount);
                Assert.Null(actual.ReportPath);
            }

            Assert.DoesNotContain(
                projection.Items,
                item => item.AnalysisState == StrategyCompatibilityAnalysisState.Analyzed);
            Assert.Null(await application.GetStrategyCompatibilityAsync(
                otherUser,
                binding.ImportJobId,
                CancellationToken.None));
            Assert.Null(await application.GetStrategyCompatibilityAsync(
                otherTenant,
                binding.ImportJobId,
                CancellationToken.None));
            Assert.Null(await application.GetStrategyCompatibilityAsync(
                owner,
                Guid.CreateVersion7(),
                CancellationToken.None));

            Assert.Equal(
                ["AnalyzedFileCount", "Items", "TotalFileCount"],
                typeof(StrategyCompatibilityProjection)
                    .GetProperties()
                    .Select(static property => property.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            Assert.Equal(
                ["AnalysisState", "FeatureCount", "Name", "ReportPath", "SourceType", "StrategyId"],
                typeof(StrategyCompatibilityItem)
                    .GetProperties()
                    .Select(static property => property.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            string serialized = JsonSerializer.Serialize(
                projection,
                WebJsonOptions);
            foreach (string sensitiveProperty in new[]
            {
                "sourceContent",
                "findings",
                "verification",
                "evidenceDocument",
                "evidenceContent"
            })
            {
                Assert.DoesNotContain(
                    $"\"{sensitiveProperty}\":",
                    serialized,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    [PostgresFact]
    public async Task StoreRecomputesConversionEvidenceBeforeAnyDatabaseMutation()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "conversion-recompute");
            using Mql5AnalyzedCorpus corpus = CreateRepresentativeCorpus();
            Mql5ConversionCorpusEvidence exact = new Mql5ConversionEvidenceAnalyzer()
                .Analyze(corpus.Documents);
            Mql5ConversionFileEvidence first = exact.Files[0];
            Mql5ConversionCorpusEvidence[] invalidEvidence =
            [
                exact with { EvidenceSha256 = new string('a', 64) },
                exact with { DependencyGraphSha256 = new string('b', 64) },
                exact with { FileCount = exact.FileCount + 1 },
                exact with { Files = exact.Files.Skip(1).ToArray() },
                exact with { Files = exact.Files.Append(first).ToArray() },
                exact with
                {
                    Files = exact.Files
                        .Select((file, index) => index == 0
                            ? file with { SourceSha256 = new string('c', 64) }
                            : file)
                        .ToArray()
                },
                exact with
                {
                    Files = exact.Files
                        .Select((file, index) => index == 0
                            ? file with { RelativePath = "Experts/changed.mq5" }
                            : file)
                        .ToArray()
                }
            ];
            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);

            foreach (Mql5ConversionCorpusEvidence invalid in invalidEvidence)
            {
                InvalidDataException rejected = await Assert.ThrowsAsync<InvalidDataException>(
                    () => store.PersistAsync(request, corpus, invalid));
                Assert.Contains("does not exactly match", rejected.Message);
            }

            ImportEvidenceSnapshot untouched = await ReadImportEvidenceAsync(
                database,
                binding,
                capability);
            Assert.Equal("active", untouched.State);
            Assert.Equal(0, untouched.CorpusCount);
            Assert.Equal(0, untouched.FileCount);
            Assert.Equal(0, untouched.AuditCount);
            Assert.Equal(0, untouched.OutboxCount);
            Assert.Equal(0, (await ReadConversionClassificationAsync(database, binding)).Count);

            Mql5CorpusPersistenceResult persisted = await store.PersistAsync(
                request,
                corpus,
                exact);
            Assert.False(persisted.Replayed);
            Assert.Equal(exact.EvidenceSha256, persisted.ConversionEmbeddedEvidenceSha256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    [PostgresFact]
    public async Task ConversionCapabilityRejectsDivergenceAndRawMutationWithoutSideEffects()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        try
        {
            ImportBinding binding = await SeedImportJobAsync(
                database,
                capability,
                "conversion-adversarial");
            using Mql5AnalyzedCorpus corpus = CreateRepresentativeCorpus();
            Mql5ConversionCorpusEvidence exact = new Mql5ConversionEvidenceAnalyzer()
                .Analyze(corpus.Documents);
            var store = new PostgresMql5CorpusStore(database.ConversionWorker);
            using var request = new Mql5CorpusPersistenceRequest(binding.ImportJobId, capability);
            Mql5CorpusPersistenceResult committed = await store.PersistAsync(
                request,
                corpus,
                exact);

            var adversarialMutations = new (string Name, Action<JsonObject, JsonObject> Mutate)[]
            {
                ("formatted-canonical-disposition-drift", (formatted, _) =>
                {
                    JsonObject file = FirstEvidenceFile(formatted);
                    string current = file["disposition"]!.GetValue<string>();
                    file["disposition"] = current == "awaitingIsolatedTypeCheck"
                        ? "blockedBinarySource"
                        : "awaitingIsolatedTypeCheck";
                }),
                ("canonical-undefined-enum", (_, canonical) =>
                    FirstEvidenceFile(canonical)["disposition"] = 99),
                ("canonical-entrypoint-drift", (_, canonical) =>
                    FirstEvidenceFile(canonical)["entrypoints"]!.AsArray().Add("OnTimer")),
                ("canonical-static-feature-drift", (_, canonical) =>
                {
                    JsonNode feature = FirstEvidenceFileWithNonEmptyArray(
                        canonical,
                        "staticFeatures")["staticFeatures"]!.AsArray()[0]!;
                    feature["support"] = (feature["support"]!.GetValue<int>() + 1) % 4;
                }),
                ("canonical-static-finding-drift", (_, canonical) =>
                {
                    JsonNode finding = FirstEvidenceFileWithNonEmptyArray(
                        canonical,
                        "staticFindings")["staticFindings"]!.AsArray()[0]!;
                    finding["severity"] = (finding["severity"]!.GetValue<int>() + 1) % 3;
                }),
                ("canonical-include-drift", (_, canonical) =>
                {
                    JsonNode include = FirstEvidenceFileWithNonEmptyArray(
                        canonical,
                        "includes")["includes"]!.AsArray()[0]!;
                    include["resolution"] = (include["resolution"]!.GetValue<int>() + 1) % 5;
                }),
                ("canonical-dependency-closure-drift", (_, canonical) =>
                    FirstEvidenceFile(canonical)["dependencyClosure"]!["directDependencies"]!
                        .AsArray().Add("other.mqh")),
                ("canonical-lexical-drift", (_, canonical) =>
                    FirstEvidenceFile(canonical)["lexical"]!["tokenCount"] = 999999),
                ("canonical-conversion-finding-drift", (_, canonical) =>
                {
                    JsonNode finding = FirstEvidenceFileWithNonEmptyArray(
                        canonical,
                        "findings")["findings"]!.AsArray()[0]!;
                    finding["severity"] = (finding["severity"]!.GetValue<int>() + 1) % 3;
                }),
                ("omitted-stages", (formatted, _) =>
                    FirstEvidenceFile(formatted).Remove("stages")),
                ("duplicate-stage", (formatted, canonical) =>
                {
                    FirstEvidenceFile(formatted)["stages"]!.AsArray().Add(
                        FirstEvidenceFile(formatted)["stages"]!.AsArray()[0]!.DeepClone());
                    FirstEvidenceFile(canonical)["stages"]!.AsArray().Add(
                        FirstEvidenceFile(canonical)["stages"]!.AsArray()[0]!.DeepClone());
                }),
                ("extra-stage", (formatted, canonical) =>
                {
                    FirstEvidenceFile(formatted)["stages"]!.AsArray().Add(new JsonObject
                    {
                        ["name"] = "sourceIntegrity",
                        ["status"] = "blocked",
                        ["evidenceCode"] = "UNEXPECTED_STAGE"
                    });
                    FirstEvidenceFile(canonical)["stages"]!.AsArray().Add(new JsonObject
                    {
                        ["name"] = 0,
                        ["status"] = 2,
                        ["evidenceCode"] = "UNEXPECTED_STAGE"
                    });
                }),
                ("nested-proof-mismatch", (formatted, _) =>
                    FirstEvidenceFile(formatted)["structural"]!["typeCheckProven"] = true),
                ("canonical-proof-claim", (_, canonical) =>
                    FirstEvidenceFile(canonical)["structural"]!["restrictedIrLoweringProven"] = true),
                ("extra-root-field", (formatted, _) => formatted["unexpected"] = true),
                ("missing-file", (formatted, canonical) =>
                {
                    formatted["files"]!.AsArray().RemoveAt(0);
                    canonical["files"]!.AsArray().RemoveAt(0);
                }),
                ("extra-file", (formatted, canonical) =>
                {
                    formatted["files"]!.AsArray().Add(
                        formatted["files"]!.AsArray()[0]!.DeepClone());
                    canonical["files"]!.AsArray().Add(
                        canonical["files"]!.AsArray()[0]!.DeepClone());
                }),
                ("source-path-drift", (formatted, canonical) =>
                {
                    FirstEvidenceFile(formatted)["relativePath"] = "Experts/other.mq5";
                    FirstEvidenceFile(canonical)["relativePath"] = "Experts/other.mq5";
                }),
                ("source-hash-drift", (formatted, canonical) =>
                {
                    FirstEvidenceFile(formatted)["sourceSha256"] = new string('d', 64);
                    FirstEvidenceFile(canonical)["sourceSha256"] = new string('d', 64);
                })
            };

            foreach ((string name, Action<JsonObject, JsonObject> mutate) in adversarialMutations)
            {
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => InvokeConversionClassificationAsync(
                        database,
                        binding,
                        exact,
                        mutate));
                Assert.Equal(
                    PostgresErrorCodes.InvalidParameterValue,
                    rejected.SqlState);
                Assert.False(string.IsNullOrWhiteSpace(name));
            }

            PostgresException duplicateFormattedRoot =
                await Assert.ThrowsAsync<PostgresException>(
                    () => InvokeConversionClassificationAsync(
                        database,
                        binding,
                        exact,
                        static (_, _) => { },
                        transformFormattedJson: static json =>
                        {
                            int propertyIndex = json.IndexOf(
                                "\"schemaVersion\"",
                                StringComparison.Ordinal);
                            return json.Insert(
                                propertyIndex,
                                "\"schemaVersion\":\"ignored-duplicate\",");
                        }));
            Assert.Equal(
                PostgresErrorCodes.InvalidParameterValue,
                duplicateFormattedRoot.SqlState);

            PostgresException duplicateCanonicalNested =
                await Assert.ThrowsAsync<PostgresException>(
                    () => InvokeConversionClassificationAsync(
                        database,
                        binding,
                        exact,
                        static (_, _) => { },
                        transformCanonicalJson: static json =>
                        {
                            int propertyIndex = json.IndexOf(
                                "\"relativePath\"",
                                StringComparison.Ordinal);
                            return json.Insert(
                                propertyIndex,
                                "\"relativePath\":\"ignored-duplicate.mq5\",");
                        }));
            Assert.Equal(
                PostgresErrorCodes.InvalidParameterValue,
                duplicateCanonicalNested.SqlState);

            const string forgedEmbeddedEvidence =
                "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
            PostgresException forgedEmbedded = await Assert.ThrowsAsync<PostgresException>(
                () => InvokeConversionClassificationAsync(
                    database,
                    binding,
                    exact,
                    (formatted, canonical) =>
                    {
                        formatted["evidenceSha256"] = forgedEmbeddedEvidence;
                        canonical["evidenceSha256"] = forgedEmbeddedEvidence;
                    },
                    embeddedEvidenceSha256: forgedEmbeddedEvidence));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, forgedEmbedded.SqlState);

            const string driftedDependencyGraph =
                "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            string driftedEmbeddedEvidence = ComputeEmbeddedEvidenceSha256(
                exact,
                driftedDependencyGraph);
            PostgresException replayConflict = await Assert.ThrowsAsync<PostgresException>(
                () => InvokeConversionClassificationAsync(
                    database,
                    binding,
                    exact,
                    (formatted, canonical) =>
                    {
                        formatted["dependencyGraphSha256"] = driftedDependencyGraph;
                        canonical["dependencyGraphSha256"] = driftedDependencyGraph;
                        formatted["evidenceSha256"] = driftedEmbeddedEvidence;
                        canonical["evidenceSha256"] = driftedEmbeddedEvidence;
                    },
                    dependencyGraphSha256: driftedDependencyGraph,
                    embeddedEvidenceSha256: driftedEmbeddedEvidence));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, replayConflict.SqlState);

            PostgresException crossTenant = await Assert.ThrowsAsync<PostgresException>(
                () => InvokeConversionClassificationAsync(
                    database,
                    binding,
                    exact,
                    static (_, _) => { },
                    contextTenantId: Guid.CreateVersion7()));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossTenant.SqlState);

            await AssertConversionWorkerRawPrivilegesDeniedAsync(database, binding);
            await AssertControlApiCompatibilityReadBoundaryAsync(database, binding, corpus.Manifest.FileCount);
            await AssertClassificationImmutableAsync(database, binding);

            ConversionClassificationSnapshot afterAttacks =
                await ReadConversionClassificationAsync(database, binding);
            Assert.Equal(1, afterAttacks.Count);
            Assert.Equal(committed.ConversionEmbeddedEvidenceSha256,
                afterAttacks.EmbeddedEvidenceSha256);
            Assert.Equal(committed.ConversionFormattedEvidenceSha256,
                afterAttacks.FormattedEvidenceSha256);
            Assert.Equal(committed.ConversionCanonicalEvidenceSha256,
                afterAttacks.CanonicalEvidenceSha256);
            Assert.Equal(1, afterAttacks.AuditCount);
            Assert.Equal(1, afterAttacks.OutboxCount);
            Assert.False(afterAttacks.HasLaterProofClaim);
            Assert.Equal(0, afterAttacks.PromotionCount);
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
            // Reservation and evidence writes share the same transaction. A
            // failed multi-file import therefore restores the original active
            // job rather than leaving a separately committed reservation.
            Assert.Equal("active", evidence.State);
            Assert.Equal(0, evidence.CorpusCount);
            Assert.Equal(0, evidence.FileCount);
            Assert.Equal(0, evidence.AuditCount);
            Assert.Equal(0, evidence.OutboxCount);
            ConversionClassificationSnapshot classification =
                await ReadConversionClassificationAsync(database, binding);
            Assert.Equal(0, classification.Count);
            Assert.Equal(0, classification.AuditCount);
            Assert.Equal(0, classification.OutboxCount);
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

    private static async Task<(UserActor Owner, UserActor OtherUser, UserActor OtherTenant)>
        SeedCompatibilityActorsAsync(
            PostgresTestDatabase database,
            ImportBinding binding)
    {
        Guid ownerSessionId = Guid.CreateVersion7();
        Guid otherUserId = Guid.CreateVersion7();
        Guid otherUserSessionId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        Guid otherTenantUserId = Guid.CreateVersion7();
        Guid otherTenantSessionId = Guid.CreateVersion7();
        // Authority-guarded identity rows fire U0 statement locks that require
        // an activated tenant context, so every block writes through the same
        // capability-backed tenant transactions production registers instead
        // of raw administrator access.
        var ownerContext = new TenantExecutionContext(
            binding.TenantId,
            binding.UserId,
            binding.CorrelationId);
        await using (var transaction =
            await database.Application.BeginTenantTransactionAsync(ownerContext))
        {
            await using var command = transaction.CreateCommand(
                """
                insert into identity.user_session_families
                    (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
                values
                    (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                     'active', statement_timestamp() + interval '20 minutes')
                """);
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, ownerSessionId);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, binding.UserId);
            command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('1', 64));
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction =
            await database.Application.BeginTenantTransactionAsync(ownerContext))
        {
            await using var command = transaction.CreateCommand(
                """
                insert into identity.user_identities
                    (id, tenant_id, normalized_email, security_state, email_verified_at)
                values
                    (@user_id, @tenant_id, @email, 'active', statement_timestamp());

                insert into identity.user_session_families
                    (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
                values
                    (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                     'active', statement_timestamp() + interval '20 minutes');
                """);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, otherUserId);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            command.Parameters.AddWithValue(
                "email",
                NpgsqlDbType.Text,
                $"user-{otherUserId:N}@example.test");
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, otherUserSessionId);
            command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('2', 64));
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        var otherTenantContext = new TenantExecutionContext(
            otherTenantId,
            otherTenantUserId,
            binding.CorrelationId);
        await using (var transaction =
            await database.Application.BeginTenantTransactionAsync(otherTenantContext))
        {
            await using var command = transaction.CreateCommand(
                """
                insert into identity.tenants (id, slug, display_name)
                values (@tenant_id, @tenant_slug, 'Compatibility isolation tenant');

                insert into identity.user_identities
                    (id, tenant_id, normalized_email, security_state, email_verified_at)
                values
                    (@user_id, @tenant_id, @email, 'active', statement_timestamp());

                insert into identity.user_session_families
                    (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
                values
                    (@session_id, @tenant_id, @user_id, @device_id, @token_hash,
                     'active', statement_timestamp() + interval '20 minutes');
                """);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, otherTenantId);
            command.Parameters.AddWithValue(
                "tenant_slug",
                NpgsqlDbType.Text,
                $"tenant-{otherTenantId:N}");
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, otherTenantUserId);
            command.Parameters.AddWithValue(
                "email",
                NpgsqlDbType.Text,
                $"user-{otherTenantUserId:N}@example.test");
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, otherTenantSessionId);
            command.Parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, new string('3', 64));
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        return (
            new UserActor(
                binding.TenantId,
                binding.UserId,
                ownerSessionId,
                AuthenticationAssurance.Password),
            new UserActor(
                binding.TenantId,
                otherUserId,
                otherUserSessionId,
                AuthenticationAssurance.Password),
            new UserActor(
                otherTenantId,
                otherTenantUserId,
                otherTenantSessionId,
                AuthenticationAssurance.Password));
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
                     capability_sha256, proof_key_id, expires_at)
                values
                    (@job_id, @tenant_id, @user_id, @correlation_id, @source_label,
                     @capability_sha256, repeat('a', 64),
                     statement_timestamp() + @expiry_interval)
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
                     capability_sha256, proof_key_id, expires_at)
                values
                    (@job_id, @tenant_id, @user_id, @correlation_id, @source_label,
                     @capability_sha256, repeat('a', 64),
                     statement_timestamp() + interval '20 minutes')
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

    private static async Task<ConversionClassificationSnapshot>
        ReadConversionClassificationAsync(
            PostgresTestDatabase database,
            ImportBinding binding)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select count(*)
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.file_count
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.total_bytes
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.dependency_graph_sha256
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.embedded_evidence_sha256
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.formatted_evidence_sha256
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select classification.canonical_evidence_sha256
                 from governance.strategy_conversion_classifications as classification
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                (select count(*)
                 from governance.strategy_conversion_classifications as classification
                 cross join lateral pg_catalog.jsonb_array_elements(
                     classification.formatted_evidence_document -> 'files')
                     with ordinality as evidence_file(document, ordinal)
                 join governance.strategy_source_files as source_file
                   on source_file.tenant_id = classification.tenant_id
                  and source_file.corpus_id = classification.corpus_id
                  and source_file.manifest_order = evidence_file.ordinal - 1
                  and source_file.relative_path = evidence_file.document ->> 'relativePath'
                  and source_file.source_sha256 = evidence_file.document ->> 'sourceSha256'
                 where classification.tenant_id = @tenant_id
                   and classification.corpus_id = @corpus_id),
                exists
                (
                    select 1
                    from governance.strategy_conversion_classifications as classification
                    cross join lateral pg_catalog.jsonb_array_elements(
                        classification.formatted_evidence_document -> 'files')
                        as evidence_file(document)
                    where classification.tenant_id = @tenant_id
                      and classification.corpus_id = @corpus_id
                      and
                      (
                          evidence_file.document -> 'structural' -> 'fullGrammarParseProven'
                              is distinct from 'false'::jsonb
                          or evidence_file.document -> 'structural' -> 'typeCheckProven'
                              is distinct from 'false'::jsonb
                          or evidence_file.document -> 'structural' -> 'restrictedIrLoweringProven'
                              is distinct from 'false'::jsonb
                          or exists
                          (
                              select 1
                              from pg_catalog.jsonb_array_elements(
                                  evidence_file.document -> 'stages') as stage(document)
                              where stage.document ->> 'name' in
                                  ('typeChecking', 'restrictedIrLowering')
                                and stage.document ->> 'status' = 'passed'
                          )
                      )
                ),
                (select count(*)
                 from audit.audit_events as audit_event
                 where audit_event.tenant_id = @tenant_id
                   and audit_event.target_type = 'strategy_conversion_classification'
                   and audit_event.target_id = @corpus_id::text),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.aggregate_type = 'strategy_conversion_classification'
                   and message.aggregate_id = @corpus_id::text),
                exists
                (
                    select 1
                    from governance.strategy_conversion_classifications as classification
                    join audit.audit_events as audit_event
                      on audit_event.tenant_id = classification.tenant_id
                     and audit_event.id = classification.audit_event_id
                    where classification.tenant_id = @tenant_id
                      and classification.corpus_id = @corpus_id
                      and audit_event.payload = pg_catalog.jsonb_build_object(
                          'canonicalEvidenceSha256', classification.canonical_evidence_sha256,
                          'corpusId', classification.corpus_id::text,
                          'embeddedEvidenceSha256', classification.embedded_evidence_sha256,
                          'formattedEvidenceSha256', classification.formatted_evidence_sha256,
                          'verification', 'static-conversion-classification-only')
                ),
                exists
                (
                    select 1
                    from governance.strategy_conversion_classifications as classification
                    join messaging.outbox_messages as message
                      on message.tenant_id = classification.tenant_id
                     and message.id = classification.outbox_message_id
                    where classification.tenant_id = @tenant_id
                      and classification.corpus_id = @corpus_id
                      and message.message_type =
                          'strategy.source_corpus.conversion_classification_persisted.v1'
                      and message.payload = pg_catalog.jsonb_build_object(
                          'canonicalEvidenceSha256', classification.canonical_evidence_sha256,
                          'corpusId', classification.corpus_id::text,
                          'embeddedEvidenceSha256', classification.embedded_evidence_sha256,
                          'formattedEvidenceSha256', classification.formatted_evidence_sha256,
                          'verification', 'static-conversion-classification-only')
                ),
                (select count(*)
                 from governance.strategy_versions as strategy_version
                 where strategy_version.tenant_id = @tenant_id)
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
        command.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, binding.ImportJobId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ConversionClassificationSnapshot(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            reader.GetBoolean(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetBoolean(11),
            reader.GetBoolean(12),
            reader.GetInt64(13));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task InvokeConversionClassificationAsync(
        PostgresTestDatabase database,
        ImportBinding binding,
        Mql5ConversionCorpusEvidence evidence,
        Action<JsonObject, JsonObject> mutate,
        string? dependencyGraphSha256 = null,
        string? embeddedEvidenceSha256 = null,
        Guid? contextTenantId = null,
        Func<string, string>? transformFormattedJson = null,
        Func<string, string>? transformCanonicalJson = null)
    {
        var formatted = JsonNode.Parse(Mql5ConversionEvidenceFormatter.ToJson(evidence))!
            .AsObject();
        var canonical = JsonNode.Parse(CanonicalJson.Serialize(evidence))!.AsObject();
        mutate(formatted, canonical);
        string formattedJson = formatted.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }) + "\n";
        string canonicalJson = canonical.ToJsonString();
        if (transformFormattedJson is not null)
        {
            formattedJson = transformFormattedJson(formattedJson);
        }

        if (transformCanonicalJson is not null)
        {
            canonicalJson = transformCanonicalJson(canonicalJson);
        }

        byte[] formattedContent = Encoding.UTF8.GetBytes(formattedJson);
        byte[] canonicalContent = Encoding.UTF8.GetBytes(canonicalJson);
        try
        {
            string dispositionCounts = CanonicalJson.Serialize(
                evidence.Files
                    .GroupBy(file => ToConversionDispositionStorage(file.Disposition),
                        StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(),
                        StringComparer.Ordinal));
            var context = new TenantExecutionContext(
                contextTenantId ?? binding.TenantId,
                binding.UserId,
                binding.CorrelationId);
            await using TenantPostgresTransaction transaction =
                await database.ConversionWorker.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select *
                from control.persist_strategy_conversion_classification(
                    @corpus_id, @schema_version, @analyzer_version,
                    @input_static_schema_version, @input_static_analyzer_version,
                    @input_corpus_sha256, @dependency_graph_sha256,
                    @embedded_evidence_sha256, @formatted_evidence_sha256,
                    @canonical_evidence_sha256, @file_count, @total_bytes,
                    @disposition_counts, @formatted_evidence_content,
                    @canonical_evidence_content, @audit_event_id, @outbox_message_id)
                """);
            command.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, binding.ImportJobId);
            command.Parameters.AddWithValue("schema_version", NpgsqlDbType.Text, evidence.SchemaVersion);
            command.Parameters.AddWithValue("analyzer_version", NpgsqlDbType.Text, evidence.AnalyzerVersion);
            command.Parameters.AddWithValue(
                "input_static_schema_version",
                NpgsqlDbType.Text,
                evidence.InputStaticSchemaVersion);
            command.Parameters.AddWithValue(
                "input_static_analyzer_version",
                NpgsqlDbType.Text,
                evidence.InputStaticAnalyzerVersion);
            command.Parameters.AddWithValue(
                "input_corpus_sha256",
                NpgsqlDbType.Text,
                evidence.InputCorpusSha256);
            command.Parameters.AddWithValue(
                "dependency_graph_sha256",
                NpgsqlDbType.Text,
                dependencyGraphSha256 ?? evidence.DependencyGraphSha256);
            command.Parameters.AddWithValue(
                "embedded_evidence_sha256",
                NpgsqlDbType.Text,
                embeddedEvidenceSha256 ?? evidence.EvidenceSha256);
            command.Parameters.AddWithValue(
                "formatted_evidence_sha256",
                NpgsqlDbType.Text,
                Sha256Utf8(formattedJson));
            command.Parameters.AddWithValue(
                "canonical_evidence_sha256",
                NpgsqlDbType.Text,
                Sha256Utf8(canonicalJson));
            command.Parameters.AddWithValue("file_count", NpgsqlDbType.Integer, evidence.FileCount);
            command.Parameters.AddWithValue("total_bytes", NpgsqlDbType.Bigint, evidence.TotalBytes);
            command.Parameters.AddWithValue(
                "disposition_counts",
                NpgsqlDbType.Jsonb,
                dispositionCounts);
            command.Parameters.AddWithValue(
                "formatted_evidence_content",
                NpgsqlDbType.Bytea,
                formattedContent);
            command.Parameters.AddWithValue(
                "canonical_evidence_content",
                NpgsqlDbType.Bytea,
                canonicalContent);
            command.Parameters.AddWithValue(
                "audit_event_id",
                NpgsqlDbType.Uuid,
                Guid.CreateVersion7());
            command.Parameters.AddWithValue(
                "outbox_message_id",
                NpgsqlDbType.Uuid,
                Guid.CreateVersion7());
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(formattedContent);
            CryptographicOperations.ZeroMemory(canonicalContent);
        }
    }

    private static JsonObject FirstEvidenceFile(JsonObject root) =>
        root["files"]!.AsArray()[0]!.AsObject();

    private static JsonObject FirstEvidenceFileWithNonEmptyArray(
        JsonObject root,
        string propertyName) => root["files"]!.AsArray()
        .Select(static node => node!.AsObject())
        .First(file => file[propertyName]!.AsArray().Count > 0);

    private static async Task AssertConversionWorkerRawPrivilegesDeniedAsync(
        PostgresTestDatabase database,
        ImportBinding binding)
    {
        string[] statements =
        [
            "select control.json_has_duplicate_object_keys('{}'::json)",
            "select count(*) from governance.strategy_conversion_classifications",
            "insert into governance.strategy_conversion_classifications (tenant_id) values (@tenant_id)",
            "update governance.strategy_conversion_classifications set created_at = created_at",
            "delete from governance.strategy_conversion_classifications"
        ];
        foreach (string sql in statements)
        {
            var context = new TenantExecutionContext(
                binding.TenantId,
                binding.UserId,
                binding.CorrelationId);
            await using TenantPostgresTransaction transaction =
                await database.ConversionWorker.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(sql);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }
    }

    private static async Task AssertClassificationImmutableAsync(
        PostgresTestDatabase database,
        ImportBinding binding)
    {
        string[] statements =
        [
            """
            update governance.strategy_conversion_classifications
            set created_at = created_at
            where tenant_id = @tenant_id and corpus_id = @corpus_id
            """,
            """
            delete from governance.strategy_conversion_classifications
            where tenant_id = @tenant_id and corpus_id = @corpus_id
            """
        ];
        foreach (string sql in statements)
        {
            await using NpgsqlConnection connection =
                await database.Administrator.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            command.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, binding.ImportJobId);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }
    }

    private static async Task AssertControlApiCompatibilityReadBoundaryAsync(
        PostgresTestDatabase database,
        ImportBinding binding,
        int expectedFileCount)
    {
        var ownerContext = new TenantExecutionContext(
            binding.TenantId,
            binding.UserId,
            binding.CorrelationId);
        await using (TenantPostgresTransaction transaction =
            await database.ControlApi.BeginTenantTransactionAsync(ownerContext))
        {
            await using NpgsqlCommand allowed = transaction.CreateCommand(
                """
                select count(*)::integer
                from governance.strategy_source_corpora as corpus
                join governance.strategy_conversion_classifications as classification
                  on classification.tenant_id = corpus.tenant_id
                 and classification.corpus_id = corpus.id
                 and classification.user_id = corpus.user_id
                join governance.strategy_source_files as source_file
                  on source_file.tenant_id = corpus.tenant_id
                 and source_file.corpus_id = corpus.id
                 and source_file.user_id = corpus.user_id
                where corpus.id = @corpus_id
                  and corpus.tenant_id = @tenant_id
                  and corpus.user_id = @user_id
                  and corpus.file_count = @file_count
                  and corpus.state = 'static_analyzed'
                  and source_file.manifest_order >= 0
                  and length(source_file.relative_path) > 0
                  and source_file.source_kind in ('expert_or_program', 'header')
                  and pg_catalog.jsonb_typeof(source_file.features) = 'array'
                  and length(source_file.disposition) > 0
                """);
            allowed.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, binding.ImportJobId);
            allowed.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
            allowed.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, binding.UserId);
            allowed.Parameters.AddWithValue("file_count", NpgsqlDbType.Integer, expectedFileCount);
            Assert.Equal(expectedFileCount, await allowed.ExecuteScalarAsync());
            await transaction.CommitAsync();
        }

        string[] forbiddenReads =
        [
            "select source_content from governance.strategy_source_files",
            "select findings from governance.strategy_source_files",
            "select corpus_sha256 from governance.strategy_source_corpora",
            "select formatted_evidence_document from governance.strategy_conversion_classifications"
        ];
        foreach (string sql in forbiddenReads)
        {
            await using TenantPostgresTransaction transaction =
                await database.ControlApi.BeginTenantTransactionAsync(ownerContext);
            await using NpgsqlCommand forbidden = transaction.CreateCommand(sql);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => forbidden.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }

        var otherActorContext = new TenantExecutionContext(
            binding.TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        await using (TenantPostgresTransaction transaction =
            await database.ControlApi.BeginTenantTransactionAsync(otherActorContext))
        {
            await using NpgsqlCommand isolated = transaction.CreateCommand(
                """
                select count(*)::integer
                from governance.strategy_source_corpora as corpus
                join governance.strategy_conversion_classifications as classification
                  on classification.tenant_id = corpus.tenant_id
                 and classification.corpus_id = corpus.id
                join governance.strategy_source_files as source_file
                  on source_file.tenant_id = corpus.tenant_id
                 and source_file.corpus_id = corpus.id
                """);
            Assert.Equal(0, await isolated.ExecuteScalarAsync());
            await transaction.CommitAsync();
        }
    }

    private static string ToConversionDispositionStorage(
        Mql5ConversionEvidenceDisposition disposition) => disposition switch
    {
        Mql5ConversionEvidenceDisposition.BlockedAllNulSource => "blockedAllNulSource",
        Mql5ConversionEvidenceDisposition.BlockedBinarySource => "blockedBinarySource",
        Mql5ConversionEvidenceDisposition.BlockedInvalidSyntax => "blockedInvalidSyntax",
        Mql5ConversionEvidenceDisposition.BlockedMissingDependency => "blockedMissingDependency",
        Mql5ConversionEvidenceDisposition.BlockedExternalDependencySnapshot =>
            "blockedExternalDependencySnapshot",
        Mql5ConversionEvidenceDisposition.BlockedDependencyCycle => "blockedDependencyCycle",
        Mql5ConversionEvidenceDisposition.BlockedUnsupportedSemantics =>
            "blockedUnsupportedSemantics",
        Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck => "awaitingIsolatedTypeCheck",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition))
    };

    private static string Sha256Utf8(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ComputeEmbeddedEvidenceSha256(
        Mql5ConversionCorpusEvidence evidence,
        string dependencyGraphSha256)
    {
        var canonical = new StringBuilder();
        AppendLengthPrefixed(canonical, evidence.SchemaVersion);
        AppendLengthPrefixed(canonical, evidence.AnalyzerVersion);
        AppendLengthPrefixed(canonical, evidence.InputStaticSchemaVersion);
        AppendLengthPrefixed(canonical, evidence.InputStaticAnalyzerVersion);
        AppendLengthPrefixed(canonical, evidence.InputCorpusSha256);
        AppendLengthPrefixed(canonical, dependencyGraphSha256);
        foreach (Mql5ConversionFileEvidence file in evidence.Files)
        {
            AppendLengthPrefixed(canonical, file.RelativePath);
            AppendLengthPrefixed(canonical, file.EvidenceSha256);
        }

        return Sha256Utf8(canonical.ToString());
    }

    private static void AppendLengthPrefixed(StringBuilder target, string value) =>
        target.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);

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

    private sealed record ConversionClassificationSnapshot(
        long Count,
        int? FileCount,
        long? TotalBytes,
        string? DependencyGraphSha256,
        string? EmbeddedEvidenceSha256,
        string? FormattedEvidenceSha256,
        string? CanonicalEvidenceSha256,
        long ExactBoundFileCount,
        bool HasLaterProofClaim,
        long AuditCount,
        long OutboxCount,
        bool HasSafeAuditPayload,
        bool HasSafeOutboxPayload,
        long PromotionCount);
}
