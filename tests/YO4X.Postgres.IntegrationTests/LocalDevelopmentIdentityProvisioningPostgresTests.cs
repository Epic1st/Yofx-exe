using Npgsql;
using NpgsqlTypes;
using YO4X.DevelopmentIdentity;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class LocalDevelopmentIdentityProvisioningPostgresTests(
    PostgresContainerFixture fixture)
{
    [PostgresFact]
    public async Task DedicatedFunctionIdempotentlyCreatesOnlyFixedVerifiedAuthority()
    {
        await using PostgresTestDatabase database = await fixture.CreateDatabaseAsync();
        Assert.True(LocalIdentityPostgresOptions.TryCreate(
            database.LocalIdentityConnectionString,
            out LocalIdentityPostgresOptions? options));
        var provisioner = new LocalIdentityProvisioner(options!, TimeProvider.System);
        var user = new DevelopmentUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = LocalIdentityContract.TenantId,
            SessionId = Guid.CreateVersion7(),
            Email = "person@example.test",
            NormalizedEmail = "PERSON@EXAMPLE.TEST",
            EmailConfirmed = true
        };

        await provisioner.ProvisionAsync(user, TestContext.Current.CancellationToken);
        await provisioner.ProvisionAsync(user, TestContext.Current.CancellationToken);

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select tenant.slug, identity.normalized_email, identity.security_state,
                   identity.email_verified_at is not null, session.state,
                   session.expires_at > statement_timestamp(),
                   session.expires_at <= statement_timestamp() + interval '8 hours 1 minute',
                   length(session.current_token_hash)
            from identity.tenants as tenant
            join identity.user_identities as identity on identity.tenant_id = tenant.id
            join identity.user_session_families as session
              on session.tenant_id = identity.tenant_id and session.user_id = identity.id
            where tenant.id = @tenant_id and identity.id = @user_id and session.id = @session_id
            """,
            administrator);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, user.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, user.Id);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, user.SessionId);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("local-development", reader.GetString(0));
        Assert.Equal(user.NormalizedEmail, reader.GetString(1));
        Assert.Equal("active", reader.GetString(2));
        Assert.True(reader.GetBoolean(3));
        Assert.Equal("active", reader.GetString(4));
        Assert.True(reader.GetBoolean(5));
        Assert.True(reader.GetBoolean(6));
        Assert.Equal(64, reader.GetInt32(7));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [PostgresFact]
    public async Task CollisionAndDirectTableMutationFailClosed()
    {
        await using PostgresTestDatabase database = await fixture.CreateDatabaseAsync();
        Assert.True(LocalIdentityPostgresOptions.TryCreate(
            database.LocalIdentityConnectionString,
            out LocalIdentityPostgresOptions? options));
        var provisioner = new LocalIdentityProvisioner(options!, TimeProvider.System);
        var user = new DevelopmentUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = LocalIdentityContract.TenantId,
            SessionId = Guid.CreateVersion7(),
            Email = "first@example.test",
            NormalizedEmail = "FIRST@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        await provisioner.ProvisionAsync(user, TestContext.Current.CancellationToken);

        DevelopmentUser collision = user.WithNormalizedEmail("SECOND@EXAMPLE.TEST");
        PostgresException conflict = await Assert.ThrowsAsync<PostgresException>(() =>
            provisioner.ProvisionAsync(collision, TestContext.Current.CancellationToken));
        Assert.Equal("23505", conflict.SqlState);

        await using var local = new NpgsqlConnection(database.LocalIdentityConnectionString);
        await local.OpenAsync(TestContext.Current.CancellationToken);
        await using var direct = new NpgsqlCommand(
            "update identity.user_identities set normalized_email = 'ATTACKER@EXAMPLE.TEST' where id = @id",
            local);
        direct.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, user.Id);
        PostgresException denied = await Assert.ThrowsAsync<PostgresException>(() =>
            direct.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501", denied.SqlState);
    }
}

file static class DevelopmentUserTestExtensions
{
    internal static DevelopmentUser WithNormalizedEmail(
        this DevelopmentUser source,
        string normalizedEmail) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        SessionId = source.SessionId,
        Email = normalizedEmail,
        NormalizedEmail = normalizedEmail,
        EmailConfirmed = true
    };
}
