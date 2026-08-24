using Npgsql;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal static class AdminPostgresDatabaseIdentity
{
    internal const string RequiredRole = "yo4x_admin_bff";

    internal static bool TryReadRuntimeConnectionString(
        string? value,
        out string connectionString)
    {
        connectionString = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.Host)
                || string.IsNullOrWhiteSpace(builder.Database)
                || !string.Equals(builder.Username, RequiredRole, StringComparison.Ordinal)
                || !PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
                || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                    builder,
                    allowInsecureLoopbackForDevelopment: false))
            {
                return false;
            }

            connectionString = builder.ConnectionString;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
