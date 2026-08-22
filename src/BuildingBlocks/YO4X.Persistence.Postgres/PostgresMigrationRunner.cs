using System.Reflection;
using System.Security.Cryptography;
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

        foreach (EmbeddedMigration migration in LoadMigrations())
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

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

    private static List<EmbeddedMigration> LoadMigrations()
    {
        Assembly assembly = typeof(PostgresMigrationRunner).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.", StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("No embedded PostgreSQL migrations were found.");
        }

        var migrations = new List<EmbeddedMigration>(resourceNames.Length);
        foreach (string resourceName in resourceNames)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Migration resource '{resourceName}' cannot be read.");
            using var reader = new StreamReader(stream);
            string sql = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException($"Migration resource '{resourceName}' is empty.");
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sql);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string id = resourceName[(resourceName.IndexOf(".Migrations.", StringComparison.Ordinal)
                + ".Migrations.".Length)..^4];
            migrations.Add(new EmbeddedMigration(id, sha256, sql));
        }

        if (migrations.Select(migration => migration.Id).Distinct(StringComparer.Ordinal).Count()
            != migrations.Count)
        {
            throw new InvalidOperationException("Embedded PostgreSQL migration identifiers must be unique.");
        }

        return migrations;
    }

    private sealed record EmbeddedMigration(string Id, string Sha256, string Sql);
}
