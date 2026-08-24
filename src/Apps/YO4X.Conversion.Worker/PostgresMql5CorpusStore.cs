using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;

namespace YO4X.Conversion.Worker;

public sealed class Mql5CorpusPersistenceRequest : IDisposable
{
    private byte[]? capability;
    private readonly object lifecycleLock = new();

    public Mql5CorpusPersistenceRequest(
        Guid importJobId,
        byte[] capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (importJobId == Guid.Empty
            || capability.Length != 32
            || capability.All(static value => value == 0))
        {
            throw new ArgumentException(
                "The corpus persistence capability binding is invalid.",
                nameof(capability));
        }

        ImportJobId = importJobId;
        this.capability = capability.ToArray();
    }

    public Guid ImportJobId { get; }

    internal byte[] CopyCapability()
    {
        lock (lifecycleLock)
        {
            return (capability
                ?? throw new ObjectDisposedException(nameof(Mql5CorpusPersistenceRequest)))
                .ToArray();
        }
    }

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            byte[]? owned = Interlocked.Exchange(ref capability, null);
            if (owned is not null)
            {
                CryptographicOperations.ZeroMemory(owned);
            }
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString() =>
        $"Mql5CorpusPersistenceRequest {{ ImportJobId = {ImportJobId:D}, Capability = [REDACTED] }}";
}

public sealed record Mql5CorpusPersistenceResult(
    Guid ImportId,
    string CorpusSha256,
    string ManifestSha256,
    int FileCount,
    string ConversionEmbeddedEvidenceSha256,
    string ConversionFormattedEvidenceSha256,
    string ConversionCanonicalEvidenceSha256,
    bool Replayed);

public sealed class PostgresMql5CorpusStore
{
    private readonly PostgresDatabase database;

