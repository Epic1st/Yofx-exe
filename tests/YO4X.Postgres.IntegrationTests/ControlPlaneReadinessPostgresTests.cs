using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Postgres;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class ControlPlaneReadinessPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task ReadinessFailsClosedForOldSchemaOrOldColumnGrants()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();

        // The catalog semantic fingerprint attests every ACL entry, so the
        // fixture's broad emergency-role grants must be removed before a
        // production readiness probe runs against this database.
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        // Recorded-migration checksums are pinned by the shared manifest
        // delegation inside the role-capability fingerprint; the readiness SQL
        // deliberately carries no manifest literals. Tampering with the
        // recorded foundation digest must therefore fail closed through that
        // delegated check.
        string recordedFoundationSha256;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var readRecordedChecksum = new NpgsqlCommand(
                "select sha256 from control.schema_migrations where migration_id = '001_foundation'",
                administrator);
            recordedFoundationSha256 =
                (string)(await readRecordedChecksum.ExecuteScalarAsync())!;

            await using var tamperRecordedChecksum = new NpgsqlCommand(
                """
                update control.schema_migrations set sha256 = repeat('0', 64)
                where migration_id = '001_foundation'
                """,
                administrator);
            await tamperRecordedChecksum.ExecuteNonQueryAsync();
        }

        Assert.False(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            new FixedClock(DateTimeOffset.UtcNow),
            CancellationToken.None));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var restoreRecordedChecksum = new NpgsqlCommand(
                """
                update control.schema_migrations set sha256 = @sha256
                where migration_id = '001_foundation'
                """,
                administrator);
            restoreRecordedChecksum.Parameters.AddWithValue(
                "sha256",
                recordedFoundationSha256);
            Assert.Equal(1, await restoreRecordedChecksum.ExecuteNonQueryAsync());
        }

        Assert.True(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            new FixedClock(DateTimeOffset.UtcNow),
            CancellationToken.None));

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "revoke select on identity.user_identities from yo4x_control_api");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "revoke execute on function control.acquire_u0_authority_lock() from yo4x_control_api");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "revoke insert on messaging.outbox_messages from yo4x_control_api");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "revoke insert (proof_key_id) on control.credential_ingestion_grants from yo4x_control_api");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "alter table control.strategy_import_jobs rename column proof_key_id to proof_key_id_unavailable");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "revoke update (retired_at) on control.idempotency_records from yo4x_control_api");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await administrator.BeginTransactionAsync())
        {
            await ExecuteAsync(
                administrator,
                transaction,
                "alter table control.idempotency_records rename column retired_at to retired_at_unavailable");
            await ExecuteAsync(administrator, transaction, "set local role yo4x_control_api");
            Assert.False(await ReadReadinessAsync(administrator, transaction));
            await transaction.RollbackAsync();
        }

        Assert.True(await ReadReadinessAsync(database.ControlApi));
    }

    [PostgresFact]
    public async Task ControlReadinessFailsClosedOutsideTheProofKeyClockSkewBound()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        DateTimeOffset databaseNow;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand(
            "select statement_timestamp()",
            administrator))
        {
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            databaseNow = reader.GetFieldValue<DateTimeOffset>(0);
            Assert.False(await reader.ReadAsync());
        }

        Assert.True(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            new FixedClock(databaseNow),
            CancellationToken.None));

        TimeSpan rejectedOffset =
            ControlPlanePostgresOptions.ProofKeyMaximumDatabaseClockSkew
            + TimeSpan.FromMinutes(1);
        Assert.False(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            new FixedClock(databaseNow.Add(rejectedOffset)),
            CancellationToken.None));
        Assert.False(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            new FixedClock(databaseNow.Subtract(rejectedOffset)),
            CancellationToken.None));
    }

    private static async Task<bool> ReadReadinessAsync(
        YO4X.Persistence.Postgres.PostgresDatabase database)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        return await ReadReadinessAsync(connection, transaction: null);
    }

    private static async Task<bool> ReadReadinessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            ControlPlaneReadinessProbe.ControlDatabaseReadinessSql,
            connection,
            transaction);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
