using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// The immutable, ordered migration manifest embedded in the deployed
/// persistence assembly. Migration execution and every role-readiness probe
/// consume this one source of truth.
/// </summary>
internal static class PostgresMigrationManifest
{
    private static readonly Lazy<ReadOnlyCollection<PostgresEmbeddedMigration>> Migrations =
        new(LoadCore, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static IReadOnlyList<PostgresEmbeddedMigration> Load() => Migrations.Value;

    private static ReadOnlyCollection<PostgresEmbeddedMigration> LoadCore()
    {
        Assembly assembly = typeof(PostgresMigrationManifest).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.", StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("No embedded PostgreSQL migrations were found.");
        }

        var migrations = new List<PostgresEmbeddedMigration>(resourceNames.Length);
        foreach (string resourceName in resourceNames)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Migration resource '{resourceName}' cannot be read.");
            using var reader = new StreamReader(stream);
            string sql = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException(
                    $"Migration resource '{resourceName}' is empty.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sql);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string id = resourceName[(resourceName.IndexOf(
                ".Migrations.",
                StringComparison.Ordinal) + ".Migrations.".Length)..^4];
            migrations.Add(new PostgresEmbeddedMigration(id, sha256, sql));
        }

        if (migrations.Any(migration => string.IsNullOrWhiteSpace(migration.Id))
            || migrations.Select(migration => migration.Id)
                .Distinct(StringComparer.Ordinal).Count() != migrations.Count
            || !migrations.Select(migration => migration.Id)
                .SequenceEqual(
                    migrations.Select(migration => migration.Id)
                        .OrderBy(id => id, StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Embedded PostgreSQL migration identifiers must be non-empty, unique, and ordered.");
        }

        return migrations.AsReadOnly();
    }
}

internal sealed record PostgresEmbeddedMigration(
    string Id,
    string Sha256,
    string Sql);
