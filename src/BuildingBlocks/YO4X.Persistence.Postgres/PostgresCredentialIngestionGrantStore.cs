using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.SecretCoordination;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// Invokes the execute-only PostgreSQL credential capabilities. The runtime
/// role cannot read stored proof hashes or directly mutate grant, account,
/// audit, or outbox tables.
/// </summary>
public sealed class PostgresCredentialIngestionGrantStore : ICredentialIngestionGrantStore
{
    private static readonly Guid IngestionServiceActorId =
        Guid.Parse("9fda7b52-620b-4eb9-a34c-632163a6078f");
    private const string InvalidProofMessage = "Credential ingestion proof is invalid or inactive.";
    private const string InvalidProofSqlState = PostgresErrorCodes.InsufficientPrivilege;
    private const string ReservationLostSqlState = "Y0001";
    private const string CompletionConflictSqlState = "Y0002";

    private const string ReadinessSql = """
        select
            current_user = 'yo4x_secret_ingestion'
            and has_function_privilege(
                current_user,
                'control.reserve_credential_ingestion_grant(uuid,uuid,text,text,text,integer,uuid,uuid)',
                'EXECUTE')
            and has_function_privilege(
                current_user,
                'control.release_credential_ingestion_grant(uuid,uuid,bigint,uuid,uuid)',
                'EXECUTE')
            and has_function_privilege(
                current_user,
                'control.complete_credential_ingestion_grant(uuid,uuid,bigint,text,text,uuid,uuid)',
                'EXECUTE')
            and not has_function_privilege(
                current_user,
                'control.expire_secret_credential_ingestion_grant(uuid,bigint,uuid,uuid)',
                'EXECUTE')
            and not has_function_privilege(
                current_user,
                'control.acquire_u0_authority_lock()',
                'EXECUTE')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'control'
                      and relation.relname = 'credential_ingestion_grants'),
                'SELECT')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'control'
                      and relation.relname = 'credential_ingestion_grants'),
                'UPDATE')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'control'
                      and relation.relname = 'credential_ingestion_grants'),
                'INSERT')
            and not has_table_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'control'
                      and relation.relname = 'credential_ingestion_grants'),
                'DELETE')
            and not has_table_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'control'
                      and relation.relname = 'credential_ingestion_grants'),
                'TRUNCATE')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'operations'
                      and relation.relname = 'broker_accounts'),
                'SELECT')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'operations'
                      and relation.relname = 'broker_accounts'),
                'UPDATE')
            and not has_any_column_privilege(
                current_user,
                (select relation.oid
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'operations'
                      and relation.relname = 'broker_accounts'),
                'INSERT')
            and not has_table_privilege(
                current_user,
                (select relation.oid from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'operations' and relation.relname = 'broker_accounts'),
                'DELETE')
            and not has_table_privilege(
                current_user,
                (select relation.oid from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'operations' and relation.relname = 'broker_accounts'),
                'TRUNCATE')
            and not has_table_privilege(
                current_user,
                (select relation.oid from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'audit' and relation.relname = 'audit_events'),
                'INSERT')
            and not has_table_privilege(
                current_user,
                (select relation.oid from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
                    where namespace.nspname = 'messaging' and relation.relname = 'outbox_messages'),
                'INSERT')
            and not has_schema_privilege(current_user, 'operations', 'USAGE')
            and not has_schema_privilege(current_user, 'audit', 'USAGE')
            and not has_schema_privilege(current_user, 'messaging', 'USAGE')
        """;

    private readonly PostgresDatabase _database;

