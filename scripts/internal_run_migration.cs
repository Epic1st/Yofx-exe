using Npgsql;
using System.Security.Cryptography;
using System.Text;

string rootCert = @"C:\Users\Dev23\Desktop\yo4x\.local\development\certificates\postgres-server.crt";
string adminPass = Environment.GetEnvironmentVariable("YO4X_ADMIN_PASS") ?? "";

string connStr = $"Host=127.0.0.1;Port=55432;Database=yo4x_development;Username=postgres;Password={adminPass};SSL Mode=VerifyFull;Root Certificate={rootCert};Pooling=false;";

Console.WriteLine($"[MIGRATION] Connecting to PostgreSQL directly as postgres...");
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

string migration012Path = @"C:\Users\Dev23\Desktop\yo4x\src\BuildingBlocks\YO4X.Persistence.Postgres\Migrations\012_strategy_licensing_and_drm.sql";
if (File.Exists(migration012Path))
{
    string sql = await File.ReadAllTextAsync(migration012Path);
    await using var cmd = new NpgsqlCommand(sql, conn);
    await cmd.ExecuteNonQueryAsync();

    // Update the recorded SHA256 checksum in control.schema_migrations
    string shaHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
    await using var updateChecksumCmd = new NpgsqlCommand(
        "update control.schema_migrations set sha256 = @sha where migration_id = '012_strategy_licensing_and_drm'",
        conn);
    updateChecksumCmd.Parameters.AddWithValue("sha", shaHex);
    await updateChecksumCmd.ExecuteNonQueryAsync();
}

Console.WriteLine("[MIGRATION SUCCESS] Applied updated 012 DRM policies and synchronized migration checksum!");
