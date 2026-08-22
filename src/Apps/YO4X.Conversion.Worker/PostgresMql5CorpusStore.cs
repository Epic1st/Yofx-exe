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
        Mql5CorpusManifest manifest = ValidateAndRebuildCorpus(corpus);

        string manifestJson = Mql5InventoryFormatter.ToJson(manifest);
        string report = Mql5InventoryFormatter.ToMarkdown(manifest);
        string manifestSha256 = Sha256Utf8(manifestJson);
        string reportSha256 = Sha256Utf8(report);
        byte[] capability = request.CopyCapability();
        try
        {
            StrategyImportReservation reservation = await AcquireReservationAsync(
                    request.ImportJobId,
                    capability,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateReservation(reservation, request.ImportJobId);
            if (string.Equals(reservation.State, "consumed", StringComparison.Ordinal))
            {
                ValidateReplayEvidence(reservation, manifest, manifestSha256, reportSha256);
                return CreateReplayResult(request.ImportJobId, manifest, manifestSha256);
            }

            await using TenantPostgresTransaction transaction = await database.BeginTenantTransactionAsync(
                    new TenantExecutionContext(
                        reservation.TenantId,
                        reservation.UserId,
                        reservation.CorrelationId,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);

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
                return CreateReplayResult(request.ImportJobId, manifest, manifestSha256);
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
                false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private async Task<StrategyImportReservation> AcquireReservationAsync(
        Guid importJobId,
        byte[] capability,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken)
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
        StrategyImportReservation result = await ReadReservationAsync(command, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
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
        string manifestSha256) =>
        new(
            importJobId,
            manifest.CorpusSha256,
            manifestSha256,
            manifest.FileCount,
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
}
