using Npgsql;

namespace YO4X.Postgres.IntegrationTests;

/// <summary>
/// The integration harness temporarily broadens the fixed emergency role for
/// low-level RLS fixtures. Production readiness re-applies the exact role script
/// before attestation and only restores the fixture grants when later seeding
/// in the same test requires them.
/// </summary>
internal static class PostgresProductionReadinessFixture
{
    public static async Task RemoveBroadActorGrantsAsync(PostgresTestDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
    }

    public static async Task RestoreBroadActorGrantsAsync(PostgresTestDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await PostgresContainerFixture.ApplyBroadActorGrantsAsync(administrator);
    }
}