    public PostgresCredentialIngestionGrantStore(PostgresDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = new(ReadinessSql, connection);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        }
        catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<CredentialIngestionReservation> ReserveAsync(
        CredentialIngestionProof proof,
        DateTimeOffset now,
        TimeSpan reservationDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (reservationDuration < TimeSpan.FromSeconds(1)
            || reservationDuration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservationDuration),
                "A credential-ingestion reservation must be between one second and one minute.");
        }

        _ = now.ToUniversalTime();
        Guid requestedReservationId = Identifiers.NewId();
        try
        {
            await using TenantPostgresTransaction transaction =
                await _database.BeginTenantTransactionAsync(
                    CreateContext(proof.TenantId, proof.GrantId),
                    cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    grant_id, tenant_id, broker_account_id, operation_type,
                    reservation_id, disposition, completed_at, grant_version
                from control.reserve_credential_ingestion_grant(
                    @grant_id,
                    @reservation_id,
                    @bearer_hash,
                    @nonce_hash,
                    @origin,
                    @duration_seconds,
                    @audit_event_id,
                    @outbox_message_id)
                """);
            command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, proof.GrantId);
            command.Parameters.AddWithValue("reservation_id", NpgsqlDbType.Uuid, requestedReservationId);
            command.Parameters.AddWithValue("bearer_hash", NpgsqlDbType.Text, proof.BearerHash);
            command.Parameters.AddWithValue("nonce_hash", NpgsqlDbType.Text, proof.NonceHash);
            command.Parameters.AddWithValue("origin", NpgsqlDbType.Text, proof.Origin);
            command.Parameters.AddWithValue(
                "duration_seconds",
                NpgsqlDbType.Integer,
                checked((int)reservationDuration.TotalSeconds));
            command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, Identifiers.NewId());
            command.Parameters.AddWithValue("outbox_message_id", NpgsqlDbType.Uuid, Identifiers.NewId());

            CredentialIngestionReservation? reservation = null;
            bool invalid = false;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw InvalidProof();
                }

                string disposition = reader.GetString(5);
                invalid = string.Equals(disposition, "invalid", StringComparison.Ordinal);
                if (!invalid)
                {
                    reservation = new CredentialIngestionReservation(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetGuid(2),
                        ParseOperation(reader.GetString(3)),
                        reader.GetGuid(4),
                        ParseDisposition(disposition),
                        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                        reader.GetInt64(7));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (invalid || reservation is null)
            {
                throw InvalidProof();
            }

            return reservation;
        }
        catch (PostgresException exception) when (exception.SqlState == InvalidProofSqlState)
        {
            throw InvalidProof();
        }
    }

    public async Task ReleaseBeforeWriteAsync(
        CredentialIngestionReservation reservation,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken)
    {
        ValidateReservation(reservation);
        _ = releasedAt.ToUniversalTime();
        try
        {
            await using TenantPostgresTransaction transaction =
                await _database.BeginTenantTransactionAsync(
                    CreateContext(reservation.TenantId, reservation.GrantId),
                    cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select grant_version, account_version, completed_at, next_state
                from control.release_credential_ingestion_grant(
                    @grant_id,
                    @reservation_id,
                    @expected_version,
                    @audit_event_id,
                    @outbox_message_id)
                """);
            AddReservationParameters(command, reservation);
            command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, Identifiers.NewId());
            command.Parameters.AddWithValue("outbox_message_id", NpgsqlDbType.Uuid, Identifiers.NewId());
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not long)
            {
                throw ReservationLost();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == ReservationLostSqlState)
        {
            throw ReservationLost();
        }
    }

    public async Task<CredentialIngestionCompletion> CompleteAsync(
        CredentialIngestionReservation reservation,
        SecretWriteReceipt receipt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ValidateReservation(reservation);
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.State != SecretWriteReceiptState.Stored
            || !receipt.IsBoundTo(reservation.ToWriteBinding()))
        {
            throw CompletionConflict();
        }

        _ = completedAt.ToUniversalTime();
        string completionDigest = RequireDigest(receipt.CompletionDigest, nameof(receipt.CompletionDigest));
        try
        {
            await using TenantPostgresTransaction transaction =
                await _database.BeginTenantTransactionAsync(
                    CreateContext(reservation.TenantId, reservation.GrantId),
                    cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select grant_version, account_version, completed_at, replayed
                from control.complete_credential_ingestion_grant(
                    @grant_id,
                    @reservation_id,
                    @expected_version,
                    @opaque_reference,
                    @completion_digest,
                    @audit_event_id,
                    @outbox_message_id)
                """);
            AddReservationParameters(command, reservation);
            command.Parameters.AddWithValue("opaque_reference", NpgsqlDbType.Text, receipt.OpaqueReference);
            command.Parameters.AddWithValue("completion_digest", NpgsqlDbType.Text, completionDigest);
            command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, Identifiers.NewId());
            command.Parameters.AddWithValue("outbox_message_id", NpgsqlDbType.Uuid, Identifiers.NewId());

            DateTimeOffset persistedCompletedAt;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw ReservationLost();
                }

                persistedCompletedAt = reader.GetFieldValue<DateTimeOffset>(2);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CredentialIngestionCompletion(reservation.GrantId, persistedCompletedAt);
        }
        catch (PostgresException exception) when (exception.SqlState == CompletionConflictSqlState)
        {
            throw CompletionConflict();
        }
        catch (PostgresException exception) when (exception.SqlState == ReservationLostSqlState)
        {
            throw ReservationLost();
        }
    }

    private static void AddReservationParameters(
        NpgsqlCommand command,
        CredentialIngestionReservation reservation)
    {
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, reservation.GrantId);
        command.Parameters.AddWithValue("reservation_id", NpgsqlDbType.Uuid, reservation.AttemptId);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, reservation.GrantVersion);
    }

    private static void ValidateReservation(CredentialIngestionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.GrantId == Guid.Empty
            || reservation.TenantId == Guid.Empty
            || reservation.BrokerAccountId == Guid.Empty
            || reservation.AttemptId == Guid.Empty
            || reservation.GrantVersion < 0)
        {
            throw new ArgumentException(
                "A complete credential-ingestion reservation is required.",
                nameof(reservation));
        }

        if (reservation.Disposition != CredentialIngestionReservationDisposition.Acquired)
        {
            throw new ArgumentException("Only an acquired reservation may be changed.", nameof(reservation));
        }
    }

    private static TenantExecutionContext CreateContext(Guid tenantId, Guid grantId) =>
        new(tenantId, IngestionServiceActorId, grantId);

    private static CredentialIngestionOperation ParseOperation(string value) => value switch
    {
        "create" => CredentialIngestionOperation.Create,
        "rotate" => CredentialIngestionOperation.Rotate,
        _ => throw new InvalidOperationException("A persisted credential-ingestion operation is invalid.")
    };

    private static CredentialIngestionReservationDisposition ParseDisposition(string value) => value switch
    {
        "acquired" => CredentialIngestionReservationDisposition.Acquired,
        "in_progress" => CredentialIngestionReservationDisposition.InProgress,
        "completed" => CredentialIngestionReservationDisposition.Completed,
        _ => throw new InvalidOperationException("A persisted credential-ingestion disposition is invalid.")
    };

    private static string RequireDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A hexadecimal SHA-256 digest is required.", parameterName);
        }

        return value.ToLowerInvariant();
    }

    private static UnauthorizedAccessException InvalidProof() => new(InvalidProofMessage);

    private static ResourceConflictException ReservationLost() => new(
        "INGESTION_RESERVATION_LOST",
        "The credential-ingestion reservation is no longer current.");

    private static ResourceConflictException CompletionConflict() => new(
        "INGESTION_COMPLETION_CONFLICT",
        "The credential-ingestion grant already has a different completion.");
}
