using Npgsql;
using NpgsqlTypes;
using YO4X.Outbox;

namespace YO4X.Persistence.Postgres;

public static class PostgresOutboxRepository
{
    private const string ClaimSql = """
        with claimable as
        (
            select message.id
            from messaging.outbox_messages as message
            where message.tenant_id = @tenant_id
              and
              (
                  (message.state = 'pending' and message.available_at <= @claimed_at)
                  or
                  (message.state = 'processing' and message.locked_until <= @claimed_at)
              )
            order by message.available_at, message.occurred_at, message.id
            for update skip locked
            limit @batch_size
        )
        update messaging.outbox_messages as message
        set state = 'processing',
            attempts = message.attempts + 1,
            locked_by = @worker_id,
            locked_until = @locked_until,
            last_error = null
        from claimable
        where message.id = claimable.id
        returning
            message.id,
            message.tenant_id,
            message.message_type,
            message.schema_version,
            message.aggregate_type,
            message.aggregate_id,
            message.payload::text,
            message.payload_sha256,
            message.correlation_id,
            message.causation_id,
            message.occurred_at,
            message.available_at,
            message.attempts,
            message.locked_by,
            message.locked_until
        """;

    public static async Task EnqueueAsync(
        TenantPostgresTransaction transaction,
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(message);
        if (message.TenantId != transaction.Context.TenantId
            || message.CorrelationId != transaction.Context.CorrelationId)
        {
            throw new InvalidOperationException("The outbox message must match the transaction context.");
        }

        await PostgresAuditOutboxWriter.InsertOutboxAsync(transaction, message, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        TenantPostgresTransaction transaction,
        string workerId,
        int batchSize,
        DateTimeOffset claimedAt,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (batchSize is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 1000.");
        }

        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration), "Lock duration must be positive.");
        }

        DateTimeOffset normalizedClaimedAt = claimedAt.ToUniversalTime();
        await using NpgsqlCommand command = transaction.CreateCommand(ClaimSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("claimed_at", NpgsqlDbType.TimestampTz, normalizedClaimedAt);
        command.Parameters.AddWithValue("locked_until", NpgsqlDbType.TimestampTz, normalizedClaimedAt.Add(lockDuration));
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId.Trim());
        command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, batchSize);

        var messages = new List<ClaimedOutboxMessage>(batchSize);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(new ClaimedOutboxMessage(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt16(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetInt32(12),
                reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14)));
        }

        return messages;
    }

    public static async Task<bool> MarkPublishedAsync(
        TenantPostgresTransaction transaction,
        Guid messageId,
        string workerId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default)
    {
        RequireIdentifier(messageId, nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update messaging.outbox_messages
            set state = 'published',
                published_at = @published_at,
                locked_by = null,
                locked_until = null,
                last_error = null
            where id = @message_id
              and tenant_id = @tenant_id
              and state = 'processing'
              and locked_by = @worker_id
            """);
        command.Parameters.AddWithValue("published_at", NpgsqlDbType.TimestampTz, publishedAt.ToUniversalTime());
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public static async Task<bool> ReleaseAfterFailureAsync(
        TenantPostgresTransaction transaction,
        Guid messageId,
        string workerId,
        string error,
        DateTimeOffset retryAt,
        int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        RequireIdentifier(messageId, nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts), "Maximum attempts must be positive.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update messaging.outbox_messages
            set state = case when attempts >= @maximum_attempts then 'dead_letter' else 'pending' end,
                available_at = case when attempts >= @maximum_attempts then available_at else @retry_at end,
                locked_by = null,
                locked_until = null,
                last_error = left(@error, 4000)
            where id = @message_id
              and tenant_id = @tenant_id
              and state = 'processing'
              and locked_by = @worker_id
            """);
        command.Parameters.AddWithValue("maximum_attempts", NpgsqlDbType.Integer, maximumAttempts);
        command.Parameters.AddWithValue("retry_at", NpgsqlDbType.TimestampTz, retryAt.ToUniversalTime());
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, error.Trim());
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}
