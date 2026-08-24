using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Application;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Postgres;

/// <summary>
/// Execute-only PostgreSQL adapter for the Supervisor's durable strategy-event
/// transaction. It never evaluates a strategy and never creates a broker
/// command; committed actions are risk-evaluation inputs only.
/// </summary>
public sealed class PostgresStrategyEventTransactionStore :
    IStrategyEventIntakeStore,
    IStrategyEventTransactionStore
{
    private const int MinimumClaimSeconds = 1;
    private const int MaximumClaimSeconds = 300;

    private readonly PostgresDatabase database;
    private readonly int claimSeconds;

    public PostgresStrategyEventTransactionStore(
        PostgresDatabase database,
        TimeSpan? claimLifetime = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        TimeSpan lifetime = claimLifetime ?? TimeSpan.FromSeconds(30);
        if (lifetime.Ticks % TimeSpan.TicksPerSecond != 0
            || lifetime.TotalSeconds is < MinimumClaimSeconds or > MaximumClaimSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(claimLifetime),
                $"The claim lifetime must be {MinimumClaimSeconds}-{MaximumClaimSeconds} whole seconds.");
        }

        claimSeconds = checked((int)lifetime.TotalSeconds);
    }

    public async Task<StrategyEventIntakeReceipt> PersistAsync(
        TenantExecutionContext context,
        StrategyEventInputEvidence input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        byte[] eventContent = Encoding.UTF8.GetBytes(input.EventJson);
        byte[] snapshotContent = Encoding.UTF8.GetBytes(input.SnapshotJson);
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.BeginTenantTransactionAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select *
                from control.persist_strategy_event(
                    @deployment_id, @worker_instance_id, @generation, @sequence,
                    @event_id, @event_kind, @event_contract_version, @event_sha256,
                    @snapshot_sequence, @snapshot_contract_version, @snapshot_sha256,
                    @event_content, @snapshot_content)
                """);
            AddReferenceParameters(command, input.Reference);
            command.Parameters.AddWithValue("event_content", NpgsqlDbType.Bytea, eventContent);
            command.Parameters.AddWithValue(
                "snapshot_content",
                NpgsqlDbType.Bytea,
                snapshotContent);

            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            StrategyEventPostgresResultContract.RequirePersistSchema(reader);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw MissingRow("strategy-event intake");
            }

            DateTimeOffset persistedAtUtc = reader.GetFieldValue<DateTimeOffset>(
                reader.GetOrdinal("persisted_at_utc"));
            bool replayed = reader.GetBoolean(reader.GetOrdinal("replayed"));
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw DuplicateRow("strategy-event intake");
            }

            await reader.CloseAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StrategyEventIntakeReceipt(
                input.Reference,
                input.EventJson,
                input.SnapshotJson,
                persistedAtUtc,
                replayed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(eventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
        }
    }

    public async Task<StrategyEventClaimResult> ClaimAsync(
        TenantExecutionContext context,
        StrategyEventReference reference,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        RequireIdentifier(claimToken, nameof(claimToken));

        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context, cancellationToken)
                .ConfigureAwait(false);

        StrategyEventCommitReceipt? durableCommit = await ReadCommitAsync(
                transaction,
                reference,
                cancellationToken)
            .ConfigureAwait(false);
        if (durableCommit is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return StrategyEventClaimResult.AlreadyCommitted(durableCommit);
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.claim_strategy_event(
                @deployment_id, @worker_instance_id, @generation, @sequence,
                @event_id, @event_kind, @event_contract_version, @event_sha256,
                @snapshot_sequence, @snapshot_contract_version, @snapshot_sha256,
                @claim_token, @claim_seconds)
            """);
        AddReferenceParameters(command, reference);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("claim_seconds", NpgsqlDbType.Integer, claimSeconds);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        StrategyEventPostgresResultContract.RequireClaimSchema(reader);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw MissingRow("strategy-event claim");
        }

        string disposition = reader.GetString(reader.GetOrdinal("claim_disposition"));
        string code = reader.GetString(reader.GetOrdinal("claim_code"));
        StrategyEventPostgresResultContract.RequireClaimShape(reader, disposition, code);
        StrategyEventClaimResult result = disposition switch
        {
            "no_work" => ReadNoWork(code),
            "claimed" => ReadClaim(reader, reference, claimToken),
            "already_committed" => ReadAlreadyCommitted(reader),
            _ => throw new InvalidOperationException(
                "PostgreSQL returned an unknown strategy-event claim disposition.")
        };

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw DuplicateRow("strategy-event claim");
        }

        await reader.CloseAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<StrategyEventCommitReceipt> CommitAsync(
        TenantExecutionContext context,
        StrategyEventCommitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Claim);
        ArgumentNullException.ThrowIfNull(request.Evidence);
        StrategyEventReference reference = request.Claim.Reference;
        StrategyEventCommitDocument document = request.Evidence.Document;
        if (document.TenantId != context.TenantId
            || document.DeploymentId != reference.DeploymentId
            || document.WorkerInstanceId != reference.WorkerInstanceId
            || document.Generation != reference.Generation
            || document.EventSequence != reference.Sequence
            || document.EventId != reference.EventId
            || document.ClaimToken != request.Claim.ClaimToken)
        {
            throw new ArgumentException(
                "The strategy-event commit request has conflicting authority bindings.",
                nameof(request));
        }

        byte[] evidenceContent = Encoding.UTF8.GetBytes(request.Evidence.CanonicalJson);
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.BeginTenantTransactionAsync(context, cancellationToken)
                    .ConfigureAwait(false);

            StrategyEventCommitReceipt? durableCommit = await ReadCommitAsync(
                    transaction,
                    reference,
                    cancellationToken)
                .ConfigureAwait(false);
            if (durableCommit is not null)
            {
                if (!FixedTimeEquals(
                        durableCommit.Evidence.Sha256,
                        request.Evidence.Sha256)
                    || !FixedTimeEquals(
                        durableCommit.Evidence.CanonicalJson,
                        request.Evidence.CanonicalJson))
                {
                    throw new InvalidOperationException(
                        "The strategy event was already committed with different evidence.");
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return durableCommit;
            }

            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select *
                from control.commit_strategy_event(
                    @deployment_id, @worker_instance_id, @generation, @sequence,
                    @event_id, @claim_token, @evidence_content, @evidence_sha256)
                """);
            AddEventKeyParameters(command, reference);
            command.Parameters.AddWithValue(
                "claim_token",
                NpgsqlDbType.Uuid,
                request.Claim.ClaimToken);
            command.Parameters.AddWithValue(
                "evidence_content",
                NpgsqlDbType.Bytea,
                evidenceContent);
            command.Parameters.AddWithValue(
                "evidence_sha256",
                NpgsqlDbType.Text,
                request.Evidence.Sha256);

            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            StrategyEventPostgresResultContract.RequireCommitSchema(reader);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw MissingRow("strategy-event commit");
            }

            StrategyEventCommitReceipt receipt = ReadPersistedCommitReceipt(
                reader,
                "strategy-event commit",
                reader.GetBoolean(reader.GetOrdinal("replayed")));
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw DuplicateRow("strategy-event commit");
            }

            await reader.CloseAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(evidenceContent);
        }
    }

    private static StrategyEventClaimResult ReadNoWork(string code)
        => StrategyEventClaimResult.NoWork(code);

    private static StrategyEventClaimResult ReadClaim(
        NpgsqlDataReader reader,
        StrategyEventReference reference,
        Guid claimToken)
    {
        byte[] eventContent = RequiredBytes(reader, "event_content");
        byte[] snapshotContent = RequiredBytes(reader, "snapshot_content");
        byte[] stateContent = RequiredBytes(reader, "prior_state_content");
        try
        {
            StrategyEventInputEvidence input =
                StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                    eventContent,
                    snapshotContent,
                    reference,
                    "strategy-event input");
            string stateSha256 = reader.GetString(reader.GetOrdinal("prior_state_sha256"));
            StrategyState state = StrategyCanonicalEvidenceCodec.ReadState(
                reader.GetInt64(reader.GetOrdinal("prior_state_version")),
                stateContent,
                stateSha256,
                "strategy state");
            var claim = new ClaimedStrategyEvent(
                reference,
                claimToken,
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("authority_now_utc")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("claim_expires_at_utc")),
                input.Envelope,
                input.Snapshot,
                state,
                input.EventJson,
                input.SnapshotJson,
                state.PayloadJson,
                stateSha256,
                reader.GetBoolean(reader.GetOrdinal("replayed")));
            return StrategyEventClaimResult.Claimed(claim);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(eventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
            CryptographicOperations.ZeroMemory(stateContent);
        }
    }

    private static StrategyEventClaimResult ReadAlreadyCommitted(DbDataReader reader) =>
        StrategyEventClaimResult.AlreadyCommitted(
            ReadClaimCommitReceipt(
                reader,
                "strategy-event claim replay"));

    private static async Task<StrategyEventCommitReceipt?> ReadCommitAsync(
        TenantPostgresTransaction transaction,
        StrategyEventReference reference,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.read_strategy_event_commit(
                @deployment_id, @worker_instance_id, @generation, @sequence, @event_id)
            """);
        AddEventKeyParameters(command, reference);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        StrategyEventPostgresResultContract.RequireReadCommitSchema(reader);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StrategyEventCommitReceipt receipt = ReadPersistedCommitReceipt(
            reader,
            "durable strategy-event commit",
            replayed: true);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw DuplicateRow("durable strategy-event commit");
        }

        return receipt;
    }

    private static StrategyEventCommitReceipt ReadPersistedCommitReceipt(
        DbDataReader reader,
        string evidenceName,
        bool replayed) => ReadCommitReceipt(
            reader,
            evidenceName,
            "persisted_commit_evidence_content",
            "persisted_commit_evidence_sha256",
            "recorded_at_utc",
            replayed);

    private static StrategyEventCommitReceipt ReadClaimCommitReceipt(
        DbDataReader reader,
        string evidenceName) => ReadCommitReceipt(
            reader,
            evidenceName,
            "commit_evidence_content",
            "commit_evidence_sha256",
            "committed_at_utc",
            replayed: true);

    private static StrategyEventCommitReceipt ReadCommitReceipt(
        DbDataReader reader,
        string evidenceName,
        string contentColumn,
        string digestColumn,
        string timeColumn,
        bool replayed)
    {
        byte[] content = RequiredBytes(reader, contentColumn);
        try
        {
            string sha256 = reader.GetString(reader.GetOrdinal(digestColumn));
            return new StrategyEventCommitReceipt(
                StrategyCanonicalEvidenceCodec.ReadCommitEvidence(
                    content,
                    sha256,
                    evidenceName),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(timeColumn)),
                replayed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private static void AddReferenceParameters(
        NpgsqlCommand command,
        StrategyEventReference reference)
    {
        AddEventKeyParameters(command, reference);
        command.Parameters.AddWithValue(
            "event_kind",
            NpgsqlDbType.Integer,
            (int)reference.EventKind);
        command.Parameters.AddWithValue(
            "event_contract_version",
            NpgsqlDbType.Integer,
            reference.EventContractVersion);
        command.Parameters.AddWithValue(
            "event_sha256",
            NpgsqlDbType.Text,
            reference.EventSha256);
        command.Parameters.AddWithValue(
            "snapshot_sequence",
            NpgsqlDbType.Bigint,
            reference.SnapshotSequence);
        command.Parameters.AddWithValue(
            "snapshot_contract_version",
            NpgsqlDbType.Integer,
            reference.SnapshotContractVersion);
        command.Parameters.AddWithValue(
            "snapshot_sha256",
            NpgsqlDbType.Text,
            reference.SnapshotSha256);
    }

    private static void AddEventKeyParameters(
        NpgsqlCommand command,
        StrategyEventReference reference)
    {
        command.Parameters.AddWithValue(
            "deployment_id",
            NpgsqlDbType.Uuid,
            reference.DeploymentId);
        command.Parameters.AddWithValue(
            "worker_instance_id",
            NpgsqlDbType.Uuid,
            reference.WorkerInstanceId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, reference.Generation);
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, reference.Sequence);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Uuid, reference.EventId);
    }

    private static byte[] RequiredBytes(DbDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? throw new InvalidOperationException(
                $"PostgreSQL omitted required {columnName} evidence.")
            : reader.GetFieldValue<byte[]>(ordinal);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
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

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }

    private static InvalidOperationException MissingRow(string operation) => new(
        $"PostgreSQL returned no row for the {operation} capability.");

    private static InvalidOperationException DuplicateRow(string operation) => new(
        $"PostgreSQL returned multiple rows for the {operation} capability.");
}

