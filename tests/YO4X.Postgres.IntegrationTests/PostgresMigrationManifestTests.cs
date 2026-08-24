using Npgsql;
using YO4X.Persistence.Postgres;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class PostgresFoundationTests
{
    [PostgresFact]
    public async Task UnexpectedAppliedMigrationRollsBackEveryEmbeddedMigration()
    {
        _postgres.RequireAvailable();
        await using PostgresDatabase database =
            await _postgres.CreateUnmigratedDatabaseAsync();

        await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
        await using (var seed = new NpgsqlCommand(
            """
            create schema control;
            create table control.schema_migrations
            (
                migration_id text primary key,
                sha256 text not null check (sha256 ~ '^[0-9a-f]{64}$'),
                applied_at timestamptz not null default transaction_timestamp(),
                applied_by text not null default current_user
            );
            insert into control.schema_migrations (migration_id, sha256)
            values ('999_unexpected', repeat('f', 64));
            """,
            connection))
        {
            await seed.ExecuteNonQueryAsync();
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await database.MigrateAsync());
        Assert.Equal(
            "The applied PostgreSQL migration manifest does not exactly match the embedded manifest.",
            exception.Message);

        await using NpgsqlConnection verification = await database.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select
                array_agg(migration_id order by migration_id),
                to_regnamespace('identity') is null,
                to_regnamespace('operations') is null,
                to_regclass('control.tenant_context_capabilities') is null
            from control.schema_migrations
            """,
            verification);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(["999_unexpected"], reader.GetFieldValue<string[]>(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.False(await reader.ReadAsync());
    }
}
