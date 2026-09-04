using System;
using Npgsql;

string rootCert = @"C:\Users\Dev23\Desktop\yo4x\.local\development\certificates\postgres-server.crt";
string adminPass = Environment.GetEnvironmentVariable("YO4X_ADMIN_PASS") ?? "";

string connStr = $"Host=127.0.0.1;Port=55432;Database=yo4x_development;Username=postgres;Password={adminPass};SSL Mode=VerifyFull;Root Certificate={rootCert};Pooling=false;";

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand("SELECT name, slug, symbol, timeframe, is_drm_protected, version, category FROM catalog.strategies ORDER BY name;", conn);
await using var reader = await cmd.ExecuteReaderAsync();

Console.WriteLine("=== Strategies in Catalog ===");
while (await reader.ReadAsync())
{
    Console.WriteLine($"Name: '{reader["name"]}', Symbol: '{reader["symbol"]}', Timeframe: '{reader["timeframe"]}', DRM: {reader["is_drm_protected"]}, Version: '{reader["version"]}'");
}
