using System.Security.Cryptography;
using Npgsql;
using Testcontainers.PostgreSql;
using YO4X.Persistence.Postgres;

namespace YO4X.Postgres.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "postgres-integration";
}

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public const string ExternalAdministratorConnectionStringEnvironmentVariable =
        "YO4X_POSTGRES_INTEGRATION_ADMIN";
    internal const string PostgreSqlContainerImage =
        "postgres:18.6-alpine3.23@sha256:697c180dbf244d3ce4a8f4cbc0156cde840af055c1bf8b76aebe422a4822086f";

    private PostgreSqlContainer? _container;
    private string? _administratorConnectionString;

    public string? UnavailableDiagnostic { get; private set; }

    public async ValueTask InitializeAsync()
    {
        string? externalConnectionString = Environment.GetEnvironmentVariable(
            ExternalAdministratorConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            _administratorConnectionString = await ValidateAdministratorAsync(
                    externalConnectionString,
                    ExternalAdministratorConnectionStringEnvironmentVariable)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            string administratorPassword = CreateEphemeralPassword();
            _container = new PostgreSqlBuilder(PostgreSqlContainerImage)
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword(administratorPassword)
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            _administratorConnectionString = await ValidateAdministratorAsync(
                    _container.GetConnectionString(),
                    "The PostgreSQL Testcontainer connection string")
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            UnavailableDiagnostic =
                $"Docker is unavailable; real PostgreSQL integration tests were skipped. "
                + $"Diagnostic: {exception.GetBaseException().Message}";
            _container = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void RequireAvailable()
    {
        if (string.IsNullOrWhiteSpace(_administratorConnectionString))
        {
            throw new InvalidOperationException(
                UnavailableDiagnostic
                ?? "No external PostgreSQL server or Docker Testcontainer was started.");
        }
    }

    public async Task<PostgresTestDatabase> CreateDatabaseAsync()
    {
        RequireAvailable();
        string suffix = Guid.CreateVersion7().ToString("N");
        string databaseName = $"yo4x_{suffix}";
        string contextIssuerPassword = CreateEphemeralPassword();
        string localIdentityPassword = CreateEphemeralPassword();
        string controlApiPassword = CreateEphemeralPassword();
        string adminBffPassword = CreateEphemeralPassword();
        string emergencyPassword = CreateEphemeralPassword();
        string secretIngestionPassword = CreateEphemeralPassword();
        string conversionWorkerPassword = CreateEphemeralPassword();
        string strategyVerifierPassword = CreateEphemeralPassword();
        string runtimeEvidencePassword = CreateEphemeralPassword();
        string workerPassword = CreateEphemeralPassword();
        string supervisorRuntimePassword = CreateEphemeralPassword();
        string tradeAuthorizerPassword = CreateEphemeralPassword();
        string gatewayRuntimePassword = CreateEphemeralPassword();
        string credentialRuntimePassword = CreateEphemeralPassword();

        var serverBuilder = new NpgsqlConnectionStringBuilder(_administratorConnectionString)
        {
            Database = "postgres",
            IncludeErrorDetail = true,
            Pooling = false
        };

        await using (var serverConnection = new NpgsqlConnection(serverBuilder.ConnectionString))
        {
            await serverConnection.OpenAsync().ConfigureAwait(false);
            await ProvisionCapabilityRolesAsync(serverConnection).ConfigureAwait(false);
            string quotedDatabase = QuoteIdentifier(databaseName);

            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_context_issuer",
                contextIssuerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_local_identity",
                localIdentityPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_control_api",
                controlApiPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_admin_bff",
                adminBffPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_emergency",
                emergencyPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_secret_ingestion",
                secretIngestionPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_conversion_worker",
                conversionWorkerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_strategy_verifier",
                strategyVerifierPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_runtime_evidence",
                runtimeEvidencePassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_worker",
                workerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_supervisor_runtime",
                supervisorRuntimePassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_trade_authorizer",
                tradeAuthorizerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_gateway_runtime",
                gatewayRuntimePassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_credential_runtime",
                credentialRuntimePassword).ConfigureAwait(false);

            await using var createDatabase = new NpgsqlCommand(
                $"""
                create database {quotedDatabase}
                    owner postgres
                    template template0
                    encoding 'UTF8'
                    locale_provider libc
                    lc_collate 'C'
                    lc_ctype 'C'
                """,
                serverConnection);
            await createDatabase.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var administratorBuilder = new NpgsqlConnectionStringBuilder(_administratorConnectionString)
        {
            Database = databaseName,
            Pooling = true
        };
        var administratorDatabase = new PostgresDatabase(
            administratorBuilder.ConnectionString,
            PostgresDatabaseUsage.Migrator,
            allowInsecureLoopbackForDevelopment: true);
        await administratorDatabase.MigrateAsync().ConfigureAwait(false);

        await using (NpgsqlConnection administratorConnection =
            await administratorDatabase.OpenConnectionAsync().ConfigureAwait(false))
        {
            await ApplyLeastPrivilegeRoleScriptAsync(administratorConnection).ConfigureAwait(false);
            await ApplyBroadActorGrantsAsync(administratorConnection).ConfigureAwait(false);
        }

        var applicationBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_emergency",
            emergencyPassword);
        applicationBuilder.MaxPoolSize = 10;
        var conversionWorkerBuilder = new NpgsqlConnectionStringBuilder(administratorBuilder.ConnectionString)
        {
            Username = "yo4x_conversion_worker",
            Password = conversionWorkerPassword,
            IncludeErrorDetail = false,
            LogParameters = false,
            MaxPoolSize = 4,
            MinPoolSize = 0,
            NoResetOnClose = false
        };
        var controlApiBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_control_api",
            controlApiPassword);
        var adminBffBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_admin_bff",
            adminBffPassword);
        var secretIngestionBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_secret_ingestion",
            secretIngestionPassword);
        var workerBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_worker",
            workerPassword);
        var runtimeEvidenceBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_runtime_evidence",
            runtimeEvidencePassword);
        var strategyVerifierBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_strategy_verifier",
            strategyVerifierPassword);
        var supervisorRuntimeBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_supervisor_runtime",
            supervisorRuntimePassword);
        var tradeAuthorizerBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_trade_authorizer",
            tradeAuthorizerPassword);
        var gatewayRuntimeBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_gateway_runtime",
            gatewayRuntimePassword);
        var credentialRuntimeBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_credential_runtime",
            credentialRuntimePassword);
        var contextIssuerBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            PostgresTenantContextCapabilityProvider.RequiredDatabaseRole,
            contextIssuerPassword);
        var localIdentityBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_local_identity",
            localIdentityPassword);
        var contextCapabilityProvider = new PostgresTenantContextCapabilityProvider(
            contextIssuerBuilder.ConnectionString,
            requireTls: false);

        return new PostgresTestDatabase(
            administratorDatabase,
            contextCapabilityProvider,
            contextIssuerBuilder.ConnectionString,
            new PostgresDatabase(
                applicationBuilder.ConnectionString,
                PostgresDatabaseUsage.Runtime,
                contextCapabilityProvider,
                allowInsecureLoopbackForDevelopment: true),
            applicationBuilder.ConnectionString,
            new PostgresDatabase(controlApiBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            new PostgresDatabase(adminBffBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            new PostgresDatabase(secretIngestionBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            new PostgresDatabase(conversionWorkerBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            conversionWorkerBuilder.ConnectionString,
            new PostgresDatabase(strategyVerifierBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            runtimeEvidenceBuilder.ConnectionString,
            new PostgresDatabase(workerBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            workerBuilder.ConnectionString,
            new PostgresDatabase(supervisorRuntimeBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            supervisorRuntimeBuilder.ConnectionString,
            new PostgresDatabase(tradeAuthorizerBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            new PostgresDatabase(gatewayRuntimeBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            gatewayRuntimeBuilder.ConnectionString,
            new PostgresDatabase(credentialRuntimeBuilder.ConnectionString, PostgresDatabaseUsage.Runtime, contextCapabilityProvider, allowInsecureLoopbackForDevelopment: true),
            credentialRuntimeBuilder.ConnectionString,
            localIdentityBuilder.ConnectionString);
    }

    internal async Task<PostgresDatabase> CreateUnmigratedDatabaseAsync()
    {
        RequireAvailable();
        string databaseName = $"yo4x_unmigrated_{Guid.CreateVersion7():N}";
        var serverBuilder = new NpgsqlConnectionStringBuilder(_administratorConnectionString)
        {
            Database = "postgres",
            IncludeErrorDetail = true,
            Pooling = false
        };

        await using (var serverConnection = new NpgsqlConnection(serverBuilder.ConnectionString))
        {
            await serverConnection.OpenAsync().ConfigureAwait(false);
            string quotedDatabase = QuoteIdentifier(databaseName);
            await using var createDatabase = new NpgsqlCommand(
                $"""
                create database {quotedDatabase}
                    owner postgres
                    template template0
                    encoding 'UTF8'
                    locale_provider libc
                    lc_collate 'C'
                    lc_ctype 'C'
                """,
                serverConnection);
            await createDatabase.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var databaseBuilder = new NpgsqlConnectionStringBuilder(_administratorConnectionString)
        {
            Database = databaseName,
            Pooling = true
        };
        return new PostgresDatabase(
            databaseBuilder.ConnectionString,
            PostgresDatabaseUsage.Migrator,
            allowInsecureLoopbackForDevelopment: true);
    }

    private static NpgsqlConnectionStringBuilder CreateRuntimeConnectionBuilder(
        NpgsqlConnectionStringBuilder administrator,
        string username,
        string password) =>
        new(administrator.ConnectionString)
        {
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            LogParameters = false,
            MaxPoolSize = 4,
            MinPoolSize = 0,
            NoResetOnClose = false
        };

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static string CreateEphemeralPassword()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(randomBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    private static async Task ProvisionCapabilityRolesAsync(NpgsqlConnection connection)
    {
        const string sql = """
            do $$
            declare
                required_role text;
            begin
                foreach required_role in array array[
                    'yo4x_migrator',
                    'yo4x_control_api',
                    'yo4x_context_authority',
                    'yo4x_context_issuer',
                    'yo4x_local_identity',
                    'yo4x_admin_bff',
                    'yo4x_emergency',
                    'yo4x_secret_ingestion',
                    'yo4x_conversion_worker',
                    'yo4x_strategy_verifier',
                    'yo4x_runtime_evidence',
                    'yo4x_worker',
                    'yo4x_supervisor_runtime',
                    'yo4x_trade_authorizer',
                    'yo4x_gateway_runtime',
                    'yo4x_credential_runtime'
                ]
                loop
                    if not exists
                    (
                        select 1
                        from pg_catalog.pg_roles
                        where rolname = required_role
                    ) then
                        execute format(
                            'create role %I nologin nosuperuser nocreatedb nocreaterole '
                            || 'noinherit nobypassrls noreplication',
                            required_role);
                    end if;
                end loop;
            end
            $$;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task EnableRoleLoginAsync(
        NpgsqlConnection connection,
        string roleName,
        string password)
    {
        string escapedPassword = password.Replace("'", "''", StringComparison.Ordinal);
        await using var command = new NpgsqlCommand(
            $"alter role {QuoteIdentifier(roleName)} login password '{escapedPassword}'",
            connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static async Task ApplyLeastPrivilegeRoleScriptAsync(NpgsqlConnection connection)
    {
        string scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Security",
            "least_privilege_roles.sql");
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException(
                "The least-privilege PostgreSQL role script was not copied to the test output.");
        }

        string sql = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException(
                "The least-privilege PostgreSQL role script is empty.");
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static async Task ApplyBroadActorGrantsAsync(NpgsqlConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        const string sql = """
            grant usage on schema identity, "authorization", control, operations,
                governance, audit, messaging, readmodel to yo4x_emergency;
            grant select, insert, update on all tables in schema identity,
                "authorization", control, operations, governance, audit, messaging,
                readmodel to yo4x_emergency;
            grant execute on all functions in schema identity, "authorization", control,
                operations, governance, audit, messaging, readmodel to yo4x_emergency;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<string> ValidateAdministratorAsync(
        string value,
        string sourceDescription)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{sourceDescription} is malformed.",
                exception);
        }

        if (!IsLoopbackHost(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Username)
            || string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException(
                $"{sourceDescription} must use a "
                + "password-authenticated loopback PostgreSQL administrator.");
        }

        if (!PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder))
        {
            throw new InvalidOperationException(
                $"{sourceDescription} contains PostgreSQL session configuration "
                + "rejected by PostgresRuntimeConnectionPolicy.");
        }

        builder.Database = "postgres";
        builder.SslMode = SslMode.Disable;
        builder.Pooling = false;
        builder.Timeout = Math.Clamp(builder.Timeout, 1, 5);
        builder.CommandTimeout = Math.Clamp(builder.CommandTimeout, 1, 30);

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "select current_setting('server_version_num')::integer",
            connection);
        int serverVersion = Convert.ToInt32(
            await command.ExecuteScalarAsync().ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (serverVersion < 180_000 || serverVersion >= 190_000)
        {
            throw new InvalidOperationException(
                "The PostgreSQL integration server must be major version 18.");
        }

        return builder.ConnectionString;
    }

    private static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string typeName = current.GetType().Name;
            string message = current.Message;
            if (typeName.Contains("DockerUnavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Docker is not running", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Docker endpoint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("docker_engine", StringComparison.OrdinalIgnoreCase)
                || message.Contains("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("docker", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class PostgresTestDatabase(
    PostgresDatabase administrator,
    PostgresTenantContextCapabilityProvider tenantContextCapabilityProvider,
    string contextIssuerConnectionString,
    PostgresDatabase application,
    string applicationConnectionString,
    PostgresDatabase controlApi,
    PostgresDatabase adminBff,
    PostgresDatabase secretIngestion,
    PostgresDatabase conversionWorker,
    string conversionWorkerConnectionString,
    PostgresDatabase strategyVerifier,
    string runtimeEvidenceConnectionString,
    PostgresDatabase worker,
    string workerConnectionString,
    PostgresDatabase supervisorRuntime,
    string supervisorRuntimeConnectionString,
    PostgresDatabase tradeAuthorizer,
    PostgresDatabase gatewayRuntime,
    string gatewayRuntimeConnectionString,
    PostgresDatabase credentialRuntime,
    string credentialRuntimeConnectionString,
    string localIdentityConnectionString) : IAsyncDisposable
{
    public PostgresDatabase Administrator { get; } = administrator;

    public PostgresTenantContextCapabilityProvider TenantContextCapabilityProvider { get; } =
        tenantContextCapabilityProvider;

    public string ContextIssuerConnectionString { get; } = contextIssuerConnectionString;

    public PostgresDatabase Application { get; } = application;

    public string ApplicationConnectionString { get; } = applicationConnectionString;

    public PostgresDatabase ControlApi { get; } = controlApi;

    public PostgresDatabase AdminBff { get; } = adminBff;

    public PostgresDatabase SecretIngestion { get; } = secretIngestion;

    public PostgresDatabase ConversionWorker { get; } = conversionWorker;

    public string ConversionWorkerConnectionString { get; } = conversionWorkerConnectionString;

    public PostgresDatabase StrategyVerifier { get; } = strategyVerifier;

    public string RuntimeEvidenceConnectionString { get; } =
        runtimeEvidenceConnectionString;

    public PostgresDatabase Worker { get; } = worker;

    public string WorkerConnectionString { get; } = workerConnectionString;

    public PostgresDatabase SupervisorRuntime { get; } = supervisorRuntime;

    public string SupervisorRuntimeConnectionString { get; } = supervisorRuntimeConnectionString;

    public PostgresDatabase TradeAuthorizer { get; } = tradeAuthorizer;

    public PostgresDatabase GatewayRuntime { get; } = gatewayRuntime;

    public string GatewayRuntimeConnectionString { get; } = gatewayRuntimeConnectionString;

    public PostgresDatabase CredentialRuntime { get; } = credentialRuntime;

    public string CredentialRuntimeConnectionString { get; } = credentialRuntimeConnectionString;

    public string LocalIdentityConnectionString { get; } = localIdentityConnectionString;

    public async ValueTask DisposeAsync()
    {
        await CredentialRuntime.DisposeAsync().ConfigureAwait(false);
        await GatewayRuntime.DisposeAsync().ConfigureAwait(false);
        await TradeAuthorizer.DisposeAsync().ConfigureAwait(false);
        await SupervisorRuntime.DisposeAsync().ConfigureAwait(false);
        await Worker.DisposeAsync().ConfigureAwait(false);
        await StrategyVerifier.DisposeAsync().ConfigureAwait(false);
        await ConversionWorker.DisposeAsync().ConfigureAwait(false);
        await SecretIngestion.DisposeAsync().ConfigureAwait(false);
        await AdminBff.DisposeAsync().ConfigureAwait(false);
        await ControlApi.DisposeAsync().ConfigureAwait(false);
        await Application.DisposeAsync().ConfigureAwait(false);
        await TenantContextCapabilityProvider.DisposeAsync().ConfigureAwait(false);
        await Administrator.DisposeAsync().ConfigureAwait(false);
    }
}