internal static class StrategyEventPostgresResultContract
{
    private static readonly string[] PersistColumns =
    [
        "persisted_at_utc",
        "replayed"
    ];

    private static readonly string[] ClaimColumns =
    [
        "claim_disposition",
        "claim_code",
        "authority_now_utc",
        "claim_expires_at_utc",
        "event_content",
        "snapshot_content",
        "prior_state_version",
        "prior_state_content",
        "prior_state_sha256",
        "commit_evidence_content",
        "commit_evidence_sha256",
        "committed_at_utc",
        "replayed"
    ];

    private static readonly string[] CommitColumns =
    [
        "persisted_commit_evidence_content",
        "persisted_commit_evidence_sha256",
        "recorded_at_utc",
        "replayed"
    ];

    private static readonly string[] ReadCommitColumns =
    [
        "persisted_commit_evidence_content",
        "persisted_commit_evidence_sha256",
        "recorded_at_utc"
    ];

    private static readonly string[] ClaimEvidenceColumns =
    [
        "authority_now_utc",
        "claim_expires_at_utc",
        "event_content",
        "snapshot_content",
        "prior_state_version",
        "prior_state_content",
        "prior_state_sha256"
    ];

    private static readonly string[] CommitEvidenceColumns =
    [
        "commit_evidence_content",
        "commit_evidence_sha256",
        "committed_at_utc"
    ];

