using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// Issues transaction-bound tenant-context capabilities through the dedicated
/// PostgreSQL context-issuer identity. The issuer sees only the token
/// digest; the runtime transaction presents the raw token exactly once.
/// </summary>
public sealed class PostgresTenantContextCapabilityProvider :
    ITenantContextCapabilityProvider,
    IAsyncDisposable
{
    public const string RequiredDatabaseRole = "yo4x_context_issuer";

    internal const string CredentialRuntimeRole = "yo4x_credential_runtime";

    private const int IssuerCommandTimeoutSeconds = 5;
    private const string IssueCapabilitySql = """
        select control.issue_tenant_context_capability(
            @capability_sha256,
            @database_name,
            @runtime_role,
            @backend_pid,
            @transaction_id,
            @tenant_id,
            @actor_id,
            @correlation_id,
            @session_id)
        """;

    private const string IssueCredentialRuntimeCapabilitySql = """
        select control.issue_credential_runtime_tenant_context_capability(
            @capability_sha256,
            @database_name,
            @backend_pid,
            @transaction_id,
            @tenant_id,
            @actor_id,
            @correlation_id,
            @session_id)
        """;

    private readonly NpgsqlDataSource _issuerDataSource;

    public PostgresDatabaseEndpoint Endpoint { get; }

    public PostgresTenantContextCapabilityProvider(
        string issuerConnectionString,
        bool requireTls = true)
    {
        if (!TryNormalizeIssuerConnectionString(
                issuerConnectionString,
                requireTls,
                out string normalizedConnectionString))
        {
            throw new ArgumentException(
                "The tenant-context issuer connection must use the dedicated role, safe session options, and the required TLS mode.",
                nameof(issuerConnectionString));
        }

        var normalized = new NpgsqlConnectionStringBuilder(normalizedConnectionString);
        Endpoint = PostgresDatabaseEndpoint.From(normalized);
        var builder = new NpgsqlDataSourceBuilder(normalized.ConnectionString);
        _issuerDataSource = builder.Build();
    }

    public static bool TryNormalizeIssuerConnectionString(
        string? value,
        bool requireTls,
        out string connectionString)
    {
        connectionString = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var options = new NpgsqlConnectionStringBuilder(value);
            PostgresConnectionSafety.ValidateNoCallerControlledSessionState(options, nameof(value));
            if (string.IsNullOrWhiteSpace(options.Host)
                || string.IsNullOrWhiteSpace(options.Database)
                || !string.Equals(options.Username, RequiredDatabaseRole, StringComparison.Ordinal)
                || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                    options,
                    allowInsecureLoopbackForDevelopment: !requireTls)
                || options.PersistSecurityInfo)
            {
                return false;
            }

            options.Timeout = Math.Clamp(options.Timeout, 1, IssuerCommandTimeoutSeconds);
            options.Enlist = false;
            options.PersistSecurityInfo = false;
            connectionString = options.ConnectionString;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(IssuerCommandTimeoutSeconds));
        try
        {
            await using NpgsqlConnection connection = await _issuerDataSource
                .OpenConnectionAsync(timeout.Token)
                .ConfigureAwait(false);
            await using (var assertion = new NpgsqlCommand(
                "select control.assert_safe_runtime_role()",
                connection)
            {
                CommandTimeout = IssuerCommandTimeoutSeconds
            })
            {
                await assertion.ExecuteNonQueryAsync(timeout.Token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(connection.Host)
                || string.IsNullOrWhiteSpace(connection.Database)
                || !string.Equals(
                    connection.Database,
                    Endpoint.Database,
                    StringComparison.Ordinal)
                || new PostgresDatabaseEndpoint(
                    connection.Host,
                    connection.Port,
                    connection.Database) != Endpoint
                || !await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                        connection,
                        transaction: null,
                        Yo4xPostgresRoleContracts.ContextIssuer,
                        timeout.Token)
                    .ConfigureAwait(false))
            {
                return false;
            }

            await using var identity = new NpgsqlCommand(
                "select current_user = @expected_role and current_database() = @expected_database",
                connection)
            {
                CommandTimeout = IssuerCommandTimeoutSeconds
            };
            identity.Parameters.AddWithValue(
                "expected_role",
                NpgsqlDbType.Text,
                RequiredDatabaseRole);
            identity.Parameters.AddWithValue(
                "expected_database",
                NpgsqlDbType.Text,
                Endpoint.Database);
            return await identity.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false) is true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async ValueTask<TenantContextCapability> AcquireAsync(
        TenantExecutionContext context,
        TenantContextTransactionBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(binding);

        byte[] material = RandomNumberGenerator.GetBytes(TenantContextCapability.SizeInBytes);
        byte[] digest = SHA256.HashData(material);
        bool materialTransferred = false;
        try
        {
            await using NpgsqlConnection connection = await _issuerDataSource
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            bool credentialRuntime = string.Equals(
                binding.RuntimeRole,
                CredentialRuntimeRole,
                StringComparison.Ordinal);
            await using var command = new NpgsqlCommand(
                credentialRuntime
                    ? IssueCredentialRuntimeCapabilitySql
                    : IssueCapabilitySql,
                connection)
            {
                CommandTimeout = IssuerCommandTimeoutSeconds
            };
            command.Parameters.AddWithValue(
                "capability_sha256",
                NpgsqlDbType.Bytea,
                digest);
            command.Parameters.AddWithValue(
                "database_name",
                NpgsqlDbType.Text,
                binding.DatabaseName);
            if (!credentialRuntime)
            {
                command.Parameters.AddWithValue(
                    "runtime_role",
                    NpgsqlDbType.Text,
                    binding.RuntimeRole);
            }
            command.Parameters.AddWithValue(
                "backend_pid",
                NpgsqlDbType.Integer,
                binding.BackendProcessId);
            command.Parameters.AddWithValue(
                "transaction_id",
                NpgsqlDbType.Text,
                binding.CanonicalTransactionId);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, context.TenantId);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, context.ActorId);
            command.Parameters.AddWithValue(
                "correlation_id",
                NpgsqlDbType.Uuid,
                context.CorrelationId);
            command.Parameters.AddWithValue(
                "session_id",
                NpgsqlDbType.Uuid,
                context.SessionId is null ? DBNull.Value : context.SessionId.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            TenantContextCapability capability = TenantContextCapability.TakeOwnership(material);
            materialTransferred = true;
            return capability;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
            if (!materialTransferred)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
    }

    public async ValueTask DisposeAsync() =>
        await _issuerDataSource.DisposeAsync().ConfigureAwait(false);
}

internal static class PostgresConnectionSafety
{
    public static void ValidateNoCallerControlledSessionState(
        NpgsqlConnectionStringBuilder options,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(options))
        {
            throw new ArgumentException(
                "PostgreSQL security-boundary connections cannot expose diagnostics or retain caller-controlled session state.",
                parameterName);
        }
    }
}
