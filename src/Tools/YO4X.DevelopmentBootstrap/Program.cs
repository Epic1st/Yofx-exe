using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Npgsql;
using YO4X.Persistence.Postgres;

const string AdministratorConnectionVariable = "YO4X_BOOTSTRAP_ADMIN_CONNECTION";
const string CertificatePasswordVariable = "YO4X_BOOTSTRAP_CERTIFICATE_PASSWORD";
const string DatabaseName = "yo4x_development";
const string DevelopmentBrokerProfileId = "019c8d27-763d-7000-8000-000000000002";
const string DevelopmentBrokerId = "019c8d27-763d-7000-8000-000000000003";
const string DevelopmentBrokerServer = "MetaQuotes-Demo";

string[] runtimeRoles =
[
    "yo4x_context_issuer",
    "yo4x_local_identity",
    "yo4x_control_api",
    "yo4x_admin_bff",
    "yo4x_emergency",
    "yo4x_secret_ingestion",
    "yo4x_conversion_worker",
    "yo4x_strategy_verifier",
    "yo4x_runtime_evidence",
    "yo4x_worker",
    "yo4x_supervisor_runtime",
    "yo4x_trade_authorizer",
    "yo4x_gateway_runtime",
    "yo4x_credential_runtime"
];

if (args.Length == 0)
{
    throw new InvalidOperationException("A bootstrap command is required.");
}

switch (args[0])
{
    case "export-postgres-certificate" when args.Length == 4:
        ExportPostgresCertificate(args[1], args[2], args[3]);
        break;
    case "database" when args.Length == 2:
        await BootstrapDatabaseAsync(args[1]);
        break;
    case "new-policy-public-key" when args.Length == 1:
        using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Console.Write(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }
        break;
    case "validate-local-identity-connection" when args.Length == 1:
        ValidateLocalIdentityConnection();
        break;
    case "catalog-fingerprint" when args.Length == 1:
        await PrintCatalogFingerprintAsync();
        break;
    default:
        throw new InvalidOperationException("The bootstrap command or arguments are invalid.");
}

// Re-pinning PostgresCatalogSemanticFingerprint.ExpectedSha256 after an additive
// migration or a role-script change requires the value actually produced by a
// database provisioned solely from the embedded migrations plus the role script.
// This prints exactly that, so the pin is re-derived rather than guessed.
async Task PrintCatalogFingerprintAsync()
{
    var builder = new NpgsqlConnectionStringBuilder(
        RequiredEnvironment(AdministratorConnectionVariable))
    {
        Database = DatabaseName,
        Pooling = false,
        IncludeErrorDetail = false,
        LogParameters = false
    };
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    Console.Write(await PostgresCatalogSemanticFingerprint.ComputeSha256Async(connection));
}

