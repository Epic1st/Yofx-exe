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

    public async Task InitializeAsync()
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

    public async Task DisposeAsync()
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
        string roleName = $"yo4x_app_{suffix}";
        string rolePassword = CreateEphemeralPassword();
        string controlApiPassword = CreateEphemeralPassword();
        string adminBffPassword = CreateEphemeralPassword();
        string secretIngestionPassword = CreateEphemeralPassword();
        string conversionWorkerPassword = CreateEphemeralPassword();
        string strategyVerifierPassword = CreateEphemeralPassword();
        string workerPassword = CreateEphemeralPassword();
        string tradeAuthorizerPassword = CreateEphemeralPassword();
        string gatewayRuntimePassword = CreateEphemeralPassword();

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
            string quotedRole = QuoteIdentifier(roleName);
            string quotedDatabase = QuoteIdentifier(databaseName);
            string escapedPassword = rolePassword.Replace("'", "''", StringComparison.Ordinal);

            await using (var createRole = new NpgsqlCommand(
                $"create role {quotedRole} login password '{escapedPassword}' "
                + "nosuperuser nocreatedb nocreaterole noinherit nobypassrls noreplication; "
                + $"alter role {quotedRole} set log_parameter_max_length = 0; "
                + $"alter role {quotedRole} set log_parameter_max_length_on_error = 0",
                serverConnection))
            {
                await createRole.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

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
                "yo4x_worker",
                workerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_trade_authorizer",
                tradeAuthorizerPassword).ConfigureAwait(false);
            await EnableRoleLoginAsync(
                serverConnection,
                "yo4x_gateway_runtime",
                gatewayRuntimePassword).ConfigureAwait(false);

            await using var createDatabase = new NpgsqlCommand(
                $"create database {quotedDatabase} owner postgres",
                serverConnection);
            await createDatabase.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var administratorBuilder = new NpgsqlConnectionStringBuilder(_administratorConnectionString)
        {
            Database = databaseName,
            IncludeErrorDetail = true,
            Pooling = true
        };
        var administratorDatabase = new PostgresDatabase(
            administratorBuilder.ConnectionString,
            PostgresDatabaseUsage.Migrator);
        await administratorDatabase.MigrateAsync().ConfigureAwait(false);

        await using (NpgsqlConnection administratorConnection =
            await administratorDatabase.OpenConnectionAsync().ConfigureAwait(false))
        {
            await ApplyLeastPrivilegeRoleScriptAsync(administratorConnection).ConfigureAwait(false);
            string quotedRole = QuoteIdentifier(roleName);
            string grantSql = $"""
                grant connect on database {QuoteIdentifier(databaseName)} to {quotedRole};
                grant usage on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel to {quotedRole};
                grant select, insert, update on all tables in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel to {quotedRole};
                grant execute on function control.current_tenant_id() to {quotedRole};
                grant execute on function control.current_actor_id() to {quotedRole};
                grant execute on function control.current_correlation_id() to {quotedRole};
                grant execute on function control.current_session_id() to {quotedRole};
                grant execute on function control.assert_safe_runtime_role() to {quotedRole};
                grant execute on function control.acquire_u0_authority_lock() to {quotedRole};
                grant execute on function control.acquire_u0_tenant_authority_lock(uuid) to {quotedRole};
                """;
            await using var grants = new NpgsqlCommand(grantSql, administratorConnection);
            await grants.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var applicationBuilder = new NpgsqlConnectionStringBuilder(administratorBuilder.ConnectionString)
        {
            Username = roleName,
            Password = rolePassword,
            IncludeErrorDetail = false,
            LogParameters = false,
            MaxPoolSize = 10,
            MinPoolSize = 0,
            NoResetOnClose = false
        };
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
        var strategyVerifierBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_strategy_verifier",
            strategyVerifierPassword);
        var tradeAuthorizerBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_trade_authorizer",
            tradeAuthorizerPassword);
        var gatewayRuntimeBuilder = CreateRuntimeConnectionBuilder(
            administratorBuilder,
            "yo4x_gateway_runtime",
            gatewayRuntimePassword);

        return new PostgresTestDatabase(
            administratorDatabase,
            new PostgresDatabase(applicationBuilder.ConnectionString),
            applicationBuilder.ConnectionString,
            new PostgresDatabase(controlApiBuilder.ConnectionString),
            new PostgresDatabase(adminBffBuilder.ConnectionString),
            new PostgresDatabase(secretIngestionBuilder.ConnectionString),
            new PostgresDatabase(conversionWorkerBuilder.ConnectionString),
            conversionWorkerBuilder.ConnectionString,
            new PostgresDatabase(strategyVerifierBuilder.ConnectionString),
            new PostgresDatabase(workerBuilder.ConnectionString),
            new PostgresDatabase(tradeAuthorizerBuilder.ConnectionString),
            new PostgresDatabase(gatewayRuntimeBuilder.ConnectionString));
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
                    'yo4x_admin_bff',
                    'yo4x_emergency',
                    'yo4x_secret_ingestion',
                    'yo4x_conversion_worker',
                    'yo4x_strategy_verifier',
                    'yo4x_runtime_evidence',
                    'yo4x_worker',
                    'yo4x_trade_authorizer',
                    'yo4x_gateway_runtime'
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

    private static async Task ApplyLeastPrivilegeRoleScriptAsync(NpgsqlConnection connection)
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

        builder.Database = "postgres";
        builder.Pooling = false;
        builder.Timeout = Math.Clamp(builder.Timeout, 1, 5);
        builder.CommandTimeout = Math.Clamp(builder.CommandTimeout, 1, 30);
        builder.IncludeErrorDetail = true;

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
    PostgresDatabase application,
    string applicationConnectionString,
    PostgresDatabase controlApi,
    PostgresDatabase adminBff,
    PostgresDatabase secretIngestion,
    PostgresDatabase conversionWorker,
    string conversionWorkerConnectionString,
    PostgresDatabase strategyVerifier,
    PostgresDatabase worker,
    PostgresDatabase tradeAuthorizer,
    PostgresDatabase gatewayRuntime) : IAsyncDisposable
{
    public PostgresDatabase Administrator { get; } = administrator;

    public PostgresDatabase Application { get; } = application;

    public string ApplicationConnectionString { get; } = applicationConnectionString;

    public PostgresDatabase ControlApi { get; } = controlApi;

    public PostgresDatabase AdminBff { get; } = adminBff;

    public PostgresDatabase SecretIngestion { get; } = secretIngestion;

    public PostgresDatabase ConversionWorker { get; } = conversionWorker;

    public string ConversionWorkerConnectionString { get; } = conversionWorkerConnectionString;

    public PostgresDatabase StrategyVerifier { get; } = strategyVerifier;

    public PostgresDatabase Worker { get; } = worker;

    public PostgresDatabase TradeAuthorizer { get; } = tradeAuthorizer;

    public PostgresDatabase GatewayRuntime { get; } = gatewayRuntime;

    public async ValueTask DisposeAsync()
    {
        await GatewayRuntime.DisposeAsync().ConfigureAwait(false);
        await TradeAuthorizer.DisposeAsync().ConfigureAwait(false);
        await Worker.DisposeAsync().ConfigureAwait(false);
        await StrategyVerifier.DisposeAsync().ConfigureAwait(false);
        await ConversionWorker.DisposeAsync().ConfigureAwait(false);
        await SecretIngestion.DisposeAsync().ConfigureAwait(false);
        await AdminBff.DisposeAsync().ConfigureAwait(false);
        await ControlApi.DisposeAsync().ConfigureAwait(false);
        await Application.DisposeAsync().ConfigureAwait(false);
        await Administrator.DisposeAsync().ConfigureAwait(false);
    }
}
