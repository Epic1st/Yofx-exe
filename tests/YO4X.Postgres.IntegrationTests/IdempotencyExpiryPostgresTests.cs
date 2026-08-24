using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Postgres;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class IdempotencyExpiryPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task ExpiredKeyReacquisitionIsAppendOnlyExclusiveAndDatabaseTimeBound()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        TenantExecutionContext context = NewContext();
        await SeedTenantAsync(database, context);

        const string operation = "test.idempotency.expiry";
        string idempotencyKey = new('1', 32);
        string expiredRequestSha256 = new('a', 64);
        string replacementRequestSha256 = new('b', 64);
        Guid expiredId = Guid.CreateVersion7();
        await SeedExpiredRecordAsync(
            database,
            context,
            expiredId,
            operation,
            idempotencyKey,
            expiredRequestSha256);

        int arrivals = 0;
        var acquisitionGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SynchronizeAcquisitionAsync()
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                acquisitionGate.TrySetResult(true);
            }

            await acquisitionGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Task<IdempotencyLease> first = AcquireAsync(
            database.ControlApi,
            context,
            operation,
            idempotencyKey,
            replacementRequestSha256,
            SynchronizeAcquisitionAsync);
        Task<IdempotencyLease> second = AcquireAsync(
            database.ControlApi,
            context,
            operation,
            idempotencyKey,
            replacementRequestSha256,
            SynchronizeAcquisitionAsync);
        IdempotencyLease[] leases = await Task.WhenAll(first, second);

        Assert.Single(leases, static lease => lease.Acquired);
        Assert.Single(leases, static lease => !lease.Acquired);
        Assert.Equal(leases[0].Id, leases[1].Id);
        Assert.NotEqual(expiredId, leases[0].Id);
        Assert.All(
            leases,
            lease => Assert.Equal(replacementRequestSha256, lease.RequestSha256));

        IdempotencyLease replay = await AcquireAsync(
            database.ControlApi,
            context,
            operation,
            idempotencyKey,
            new string('c', 64));
        Assert.False(replay.Acquired);
        Assert.Equal(leases[0].Id, replay.Id);
        Assert.Equal(replacementRequestSha256, replay.RequestSha256);

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var history = new NpgsqlCommand(
                """
                select id, request_sha256, retired_at is not null
                from control.idempotency_records
                where tenant_id = @tenant_id
                  and actor_id = @actor_id
                  and operation = @operation
                  and idempotency_key = @idempotency_key
                order by created_at, id
                """,
                administrator);
            history.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
            history.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, context.ActorId);
            history.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
            history.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using NpgsqlDataReader reader = await history.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(expiredId, reader.GetGuid(0));
            Assert.Equal(expiredRequestSha256, reader.GetString(1));
            Assert.True(reader.GetBoolean(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(leases[0].Id, reader.GetGuid(0));
            Assert.Equal(replacementRequestSha256, reader.GetString(1));
            Assert.False(reader.GetBoolean(2));
            Assert.False(await reader.ReadAsync());
        }

        await using (TenantPostgresTransaction transaction =
            await database.ControlApi.BeginTenantTransactionAsync(context))
        {
            await using NpgsqlCommand retireEarly = transaction.CreateCommand(
                """
                update control.idempotency_records
                set retired_at = statement_timestamp()
                where id = @id and tenant_id = @tenant_id
                """);
            retireEarly.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, leases[0].Id);
            retireEarly.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () => await retireEarly.ExecuteNonQueryAsync());
            Assert.Equal("55000", exception.SqlState);
        }

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var delete = new NpgsqlCommand(
            "delete from control.idempotency_records where id = @id",
            administrator))
        {
            delete.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expiredId);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () => await delete.ExecuteNonQueryAsync());
            Assert.Equal("55000", exception.SqlState);
        }
    }

    private static async Task<IdempotencyLease> AcquireAsync(
        PostgresDatabase database,
        TenantExecutionContext context,
        string operation,
        string idempotencyKey,
        string requestSha256,
        Func<Task>? synchronize = null)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(context);
        DateTimeOffset now;
        await using (NpgsqlCommand clock = transaction.CreateCommand("select statement_timestamp()"))
        {
            object value = await clock.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("PostgreSQL did not return its clock value.");
            now = value switch
            {
                DateTimeOffset timestamp => timestamp.ToUniversalTime(),
                DateTime timestamp when timestamp.Kind == DateTimeKind.Utc =>
                    new DateTimeOffset(timestamp),
                _ => throw new InvalidOperationException("PostgreSQL returned an invalid clock value.")
            };
        }

        if (synchronize is not null)
        {
            await synchronize();
        }

        IdempotencyLease lease = await PostgresIdempotencyRepository.TryAcquireAsync(
            transaction,
            operation,
            idempotencyKey,
            requestSha256,
            now,
            now.Add(ControlPlanePostgresOptions.IdempotencyReplayLifetime));
        await transaction.CommitAsync();
        return lease;
    }

    private static async Task SeedExpiredRecordAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        Guid id,
        string operation,
        string idempotencyKey,
        string requestSha256)
    {
        await using TenantPostgresTransaction transaction =
            await database.ControlApi.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.idempotency_records
                (id, tenant_id, actor_id, operation, idempotency_key,
                 request_sha256, created_at, expires_at)
            values
                (@id, @tenant_id, @actor_id, @operation, @idempotency_key,
                 @request_sha256,
                 statement_timestamp() - interval '2 hours',
                 statement_timestamp() - interval '1 hour')
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, context.ActorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        command.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, requestSha256);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task SeedTenantAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context)
    {
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@id, @slug, 'Idempotency expiry tenant')
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, context.TenantId);
        command.Parameters.AddWithValue(
            "slug",
            NpgsqlDbType.Text,
            $"idempotency-{context.TenantId:N}");
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static TenantExecutionContext NewContext() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7());
}