void ValidateLocalIdentityConnection()
{
    var builder = new NpgsqlConnectionStringBuilder(
        RequiredEnvironment("YO4X_BOOTSTRAP_LOCAL_IDENTITY_CONNECTION"));
    if (!string.Equals(builder.Username, "yo4x_local_identity", StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(builder.Password)
        || string.IsNullOrWhiteSpace(builder.Database)
        || !string.Equals(builder.Host, "127.0.0.1", StringComparison.Ordinal)
        || builder.Multiplexing
        || builder.IncludeErrorDetail
        || builder.LogParameters
        || builder.NoResetOnClose)
    {
        throw new InvalidOperationException("The local identity runtime connection failed its non-secret preflight.");
    }
}

void ExportPostgresCertificate(string pfxPath, string certificatePath, string privateKeyPath)
{
    string password = RequiredEnvironment(CertificatePasswordVariable);
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        Path.GetFullPath(pfxPath),
        password,
        X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    using RSA privateKey = certificate.GetRSAPrivateKey()
        ?? throw new InvalidOperationException("The HTTPS development certificate does not have an RSA private key.");

    WriteAtomic(Path.GetFullPath(certificatePath), certificate.ExportCertificatePem());
    WriteAtomic(Path.GetFullPath(privateKeyPath), privateKey.ExportPkcs8PrivateKeyPem());
}

async Task BootstrapDatabaseAsync(string roleScriptPath)
{
    string administratorConnection = RequiredEnvironment(AdministratorConnectionVariable);
    var serverBuilder = new NpgsqlConnectionStringBuilder(administratorConnection)
    {
        Database = "postgres",
        Pooling = false,
        IncludeErrorDetail = false,
        LogParameters = false
    };

    await using (var connection = new NpgsqlConnection(serverBuilder.ConnectionString))
    {
        await connection.OpenAsync();
        await EnsureRolesAsync(connection);
        foreach (string role in runtimeRoles)
        {
            await SetRolePasswordAsync(connection, role, RequiredEnvironment(RolePasswordVariable(role)));
        }

        await using var exists = new NpgsqlCommand(
            "select exists(select 1 from pg_catalog.pg_database where datname = @name)",
            connection);
        exists.Parameters.AddWithValue("name", DatabaseName);
        if (await exists.ExecuteScalarAsync() is not true)
        {
            await using var create = new NpgsqlCommand(
                "create database yo4x_development owner postgres template template0 encoding 'UTF8' locale_provider libc lc_collate 'C' lc_ctype 'C'",
                connection);
            await create.ExecuteNonQueryAsync();
        }
    }

    var databaseBuilder = new NpgsqlConnectionStringBuilder(administratorConnection)
    {
        Database = DatabaseName,
        Pooling = false,
        IncludeErrorDetail = false,
        LogParameters = false
    };
    await using var database = new PostgresDatabase(
        databaseBuilder.ConnectionString,
        PostgresDatabaseUsage.Migrator);
    await database.MigrateAsync();
    await using NpgsqlConnection administrator = await database.OpenConnectionAsync();
    await SeedDevelopmentBrokerProfileAsync(administrator);
    string roleSql = await File.ReadAllTextAsync(Path.GetFullPath(roleScriptPath));
    if (string.IsNullOrWhiteSpace(roleSql))
    {
        throw new InvalidOperationException("The least-privilege role script is empty.");
    }

    await using (var applyRoles = new NpgsqlCommand(roleSql, administrator)
    {
        CommandTimeout = 180
    })
    {
        await applyRoles.ExecuteNonQueryAsync();
    }

    await VerifyRuntimeRolesAsync(administrator);
}

async Task SeedDevelopmentBrokerProfileAsync(NpgsqlConnection connection)
{
    // This executable provisions only the workspace-local development database.
    // Registration options are still resolved through the normal authenticated
    // governance read; this seed merely makes the launcher's configured profile
    // exist and never contains a broker login or credential.
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await using (var fixtureMode = new NpgsqlCommand(
        "set local session_replication_role = replica",
        connection,
        transaction))
    {
        await fixtureMode.ExecuteNonQueryAsync();
    }

    await using (var insert = new NpgsqlCommand(
        """
        insert into governance.broker_profiles
            (id, broker_id, profile_version, broker_company, server_name,
             environment_support, capabilities, cloud_rules, limitations,
             evidence_sha256, tested_at, state)
        values
            (@profile_id, @broker_id, 1, 'MetaQuotes', @server,
             array['demo'],
             '{"connectionTestOnly":true,"trading":false}'::jsonb,
             '{"developmentOnly":true}'::jsonb,
             '{"noTrading":true,"noCredentialMaterial":true}'::jsonb,
             @evidence_sha256, '2026-08-24T00:00:00Z'::timestamptz, 'approved')
        on conflict (id) do nothing
        """,
        connection,
        transaction))
    {
        insert.Parameters.AddWithValue("profile_id", Guid.Parse(DevelopmentBrokerProfileId));
        insert.Parameters.AddWithValue("broker_id", Guid.Parse(DevelopmentBrokerId));
        insert.Parameters.AddWithValue("server", DevelopmentBrokerServer);
        insert.Parameters.AddWithValue(
            "evidence_sha256",
            Convert.ToHexString(SHA256.HashData(
                "YO4X/development-broker-profile/v1\0MetaQuotes\0MetaQuotes-Demo\0demo\0connection-test-only"u8))
                .ToLowerInvariant());
        await insert.ExecuteNonQueryAsync();
    }

    await using (var verify = new NpgsqlCommand(
        """
        select count(*)
        from governance.broker_profiles
        where id = @profile_id
          and broker_id = @broker_id
          and profile_version = 1
          and broker_company = 'MetaQuotes'
          and server_name = @server
          and environment_support = array['demo']
          and state = 'approved'
          and capabilities ->> 'connectionTestOnly' = 'true'
          and capabilities ->> 'trading' = 'false'
        """,
        connection,
        transaction))
    {
        verify.Parameters.AddWithValue("profile_id", Guid.Parse(DevelopmentBrokerProfileId));
        verify.Parameters.AddWithValue("broker_id", Guid.Parse(DevelopmentBrokerId));
        verify.Parameters.AddWithValue("server", DevelopmentBrokerServer);
        if (await verify.ExecuteScalarAsync() is not long count || count != 1)
        {
            throw new InvalidOperationException(
                "The development broker profile does not match the launcher's approved metadata.");
        }
    }

    await transaction.CommitAsync();
}

async Task EnsureRolesAsync(NpgsqlConnection connection)
{
    const string sql = """
        do $$
        declare
            required_role text;
        begin
            foreach required_role in array array[
                'yo4x_migrator', 'yo4x_context_authority',
                'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api',
                'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion',
                'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                'yo4x_runtime_evidence', 'yo4x_worker', 'yo4x_supervisor_runtime',
                'yo4x_trade_authorizer', 'yo4x_gateway_runtime',
                'yo4x_credential_runtime'
            ]
            loop
                if not exists (select 1 from pg_catalog.pg_roles where rolname = required_role) then
                    execute format(
                        'create role %I nologin noinherit nosuperuser nobypassrls nocreatedb nocreaterole noreplication connection limit -1',
                        required_role);
                end if;
            end loop;
        end
        $$;
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static async Task SetRolePasswordAsync(NpgsqlConnection connection, string role, string password)
{
    await using var quote = new NpgsqlCommand(
        "select format('alter role %I login noinherit nosuperuser nobypassrls nocreatedb nocreaterole noreplication password %L', @role, @password)",
        connection);
    quote.Parameters.AddWithValue("role", role);
    quote.Parameters.AddWithValue("password", password);
    string sql = (string)(await quote.ExecuteScalarAsync()
        ?? throw new InvalidOperationException("PostgreSQL did not produce a role alteration statement."));
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task VerifyRuntimeRolesAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        select count(*)
        from pg_catalog.pg_roles as role
        where role.rolname = any(@roles)
          and role.rolcanlogin
          and not role.rolinherit
          and not role.rolsuper
          and not role.rolbypassrls
          and not role.rolcreatedb
          and not role.rolcreaterole
          and not role.rolreplication
          and not exists
          (
              select 1 from pg_catalog.pg_auth_members as membership
              where membership.member = role.oid or membership.roleid = role.oid
          )
        """,
        connection);
    command.Parameters.AddWithValue("roles", runtimeRoles);
    long count = (long)(await command.ExecuteScalarAsync()
        ?? throw new InvalidOperationException("Runtime-role verification returned no result."));
    if (count != runtimeRoles.Length)
    {
        throw new InvalidOperationException("The direct runtime-role boundary was not provisioned exactly.");
    }
}

static string RolePasswordVariable(string role) =>
    "YO4X_BOOTSTRAP_PASSWORD_" + role[5..].ToUpperInvariant();

static string RequiredEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required environment variable '{name}' is missing.");

static void WriteAtomic(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string temporary = path + ".new";
    File.WriteAllText(temporary, content);
    File.Move(temporary, path, overwrite: true);
}
