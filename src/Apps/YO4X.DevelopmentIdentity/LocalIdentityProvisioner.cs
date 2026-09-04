using Npgsql;
using NpgsqlTypes;

namespace YO4X.DevelopmentIdentity;

public sealed class LocalIdentityPostgresOptions
{
    private LocalIdentityPostgresOptions(string connectionString) =>
        ConnectionString = connectionString;

    public string ConnectionString { get; }

    public static bool TryCreate(string? value, out LocalIdentityPostgresOptions? options)
    {
        options = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (!string.Equals(builder.Username, "yo4x_local_identity", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(builder.Password)
                || string.IsNullOrWhiteSpace(builder.Database)
                || !IsLoopbackHost(builder.Host)
                || builder.Multiplexing
                || builder.IncludeErrorDetail
                || builder.LogParameters
                || builder.NoResetOnClose)
            {
                return false;
            }

            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            builder.ApplicationName = "YO4X.DevelopmentIdentity";
            builder.MaxPoolSize = Math.Min(builder.MaxPoolSize, 4);
            builder.MinPoolSize = 0;
            builder.Timeout = Math.Min(builder.Timeout, 5);
            builder.CommandTimeout = Math.Min(builder.CommandTimeout, 5);
            options = new LocalIdentityPostgresOptions(builder.ConnectionString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
}

public sealed class LocalIdentityProvisioner(
    LocalIdentityPostgresOptions options,
    TimeProvider timeProvider)
{
    public async Task ProvisionAsync(
        DevelopmentUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Id == Guid.Empty
            || user.TenantId != LocalIdentityContract.TenantId
            || user.SessionId == Guid.Empty
            || !user.EmailConfirmed
            || string.IsNullOrWhiteSpace(user.NormalizedEmail)
            || user.NormalizedEmail.Length is < 3 or > 320)
        {
            throw new InvalidOperationException("The local identity cannot be provisioned.");
        }

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "select identity.provision_local_development_identity(@tenant_id, @user_id, @session_id, @email, @expires_at)",
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, user.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, user.Id);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, user.SessionId);
        command.Parameters.AddWithValue("email", NpgsqlDbType.Text, user.NormalizedEmail);
        command.Parameters.AddWithValue(
            "expires_at",
            NpgsqlDbType.TimestampTz,
            timeProvider.GetUtcNow().AddHours(8));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