    private static readonly HashSet<string> NoWorkCodes = new(StringComparer.Ordinal)
    {
        "strategy_event_no_generation_head",
        "strategy_event_not_persisted",
        "strategy_event_waiting_for_prior_sequence",
        "strategy_event_claim_held"
    };

    internal static void RequirePersistSchema(DbDataReader reader) =>
        RequireExactSchema(reader, PersistColumns, "strategy-event intake");

    internal static void RequireClaimSchema(DbDataReader reader) =>
        RequireExactSchema(reader, ClaimColumns, "strategy-event claim");

    internal static void RequireCommitSchema(DbDataReader reader) =>
        RequireExactSchema(reader, CommitColumns, "strategy-event commit");

    internal static void RequireReadCommitSchema(DbDataReader reader) =>
        RequireExactSchema(reader, ReadCommitColumns, "strategy-event commit read");

    internal static void RequireClaimShape(
        DbDataReader reader,
        string disposition,
        string code)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(disposition);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        int replayedOrdinal = reader.GetOrdinal("replayed");
        if (reader.IsDBNull(replayedOrdinal))
        {
            throw MalformedClaim();
        }

        bool replayed = reader.GetBoolean(replayedOrdinal);
        bool valid = disposition switch
        {
            "no_work" => NoWorkCodes.Contains(code)
                && !replayed
                && AllNull(reader, ClaimEvidenceColumns)
                && AllNull(reader, CommitEvidenceColumns),
            "claimed" => IsValidClaimedCode(code, replayed)
                && AllNotNull(reader, ClaimEvidenceColumns)
                && AllNull(reader, CommitEvidenceColumns),
            "already_committed" => string.Equals(
                    code,
                    "strategy_event_already_committed",
                    StringComparison.Ordinal)
                && replayed
                && AllNull(reader, ClaimEvidenceColumns)
                && AllNotNull(reader, CommitEvidenceColumns),
            _ => false
        };