    public PostgresMql5CorpusStore(PostgresDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<Mql5CorpusPersistenceResult> PersistAsync(
        Mql5CorpusPersistenceRequest request,
        Mql5AnalyzedCorpus corpus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(corpus);
        ValidateRequest(request);
        using Mql5AnalyzedCorpus ownedCorpus = SnapshotCorpus(corpus);
        Mql5ConversionCorpusEvidence rebuiltEvidence =
            new Mql5ConversionEvidenceAnalyzer().Analyze(ownedCorpus.Documents);
        return await PersistCoreAsync(
                request,
                ownedCorpus,
                rebuiltEvidence,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Mql5CorpusPersistenceResult> PersistAsync(
        Mql5CorpusPersistenceRequest request,
        Mql5AnalyzedCorpus corpus,
        Mql5ConversionCorpusEvidence conversionEvidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(conversionEvidence);
        ValidateRequest(request);
        using Mql5AnalyzedCorpus ownedCorpus = SnapshotCorpus(corpus);
        Mql5ConversionCorpusEvidence rebuiltEvidence =
            ValidateAndRebuildConversionEvidence(ownedCorpus, conversionEvidence);
        return await PersistCoreAsync(
                request,
                ownedCorpus,
                rebuiltEvidence,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Mql5CorpusPersistenceResult> PersistCoreAsync(
        Mql5CorpusPersistenceRequest request,
        Mql5AnalyzedCorpus corpus,
        Mql5ConversionCorpusEvidence conversionEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(conversionEvidence);
        ValidateRequest(request);
        Mql5CorpusManifest manifest = ValidateAndRebuildCorpus(corpus);
        ValidateConversionEvidenceBinding(manifest, conversionEvidence);

        string manifestJson = Mql5InventoryFormatter.ToJson(manifest);
        string report = Mql5InventoryFormatter.ToMarkdown(manifest);
        string manifestSha256 = Sha256Utf8(manifestJson);
        string reportSha256 = Sha256Utf8(report);
        string formattedConversionJson = Mql5ConversionEvidenceFormatter.ToJson(
            conversionEvidence);
        string canonicalConversionJson = CanonicalJson.Serialize(conversionEvidence);
        string formattedConversionSha256 = Sha256Utf8(formattedConversionJson);
        string canonicalConversionSha256 = Sha256Utf8(canonicalConversionJson);
        string conversionDispositionCounts = CanonicalJson.Serialize(
            conversionEvidence.Files
                .GroupBy(file => ToStorage(file.Disposition), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        byte[] capability = request.CopyCapability();
        try
        {
            (TenantPostgresTransaction transaction, StrategyImportReservation reservation) =
                await BeginStrategyImportTransactionAsync(
                    request.ImportJobId,
                    capability,
                    cancellationToken)
                .ConfigureAwait(false);
            await using (transaction)
            {
            ValidateReservation(reservation, request.ImportJobId);

            await AcquirePersistenceLockAsync(transaction, request.ImportJobId, cancellationToken)
                .ConfigureAwait(false);

            StrategyImportReservation lockedReservation = await ReadReservationUnderPersistenceLockAsync(
                    transaction,
                    request.ImportJobId,
                    capability,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateReservation(lockedReservation, request.ImportJobId);
            ValidateUnchangedAuthorityBinding(reservation, lockedReservation);
            if (string.Equals(lockedReservation.State, "consumed", StringComparison.Ordinal))
            {
                ValidateReplayEvidence(
                    lockedReservation,
                    manifest,
                    manifestSha256,
                    reportSha256);
                StrategyConversionClassificationReceipt replayReceipt =
                    await PersistClassificationAsync(
                            transaction,
                            request.ImportJobId,
                            conversionEvidence,
                            formattedConversionJson,
                            formattedConversionSha256,
                            canonicalConversionJson,
                            canonicalConversionSha256,
                            conversionDispositionCounts,
                            Guid.CreateVersion7(),
                            Guid.CreateVersion7(),
                            cancellationToken)
                        .ConfigureAwait(false);
                if (!replayReceipt.Replayed)
                {
                    throw new InvalidOperationException(
                        "A consumed import did not return its immutable conversion evidence receipt.");
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return CreateReplayResult(
                    request.ImportJobId,
                    manifest,
                    manifestSha256,
                    replayReceipt);
            }

            reservation = lockedReservation;
            string dispositionCounts = CanonicalJson.Serialize(
                manifest.Files
                    .GroupBy(file => ToStorage(file.Disposition), StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
            byte[] manifestContent = Encoding.UTF8.GetBytes(manifestJson);
            byte[] reportContent = Encoding.UTF8.GetBytes(report);
            try
            {
                await using NpgsqlCommand insertCorpus = transaction.CreateCommand(
                """
                insert into governance.strategy_source_corpora
                (
                    id, tenant_id, user_id, import_job_id, reservation_id,
                    source_label, schema_version, analyzer_version, corpus_sha256,
                    manifest_sha256, report_sha256, file_count, total_bytes,
                    disposition_counts, manifest, manifest_content, report_content,
                    state
                )
                values
                (
                    @id, @tenant_id, @user_id, @import_job_id, @reservation_id,
                    @source_label, @schema_version, @analyzer_version, @corpus_sha256,
                    @manifest_sha256, @report_sha256, @file_count, @total_bytes,
                    @disposition_counts, @manifest, @manifest_content, @report_content,
                    'static_analyzed'
                )
                """);
                AddUuid(insertCorpus, "id", request.ImportJobId);
                AddUuid(insertCorpus, "tenant_id", reservation.TenantId);
                AddUuid(insertCorpus, "user_id", reservation.UserId);
                AddUuid(insertCorpus, "import_job_id", request.ImportJobId);
                AddUuid(insertCorpus, "reservation_id", reservation.ReservationId!.Value);
                insertCorpus.Parameters.AddWithValue("source_label", NpgsqlDbType.Text, reservation.SourceLabel);
                insertCorpus.Parameters.AddWithValue("schema_version", NpgsqlDbType.Text, manifest.SchemaVersion);
                insertCorpus.Parameters.AddWithValue("analyzer_version", NpgsqlDbType.Text, manifest.AnalyzerVersion);
                insertCorpus.Parameters.AddWithValue("corpus_sha256", NpgsqlDbType.Text, manifest.CorpusSha256);
                insertCorpus.Parameters.AddWithValue("manifest_sha256", NpgsqlDbType.Text, manifestSha256);
                insertCorpus.Parameters.AddWithValue("report_sha256", NpgsqlDbType.Text, reportSha256);
                insertCorpus.Parameters.AddWithValue("file_count", NpgsqlDbType.Integer, manifest.FileCount);
                insertCorpus.Parameters.AddWithValue("total_bytes", NpgsqlDbType.Bigint, manifest.TotalBytes);
                insertCorpus.Parameters.AddWithValue("disposition_counts", NpgsqlDbType.Jsonb, dispositionCounts);
                insertCorpus.Parameters.AddWithValue("manifest", NpgsqlDbType.Jsonb, manifestJson);
                insertCorpus.Parameters.AddWithValue("manifest_content", NpgsqlDbType.Bytea, manifestContent);
                insertCorpus.Parameters.AddWithValue("report_content", NpgsqlDbType.Bytea, reportContent);
                await insertCorpus.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(manifestContent);
                CryptographicOperations.ZeroMemory(reportContent);
            }

            Dictionary<string, Mql5SourceDocument> documents = corpus.Documents.ToDictionary(
                document => document.RelativePath,
                StringComparer.Ordinal);
            for (int manifestOrder = 0; manifestOrder < manifest.Files.Count; manifestOrder++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mql5SourceManifest file = manifest.Files[manifestOrder];
                Mql5SourceDocument document = documents[file.RelativePath];
                await InsertFileAsync(
                        transaction,
                        request.ImportJobId,
                        reservation,
                        manifestOrder,
                        file,
                        document.Content,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            StrategyConversionClassificationReceipt classificationReceipt =
                await PersistClassificationAsync(
                        transaction,
                        request.ImportJobId,
                        conversionEvidence,
                        formattedConversionJson,
                        formattedConversionSha256,
                        canonicalConversionJson,
                        canonicalConversionSha256,
                        conversionDispositionCounts,
                        Guid.CreateVersion7(),
                        Guid.CreateVersion7(),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (classificationReceipt.Replayed)
            {
                throw new InvalidOperationException(
                    "A new import unexpectedly replayed conversion classification evidence.");
            }

            await CompleteImportAsync(
                    transaction,
                    request.ImportJobId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new Mql5CorpusPersistenceResult(
                request.ImportJobId,
                manifest.CorpusSha256,
                manifestSha256,
                manifest.FileCount,
                classificationReceipt.EmbeddedEvidenceSha256,
                classificationReceipt.FormattedEvidenceSha256,
                classificationReceipt.CanonicalEvidenceSha256,
                false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private async Task<(TenantPostgresTransaction Transaction, StrategyImportReservation Reservation)>
        BeginStrategyImportTransactionAsync(
        Guid importJobId,
        byte[] capability,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        TenantPostgresTransaction? tenantTransaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = new(
                """
                select *
                from control.acquire_strategy_import_job(
                    @job_id, @capability)
                """,
                connection,
                transaction);
            AddUuid(command, "job_id", importJobId);
            command.Parameters.AddWithValue("capability", NpgsqlDbType.Bytea, capability);
            StrategyImportReservation reservation = await ReadReservationAsync(
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateReservation(reservation, importJobId);

            var context = new TenantExecutionContext(
                reservation.TenantId,
                reservation.UserId,
                reservation.CorrelationId,
                null);
            tenantTransaction = new TenantPostgresTransaction(connection, transaction, context);
            await tenantTransaction.VerifyActivatedContextAsync(cancellationToken)
                .ConfigureAwait(false);
            return (tenantTransaction, reservation);
        }
        catch
        {
            if (tenantTransaction is not null)
            {
                await tenantTransaction.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }

                await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task<StrategyImportReservation> ReadReservationUnderPersistenceLockAsync(
        TenantPostgresTransaction transaction,
        Guid importJobId,
        byte[] capability,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.acquire_strategy_import_job(
                @job_id, @capability)
            """);
        AddUuid(command, "job_id", importJobId);
        command.Parameters.AddWithValue("capability", NpgsqlDbType.Bytea, capability);
        return await ReadReservationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StrategyImportReservation> ReadReservationAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("The strategy import capability was not accepted.");
        }

        var result = new StrategyImportReservation(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetInt64(14));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The strategy import capability returned an ambiguous binding.");
        }

        return result;
    }

    private static async Task AcquirePersistenceLockAsync(
        TenantPostgresTransaction transaction,
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand import = transaction.CreateCommand(
            "select control.acquire_strategy_import_persistence_lock(@job_id)");
        AddUuid(import, "job_id", importJobId);
        await import.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteImportAsync(
        TenantPostgresTransaction transaction,
        Guid importJobId,
        Guid auditEventId,
        Guid outboxMessageId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select control.complete_strategy_import_job(@job_id, @audit_id, @outbox_id)");
        AddUuid(command, "job_id", importJobId);
        AddUuid(command, "audit_id", auditEventId);
        AddUuid(command, "outbox_id", outboxMessageId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StrategyConversionClassificationReceipt>
        PersistClassificationAsync(
            TenantPostgresTransaction transaction,
            Guid importJobId,
            Mql5ConversionCorpusEvidence evidence,
            string formattedJson,
            string formattedSha256,
            string canonicalJson,
            string canonicalSha256,
            string dispositionCounts,
            Guid auditEventId,
            Guid outboxMessageId,
            CancellationToken cancellationToken)
    {
        byte[] formattedContent = Encoding.UTF8.GetBytes(formattedJson);
        byte[] canonicalContent = Encoding.UTF8.GetBytes(canonicalJson);
        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    persisted_embedded_evidence_sha256,
                    persisted_formatted_evidence_sha256,
                    persisted_canonical_evidence_sha256,
                    recorded_at_utc,
                    persisted_audit_event_id,
                    persisted_outbox_message_id,
                    replayed
                from control.persist_strategy_conversion_classification(
                    @corpus_id,
                    @schema_version,
                    @analyzer_version,
                    @input_static_schema_version,
                    @input_static_analyzer_version,
                    @input_corpus_sha256,
                    @dependency_graph_sha256,
                    @embedded_evidence_sha256,
                    @formatted_evidence_sha256,
                    @canonical_evidence_sha256,
                    @file_count,
                    @total_bytes,
                    @disposition_counts,
                    @formatted_evidence_content,
                    @canonical_evidence_content,
                    @audit_event_id,
                    @outbox_message_id)
                """);
            AddUuid(command, "corpus_id", importJobId);
            command.Parameters.AddWithValue(
                "schema_version",
                NpgsqlDbType.Text,
                evidence.SchemaVersion);
            command.Parameters.AddWithValue(
                "analyzer_version",
                NpgsqlDbType.Text,
                evidence.AnalyzerVersion);
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
                evidence.DependencyGraphSha256);
            command.Parameters.AddWithValue(
                "embedded_evidence_sha256",
                NpgsqlDbType.Text,
                evidence.EvidenceSha256);
            command.Parameters.AddWithValue(
                "formatted_evidence_sha256",
                NpgsqlDbType.Text,
                formattedSha256);
            command.Parameters.AddWithValue(
                "canonical_evidence_sha256",
                NpgsqlDbType.Text,
                canonicalSha256);
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
            AddUuid(command, "audit_event_id", auditEventId);
            AddUuid(command, "outbox_message_id", outboxMessageId);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Conversion classification persistence returned no receipt.");
            }

            var receipt = new StrategyConversionClassificationReceipt(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetBoolean(6));
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Conversion classification persistence returned an ambiguous receipt.");
            }

            if (!FixedTimeEquals(receipt.EmbeddedEvidenceSha256, evidence.EvidenceSha256)
                || !FixedTimeEquals(receipt.FormattedEvidenceSha256, formattedSha256)
                || !FixedTimeEquals(receipt.CanonicalEvidenceSha256, canonicalSha256)
                || receipt.RecordedAtUtc == default
                || receipt.AuditEventId == Guid.Empty
                || receipt.OutboxMessageId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Conversion classification persistence returned a mismatched receipt.");
            }

            return receipt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(formattedContent);
            CryptographicOperations.ZeroMemory(canonicalContent);
        }
    }

    private static async Task InsertFileAsync(
        TenantPostgresTransaction transaction,
        Guid importJobId,
        StrategyImportReservation reservation,
        int manifestOrder,
        Mql5SourceManifest file,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string actualSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (content.LongLength != file.ByteLength || !FixedTimeEquals(actualSha256, file.Sha256))
        {
            throw new InvalidDataException("A source file changed after static inventory analysis.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into governance.strategy_source_files
            (
                id, tenant_id, corpus_id, user_id, import_job_id,
                reservation_id, manifest_order, relative_path, source_kind, byte_length,
                source_sha256, text_encoding, entrypoints, includes, features,
                findings, disposition, verification, source_content
            )
            values
            (
                @id, @tenant_id, @corpus_id, @user_id, @import_job_id,
                @reservation_id, @manifest_order, @relative_path, @source_kind, @byte_length,
                @source_sha256, @text_encoding, @entrypoints, @includes, @features,
                @findings, @disposition, @verification, @source_content
            )
            """);
        AddUuid(command, "id", Guid.CreateVersion7());
        AddUuid(command, "tenant_id", reservation.TenantId);
        AddUuid(command, "corpus_id", importJobId);
        AddUuid(command, "user_id", reservation.UserId);
        AddUuid(command, "import_job_id", importJobId);
        AddUuid(command, "reservation_id", reservation.ReservationId!.Value);
        command.Parameters.AddWithValue("manifest_order", NpgsqlDbType.Integer, manifestOrder);
        command.Parameters.AddWithValue("relative_path", NpgsqlDbType.Text, file.RelativePath);
        command.Parameters.AddWithValue("source_kind", NpgsqlDbType.Text, ToStorage(file.Kind));
        command.Parameters.AddWithValue("byte_length", NpgsqlDbType.Bigint, file.ByteLength);
        command.Parameters.AddWithValue("source_sha256", NpgsqlDbType.Text, file.Sha256);
        command.Parameters.AddWithValue("text_encoding", NpgsqlDbType.Text, file.TextEncoding);
        command.Parameters.AddWithValue("entrypoints", NpgsqlDbType.Array | NpgsqlDbType.Text, file.Entrypoints.ToArray());
        command.Parameters.AddWithValue("includes", NpgsqlDbType.Jsonb, Mql5InventoryFormatter.ToJsonFragment(file.Includes));
        command.Parameters.AddWithValue("features", NpgsqlDbType.Jsonb, Mql5InventoryFormatter.ToJsonFragment(file.Features));
        command.Parameters.AddWithValue("findings", NpgsqlDbType.Jsonb, Mql5InventoryFormatter.ToJsonFragment(file.Findings));
        command.Parameters.AddWithValue("disposition", NpgsqlDbType.Text, ToStorage(file.Disposition));
        command.Parameters.AddWithValue("verification", NpgsqlDbType.Jsonb, Mql5InventoryFormatter.ToJsonFragment(file.Verification));
        command.Parameters.AddWithValue("source_content", NpgsqlDbType.Bytea, content);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRequest(Mql5CorpusPersistenceRequest request)
    {
        byte[] capability = request.CopyCapability();
        try
        {
            if (request.ImportJobId == Guid.Empty
                || capability.Length != 32
                || capability.All(static value => value == 0))
            {
                throw new ArgumentException("The corpus persistence capability binding is invalid.", nameof(request));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private static void ValidateReservation(
        StrategyImportReservation reservation,
        Guid importJobId)
    {
        bool reserved = string.Equals(reservation.State, "reserved", StringComparison.Ordinal);
        bool consumed = string.Equals(reservation.State, "consumed", StringComparison.Ordinal);
        if (reservation.TenantId == Guid.Empty
            || reservation.UserId == Guid.Empty
            || reservation.CorrelationId == Guid.Empty
            || reservation.SourceLabel is not { Length: >= 1 and <= 100 }
            || reservation.SourceLabel.Any(character => character is not (>= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_' or '.'))
            || !reserved && !consumed
            || reserved && (reservation.ReservationId != importJobId
                || reservation.ReservationExpiresAt is null
                || reservation.CorpusId is not null)
            || consumed && (reservation.ReservationId != importJobId
                || reservation.CorpusId != importJobId))
        {
            throw new InvalidOperationException("The strategy import capability returned an invalid binding.");
        }
    }

    private static void ValidateUnchangedAuthorityBinding(
        StrategyImportReservation beforeLock,
        StrategyImportReservation underLock)
    {
        if (underLock.TenantId != beforeLock.TenantId
            || underLock.UserId != beforeLock.UserId
            || underLock.CorrelationId != beforeLock.CorrelationId
            || !string.Equals(
                underLock.SourceLabel,
                beforeLock.SourceLabel,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The strategy import capability authority binding changed during persistence.");
        }
    }

    private static Mql5CorpusPersistenceResult CreateReplayResult(
        Guid importJobId,
        Mql5CorpusManifest manifest,
        string manifestSha256,
        StrategyConversionClassificationReceipt classification) =>
        new(
            importJobId,
            manifest.CorpusSha256,
            manifestSha256,
            manifest.FileCount,
            classification.EmbeddedEvidenceSha256,
            classification.FormattedEvidenceSha256,
            classification.CanonicalEvidenceSha256,
            true);

    private static void ValidateReplayEvidence(
        StrategyImportReservation reservation,
        Mql5CorpusManifest manifest,
        string manifestSha256,
        string reportSha256)
    {
        if (!string.Equals(reservation.SchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(reservation.AnalyzerVersion, manifest.AnalyzerVersion, StringComparison.Ordinal)
            || !FixedTimeEquals(reservation.CorpusSha256 ?? string.Empty, manifest.CorpusSha256)
            || !FixedTimeEquals(reservation.ManifestSha256 ?? string.Empty, manifestSha256)
            || !FixedTimeEquals(reservation.ReportSha256 ?? string.Empty, reportSha256)
            || reservation.FileCount != manifest.FileCount
            || reservation.TotalBytes != manifest.TotalBytes)
        {
            throw new InvalidOperationException("The import job is bound to different immutable corpus evidence.");
        }
    }

    internal static Mql5AnalyzedCorpus SnapshotCorpus(Mql5AnalyzedCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        IReadOnlyList<Mql5SourceDocument> suppliedDocuments = corpus.Documents;
        int documentCount;
        try
        {
            documentCount = suppliedDocuments.Count;
        }
        catch (Exception exception) when (IsUntrustedCollectionFailure(exception))
        {
            throw new InvalidDataException(
                "The source corpus document collection could not be bounded.",
                exception);
        }

        if (documentCount is < 1 or > Mql5CorpusInventoryJob.MaximumFileCount)
        {
            throw new InvalidDataException("The source corpus file count is outside persistence bounds.");
        }

        var ownedDocuments = new List<Mql5SourceDocument>(documentCount);
        try
        {
            long totalBytes = 0;
            for (int index = 0; index < documentCount; index++)
            {
                Mql5SourceDocument document;
                try
                {
                    document = suppliedDocuments[index]
                        ?? throw new InvalidDataException(
                            "The source corpus contains a null document.");
                }
                catch (Exception exception) when (IsUntrustedCollectionFailure(exception))
                {
                    throw new InvalidDataException(
                        "The source corpus document collection changed during bounded snapshot.",
                        exception);
                }

                string relativePath = document.RelativePath;
                byte[] content = document.Content;
                if (string.IsNullOrWhiteSpace(relativePath)
                    || relativePath.Length > 4_096
                    || content is null)
                {
                    throw new InvalidDataException(
                        "The source corpus contains invalid document metadata.");
                }

                if (content.LongLength > Mql5CorpusInventoryJob.MaximumFileBytes)
                {
                    throw new InvalidDataException(
                        "An MQL5 source file exceeds the persistence size limit.");
                }

                totalBytes = checked(totalBytes + content.LongLength);
                if (totalBytes > Mql5CorpusInventoryJob.MaximumCorpusBytes)
                {
                    throw new InvalidDataException(
                        "The MQL5 source corpus exceeds the persistence size limit.");
                }

                var ownedDocument = new Mql5SourceDocument(
                    relativePath,
                    content.ToArray());
                try
                {
                    Mql5SourceSecretScanner.EnsureNoHighConfidenceSecrets(ownedDocument);
                    ownedDocuments.Add(ownedDocument);
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(ownedDocument.Content);
                    throw;
                }
            }

            Mql5CorpusManifest rebuilt = new Mql5StaticInventoryAnalyzer().Analyze(ownedDocuments);
            // Caller-supplied manifest graphs are deliberately ignored here.
            // Only bounded owned bytes drive the trusted manifest and all later
            // persistence evidence.
            return new Mql5AnalyzedCorpus(rebuilt, ownedDocuments);
        }
        catch
        {
            foreach (Mql5SourceDocument document in ownedDocuments)
            {
                CryptographicOperations.ZeroMemory(document.Content);
            }

            throw;
        }
    }

    private static bool IsUntrustedCollectionFailure(Exception exception) => exception is not
        (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal static Mql5CorpusManifest ValidateAndRebuildCorpus(Mql5AnalyzedCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        Mql5CorpusManifest rebuilt = new Mql5StaticInventoryAnalyzer().Analyze(corpus.Documents);
        if (corpus.Manifest.FileCount != corpus.Documents.Count
            || corpus.Manifest.Files.Count != corpus.Documents.Count
            || !string.Equals(
                Mql5InventoryFormatter.ToJson(corpus.Manifest),
                Mql5InventoryFormatter.ToJson(rebuilt),
                StringComparison.Ordinal)
            || corpus.Manifest.Files.Any(file => !file.Verification.StaticInventoryCompleted
                || file.Verification.ParsedAndTypeChecked
                || file.Verification.SemanticConversionProven
                || file.Verification.MetaEditorCompileProven
                || file.Verification.ReferenceParityProven
                || file.Verification.DemoRuntimeProven))
        {
            throw new InvalidDataException(
                "The corpus does not exactly match trusted static-inventory analysis.");
        }

        return rebuilt;
    }

    internal static Mql5ConversionCorpusEvidence ValidateAndRebuildConversionEvidence(
        Mql5AnalyzedCorpus corpus,
        Mql5ConversionCorpusEvidence supplied)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(supplied);
        Mql5ConversionCorpusEvidence rebuilt =
            new Mql5ConversionEvidenceAnalyzer().Analyze(corpus.Documents);
        if (!string.Equals(
                Mql5ConversionEvidenceFormatter.ToJson(supplied),
                Mql5ConversionEvidenceFormatter.ToJson(rebuilt),
                StringComparison.Ordinal)
            || !string.Equals(
                CanonicalJson.Serialize(supplied),
                CanonicalJson.Serialize(rebuilt),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Conversion classification does not exactly match trusted analysis of the owned source bytes.");
        }

        return rebuilt;
    }

    private static void ValidateConversionEvidenceBinding(
        Mql5CorpusManifest manifest,
        Mql5ConversionCorpusEvidence evidence)
    {
        if (!string.Equals(
                evidence.SchemaVersion,
                Mql5ConversionEvidenceAnalyzer.SchemaVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                evidence.AnalyzerVersion,
                Mql5ConversionEvidenceAnalyzer.AnalyzerVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                evidence.InputStaticSchemaVersion,
                manifest.SchemaVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                evidence.InputStaticAnalyzerVersion,
                manifest.AnalyzerVersion,
                StringComparison.Ordinal)
            || !FixedTimeEquals(evidence.InputCorpusSha256, manifest.CorpusSha256)
            || evidence.FileCount != manifest.FileCount
            || evidence.TotalBytes != manifest.TotalBytes
            || evidence.Files.Count != manifest.Files.Count)
        {
            throw new InvalidDataException(
                "Conversion classification is not bound to the exact static source corpus.");
        }

        for (int index = 0; index < manifest.Files.Count; index++)
        {
            Mql5SourceManifest source = manifest.Files[index];
            Mql5ConversionFileEvidence conversion = evidence.Files[index];
            if (!string.Equals(source.RelativePath, conversion.RelativePath, StringComparison.Ordinal)
                || !FixedTimeEquals(source.Sha256, conversion.SourceSha256)
                || !string.Equals(source.TextEncoding, conversion.TextEncoding, StringComparison.Ordinal)
                || source.Kind != conversion.Kind
                || source.Disposition != conversion.StaticDisposition
                || conversion.Structural.FullGrammarParseProven
                || conversion.Structural.TypeCheckProven
                || conversion.Structural.RestrictedIrLoweringProven
                || conversion.Stages.Count != 6
                || conversion.Stages.Any(stage =>
                    (stage.Name is Mql5EvidenceStageName.TypeChecking
                        or Mql5EvidenceStageName.RestrictedIrLowering)
                    && stage.Status == Mql5EvidenceStageStatus.Passed))
            {
                throw new InvalidDataException(
                    "Conversion classification contains mismatched source or proof evidence.");
            }
        }
    }

    private static string ToStorage(Mql5SourceKind value) => value switch
    {
        Mql5SourceKind.ExpertOrProgram => "expert_or_program",
        Mql5SourceKind.Header => "header",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToStorage(Mql5StaticDisposition value) => value switch
    {
        Mql5StaticDisposition.NeedsSemanticValidation => "needs_semantic_validation",
        Mql5StaticDisposition.NeedsSource => "needs_source",
        Mql5StaticDisposition.Unsupported => "unsupported",
        Mql5StaticDisposition.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToStorage(Mql5ConversionEvidenceDisposition value) => value switch
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
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string Sha256Utf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private sealed record StrategyImportReservation(
        Guid TenantId,
        Guid UserId,
        Guid CorrelationId,
        string SourceLabel,
        string State,
        Guid? ReservationId,
        DateTimeOffset? ReservationExpiresAt,
        Guid? CorpusId,
        string? CorpusSha256,
        string? ManifestSha256,
        string? ReportSha256,
        string? SchemaVersion,
        string? AnalyzerVersion,
        int? FileCount,
        long? TotalBytes);

    private sealed record StrategyConversionClassificationReceipt(
        string EmbeddedEvidenceSha256,
        string FormattedEvidenceSha256,
        string CanonicalEvidenceSha256,
        DateTimeOffset RecordedAtUtc,
        Guid AuditEventId,
        Guid OutboxMessageId,
        bool Replayed);
}
