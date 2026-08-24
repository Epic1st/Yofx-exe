using System.Globalization;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class TenantContextCapabilityPostgresTests(
    PostgresContainerFixture postgres)
{
    [PostgresFact]
    public async Task CapabilityIsSingleUseAndBoundToExactRoleBackendTransactionAndContext()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        byte[] wrongContextCapability = RandomNumberGenerator.GetBytes(32);
        byte[] expiredCapability = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = new TenantExecutionContext(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
            await using NpgsqlConnection worker =
                await OpenNonPooledAsync(database.WorkerConnectionString);

            TransactionBinding firstBinding;
            await using (NpgsqlTransaction first = await worker.BeginTransactionAsync())
            {
                firstBinding = await ReadBindingAsync(worker, first);
                await IssueAsync(
                    database.ContextIssuerConnectionString,
                    capability,
                    firstBinding,
                    context);
                await ActivateAsync(worker, first, capability, context);

                await using (var verify = new NpgsqlCommand(
                    """
                    select control.current_tenant_id() = @tenant_id,
                        control.current_actor_id() = @actor_id,
                        control.current_correlation_id() = @correlation_id,
                        control.current_session_id() = @session_id
                    """,
                    worker,
                    first))
                {
                    AddContext(verify, context);
                    await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.True(reader.GetBoolean(0));
                    Assert.True(reader.GetBoolean(1));
                    Assert.True(reader.GetBoolean(2));
                    Assert.True(reader.GetBoolean(3));
                    Assert.False(await reader.ReadAsync());
                }

                PostgresException replayRejected =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => ActivateAsync(worker, first, capability, context));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    replayRejected.SqlState);
                await first.RollbackAsync();
            }

            await using (NpgsqlTransaction later = await worker.BeginTransactionAsync())
            {
                TransactionBinding laterBinding = await ReadBindingAsync(worker, later);
                Assert.NotEqual(firstBinding.TransactionId, laterBinding.TransactionId);
                PostgresException rollbackReplayRejected =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => ActivateAsync(worker, later, capability, context));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    rollbackReplayRejected.SqlState);
                await later.RollbackAsync();
            }

            await using (NpgsqlTransaction wrongContext = await worker.BeginTransactionAsync())
            {
                TransactionBinding binding = await ReadBindingAsync(worker, wrongContext);
                await IssueAsync(
                    database.ContextIssuerConnectionString,
                    wrongContextCapability,
                    binding,
                    context);
                var otherContext = new TenantExecutionContext(
                    Guid.CreateVersion7(),
                    context.ActorId,
                    context.CorrelationId,
                    context.SessionId);
                PostgresException mismatchRejected =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => ActivateAsync(
                            worker,
                            wrongContext,
                            wrongContextCapability,
                            otherContext));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    mismatchRejected.SqlState);
                await wrongContext.RollbackAsync();
            }

            await using (NpgsqlTransaction expiring = await worker.BeginTransactionAsync())
            {
                TransactionBinding binding = await ReadBindingAsync(worker, expiring);
                await IssueAsync(
                    database.ContextIssuerConnectionString,
                    expiredCapability,
                    binding,
                    context);
                await Task.Delay(TimeSpan.FromMilliseconds(15_500));
                PostgresException expiredRejected =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => ActivateAsync(
                            worker,
                            expiring,
                            expiredCapability,
                            context));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    expiredRejected.SqlState);
                await expiring.RollbackAsync();
            }

            await using NpgsqlConnection issuer =
                await OpenNonPooledAsync(database.ContextIssuerConnectionString);
            await using var cleanup = new NpgsqlCommand(
                "select control.cleanup_tenant_context_capabilities(1000)",
                issuer);
            Assert.True(
                Convert.ToInt32(
                    await cleanup.ExecuteScalarAsync(),
                    CultureInfo.InvariantCulture) >= 3);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
            CryptographicOperations.ZeroMemory(wrongContextCapability);
            CryptographicOperations.ZeroMemory(expiredCapability);
        }
    }

    [PostgresFact]
    public async Task CallerWritableGucsAndRawTableAccessCannotCreateTenantAuthority()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using NpgsqlConnection worker =
            await OpenNonPooledAsync(database.WorkerConnectionString);
        await using (NpgsqlTransaction transaction = await worker.BeginTransactionAsync())
        {
            await using (var fakeContext = new NpgsqlCommand(
                """
                select set_config('yo4x.tenant_id', @tenant_id, true),
                    set_config('yo4x.actor_id', @actor_id, true),
                    set_config('yo4x.correlation_id', @correlation_id, true)
                """,
                worker,
                transaction))
            {
                fakeContext.Parameters.AddWithValue(
                    "tenant_id",
                    NpgsqlDbType.Text,
                    tenantId.ToString("D"));
                fakeContext.Parameters.AddWithValue(
                    "actor_id",
                    NpgsqlDbType.Text,
                    Guid.CreateVersion7().ToString("D"));
                fakeContext.Parameters.AddWithValue(
                    "correlation_id",
                    NpgsqlDbType.Text,
                    Guid.CreateVersion7().ToString("D"));
                await fakeContext.ExecuteNonQueryAsync();
            }

            await using var current = new NpgsqlCommand(
                """
                select control.current_tenant_id() is null,
                    control.current_actor_id() is null,
                    control.current_correlation_id() is null,
                    control.current_session_id() is null
                """,
                worker,
                transaction);
            await using (NpgsqlDataReader reader = await current.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.True(reader.GetBoolean(0));
                Assert.True(reader.GetBoolean(1));
                Assert.True(reader.GetBoolean(2));
                Assert.True(reader.GetBoolean(3));
                Assert.False(await reader.ReadAsync());
            }

            await transaction.RollbackAsync();
        }

        foreach (string connectionString in new[]
        {
            database.WorkerConnectionString,
            database.ContextIssuerConnectionString
        })
        {
            await using NpgsqlConnection deniedConnection =
                await OpenNonPooledAsync(connectionString);
            foreach (string sql in new[]
            {
                "select count(*) from control.tenant_context_capabilities",
                "delete from control.tenant_context_capabilities"
            })
            {
                await using var denied = new NpgsqlCommand(sql, deniedConnection);
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => denied.ExecuteNonQueryAsync());
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    rejected.SqlState);
            }
        }

        await using var issuerByRuntime = new NpgsqlCommand(
            """
            select control.issue_tenant_context_capability(
                decode(repeat('11', 32), 'hex'), current_database(),
                'yo4x_worker', pg_backend_pid(), pg_current_xact_id()::text,
                @tenant_id, @actor_id, @correlation_id, null)
            """,
            worker);
        issuerByRuntime.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        issuerByRuntime.Parameters.AddWithValue(
            "actor_id",
            NpgsqlDbType.Uuid,
            Guid.CreateVersion7());
        issuerByRuntime.Parameters.AddWithValue(
            "correlation_id",
            NpgsqlDbType.Uuid,
            Guid.CreateVersion7());
        PostgresException issuerRejected = await Assert.ThrowsAsync<PostgresException>(
            () => issuerByRuntime.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, issuerRejected.SqlState);
    }

    private static async Task<TransactionBinding> ReadBindingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            select current_database(), current_user, pg_backend_pid(),
                pg_current_xact_id()::text
            """,
            connection,
            transaction);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var binding = new TransactionBinding(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3));
        Assert.False(await reader.ReadAsync());
        return binding;
    }

    private static async Task IssueAsync(
        string issuerConnectionString,
        byte[] capability,
        TransactionBinding binding,
        TenantExecutionContext context)
    {
        byte[] digest = SHA256.HashData(capability);
        try
        {
            await using NpgsqlConnection issuer =
                await OpenNonPooledAsync(issuerConnectionString);
            await using var command = new NpgsqlCommand(
                """
                select control.issue_tenant_context_capability(
                    @capability_sha256, @database_name, @runtime_role,
                    @backend_pid, @transaction_id, @tenant_id, @actor_id,
                    @correlation_id, @session_id)
                """,
                issuer);
            command.Parameters.AddWithValue(
                "capability_sha256",
                NpgsqlDbType.Bytea,
                digest);
            command.Parameters.AddWithValue(
                "database_name",
                NpgsqlDbType.Text,
                binding.DatabaseName);
            command.Parameters.AddWithValue(
                "runtime_role",
                NpgsqlDbType.Text,
                binding.RuntimeRole);
            command.Parameters.AddWithValue(
                "backend_pid",
                NpgsqlDbType.Integer,
                binding.BackendPid);
            command.Parameters.AddWithValue(
                "transaction_id",
                NpgsqlDbType.Text,
                binding.TransactionId);
            AddContext(command, context);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static async Task ActivateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        byte[] capability,
        TenantExecutionContext context)
    {
        await using var command = new NpgsqlCommand(
            """
            select control.activate_tenant_context(
                @capability, @tenant_id, @actor_id, @correlation_id, @session_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("capability", NpgsqlDbType.Bytea, capability);
        AddContext(command, context);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddContext(
        NpgsqlCommand command,
        TenantExecutionContext context)
    {
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
    }

    private static async Task<NpgsqlConnection> OpenNonPooledAsync(
        string connectionString)
    {
        var options = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };
        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed record TransactionBinding(
        string DatabaseName,
        string RuntimeRole,
        int BackendPid,
        string TransactionId);
}