        if (!valid)
        {
            throw MalformedClaim();
        }
    }

    private static bool IsValidClaimedCode(string code, bool replayed) => replayed
        ? string.Equals(code, "strategy_event_claim_replayed", StringComparison.Ordinal)
        : string.Equals(code, "strategy_event_claimed", StringComparison.Ordinal)
            || string.Equals(
                code,
                "strategy_event_expired_claim_recovered",
                StringComparison.Ordinal);

    private static void RequireExactSchema(
        DbDataReader reader,
        string[] expectedColumns,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.FieldCount != expectedColumns.Length)
        {
            throw MalformedSchema(operation);
        }

        for (int ordinal = 0; ordinal < expectedColumns.Length; ordinal++)
        {
            if (!string.Equals(
                    reader.GetName(ordinal),
                    expectedColumns[ordinal],
                    StringComparison.Ordinal))
            {
                throw MalformedSchema(operation);
            }
        }
    }

    private static bool AllNull(DbDataReader reader, IEnumerable<string> columnNames) =>
        columnNames.All(name => reader.IsDBNull(reader.GetOrdinal(name)));

    private static bool AllNotNull(DbDataReader reader, IEnumerable<string> columnNames) =>
        columnNames.All(name => !reader.IsDBNull(reader.GetOrdinal(name)));

    private static InvalidOperationException MalformedSchema(string operation) => new(
        $"PostgreSQL returned a malformed {operation} result schema.");

    private static InvalidOperationException MalformedClaim() => new(
        "PostgreSQL returned a malformed strategy-event claim result.");
}
