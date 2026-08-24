using Npgsql;
using NpgsqlTypes;

namespace YO4X.Persistence.Postgres;

internal static class PostgresMigrationRunner
{
    private const long MigrationLockId = 9_079_040_001_000_001;

    private const string BootstrapSql = """
        create schema if not exists control;

        create table if not exists control.schema_migrations
        (
            migration_id text primary key,
            sha256 text not null check (sha256 ~ '^[0-9a-f]{64}$'),
            applied_at timestamptz not null default transaction_timestamp(),
            applied_by text not null default current_user
        );
        """;

    public static async Task MigrateAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var advisoryLock = new NpgsqlCommand(
            "select pg_advisory_xact_lock(@lock_id)",
            connection,
            transaction))
        {
            advisoryLock.Parameters.AddWithValue("lock_id", NpgsqlDbType.Bigint, MigrationLockId);
            await advisoryLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var bootstrap = new NpgsqlCommand(BootstrapSql, connection, transaction))
        {
            await bootstrap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PostgresEmbeddedMigration> migrations = PostgresMigrationManifest.Load();
        foreach (PostgresEmbeddedMigration migration in migrations)
        {
            string? appliedChecksum = await ReadAppliedChecksumAsync(
                connection,
                transaction,
                migration.Id,
                cancellationToken).ConfigureAwait(false);

            if (appliedChecksum is not null)
            {
                if (!string.Equals(appliedChecksum, migration.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Migration '{migration.Id}' was modified after it was applied.");
                }

                continue;
            }

            await using (var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction))
            {
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var record = new NpgsqlCommand(
                """
                insert into control.schema_migrations (migration_id, sha256)
                values (@migration_id, @sha256)
                """,
                connection,
                transaction);
            record.Parameters.AddWithValue("migration_id", NpgsqlDbType.Text, migration.Id);
            record.Parameters.AddWithValue("sha256", NpgsqlDbType.Text, migration.Sha256);
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await VerifyAppliedManifestAsync(
            connection,
            transaction,
            migrations,
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyAppliedManifestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PostgresEmbeddedMigration> migrations,
        CancellationToken cancellationToken)
    {
        string[] migrationIds = migrations.Select(migration => migration.Id).ToArray();
        string[] migrationSha256 = migrations.Select(migration => migration.Sha256).ToArray();
        await using var command = new NpgsqlCommand(
            """
            with expected(migration_id, sha256) as
            (
                select *
                from unnest(@migration_ids::text[], @migration_sha256::text[])
            )
            select not exists
            (
                (select migration_id, sha256 from control.schema_migrations
                 except
                 select migration_id, sha256 from expected)
                union all
                (select migration_id, sha256 from expected
                 except
                 select migration_id, sha256 from control.schema_migrations)
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(
            "migration_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            migrationIds);
        command.Parameters.AddWithValue(
            "migration_sha256",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            migrationSha256);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
        {
            throw new InvalidOperationException(
                "The applied PostgreSQL migration manifest does not exactly match the embedded manifest.");
        }
    }

    private static async Task<string?> ReadAppliedChecksumAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select sha256 from control.schema_migrations where migration_id = @migration_id",
            connection,
            transaction);
        command.Parameters.AddWithValue("migration_id", NpgsqlDbType.Text, migrationId);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

}
