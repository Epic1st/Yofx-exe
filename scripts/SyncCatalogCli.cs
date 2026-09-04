#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;
using YO4X.Tenancy;

namespace SyncCatalog;

internal sealed class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Synchronizing MQL5 & Protected YO4X Strategies to Catalog ===");

        string rootCert = @"C:\Users\Dev23\Desktop\yo4x\.local\development\certificates\postgres-server.crt";
        string apiPass = Environment.GetEnvironmentVariable("YO4X_API_PASS") ?? "";
        string issuerPass = Environment.GetEnvironmentVariable("YO4X_ISSUER_PASS") ?? "";

        string baseConn = $"Host=127.0.0.1;Port=55432;Database=yo4x_development;SSL Mode=VerifyFull;Root Certificate={rootCert};";
        string apiConn = $"{baseConn}Username=yo4x_control_api;Password={apiPass};";
        string issuerConn = $"{baseConn}Username=yo4x_context_issuer;Password={issuerPass};";

        string mq5Dir = @"C:\Users\Dev23\Desktop\yo4x\Testing\Mq5";
        if (!Directory.Exists(mq5Dir))
        {
            Console.WriteLine($"Directory not found: {mq5Dir}");
            return;
        }

        Guid tenantId = Guid.Parse("019c8d27-763d-7000-8000-000000000001");
        Guid actorId = Guid.Parse("019c8d27-763d-7000-8000-000000000002");

        var capabilityProvider = new PostgresTenantContextCapabilityProvider(issuerConn);
        var database = new PostgresDatabase(apiConn, PostgresDatabaseUsage.Runtime, capabilityProvider);

        // 1. Process Protected .yo4x packages first
        var yo4xFiles = Directory.GetFiles(mq5Dir, "*.yo4x", SearchOption.AllDirectories);
        Console.WriteLine($"Found {yo4xFiles.Length} encrypted .yo4x DRM packages in {mq5Dir}.\n");

        foreach (var file in yo4xFiles)
        {
            string fileName = Path.GetFileName(file);
            byte[] packageBytes = await File.ReadAllBytesAsync(file);
            string sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();

            Yo4xStrategyManifest manifest;
            try
            {
                manifest = Yo4xStrategyPackage.ReadManifest(packageBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not parse package manifest for {fileName}: {ex.Message}");
                continue;
            }

            // Compute deterministic ID based on base strategy name
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            byte[] nameDigest = SHA256.HashData(Encoding.UTF8.GetBytes(baseName.ToLowerInvariant()));
            byte[] guidBytes = new byte[16];
            Array.Copy(nameDigest, 0, guidBytes, 0, 16);
            guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x70); // version 7
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant
            Guid strategyId = new Guid(guidBytes);

            string slug = "drm-" + Regex.Replace(baseName.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
            if (slug.Length > 100) slug = slug[..100];

            string symbol = manifest.SupportedSymbols.Count > 0 ? manifest.SupportedSymbols[0] : "XAUUSDm";
            string timeframe = manifest.SupportedTimeframes.Count > 0 ? manifest.SupportedTimeframes[0] : "M1";
            string licenseType = manifest.License?.Claims.LicenseType.ToString() ?? "Lifetime";

            var execContext = new TenantExecutionContext(tenantId, actorId, Guid.NewGuid(), null);
            await using var tx = await database.BeginTenantTransactionAsync(execContext);

            // Upsert catalog.strategies with DRM protection flag and metadata
            await using (var upsertCmd = tx.CreateCommand(
                """
                insert into catalog.strategies
                (
                    id, tenant_id, slug, name, author_name, author_initials, category,
                    symbol, timeframe, version, description, summary,
                    rating_average, rating_count, active_users,
                    is_free, cloud_price_monthly_cents, cloud_price_yearly_cents, currency,
                    is_drm_protected, package_format_version, package_sha256, package_size_bytes, drm_license_type
                )
                values
                (
                    @id, @tenant_id, @slug, @name, @author_name, @author_initials, @category,
                    @symbol, @timeframe, @version, @description, @summary,
                    0, 0, 0,
                    true, 0, 0, 'USD',
                    true, 1, @package_sha256, @package_size_bytes, @drm_license_type
                )
                on conflict (id) do update set
                    is_drm_protected = true,
                    package_format_version = 1,
                    package_sha256 = @package_sha256,
                    package_size_bytes = @package_size_bytes,
                    drm_license_type = @drm_license_type,
                    updated_at = clock_timestamp();
                """))
            {
                upsertCmd.Parameters.AddWithValue("id", strategyId);
                upsertCmd.Parameters.AddWithValue("tenant_id", tenantId);
                upsertCmd.Parameters.AddWithValue("slug", slug);
                upsertCmd.Parameters.AddWithValue("name", manifest.Name);
                upsertCmd.Parameters.AddWithValue("author_name", manifest.Author);
                upsertCmd.Parameters.AddWithValue("author_initials", "YO");
                upsertCmd.Parameters.AddWithValue("category", "Proprietary Algorithm");
                upsertCmd.Parameters.AddWithValue("symbol", symbol);
                upsertCmd.Parameters.AddWithValue("timeframe", timeframe);
                upsertCmd.Parameters.AddWithValue("version", manifest.Version);
                upsertCmd.Parameters.AddWithValue("description", manifest.Description);
                upsertCmd.Parameters.AddWithValue("summary", $"Protected algorithmic trading strategy ({manifest.Parameters.Count} parameters).");
                upsertCmd.Parameters.AddWithValue("package_sha256", sha256);
                upsertCmd.Parameters.AddWithValue("package_size_bytes", (long)packageBytes.Length);
                upsertCmd.Parameters.AddWithValue("drm_license_type", licenseType);

                await upsertCmd.ExecuteNonQueryAsync();
            }

            // Ingest strategy parameters into catalog.strategy_inputs
            for (int i = 0; i < manifest.Parameters.Count; i++)
            {
                var p = manifest.Parameters[i];
                string declaredType = string.IsNullOrWhiteSpace(p.Type) ? "int" : p.Type;
                string valueKind = declaredType.ToLowerInvariant() switch
                {
                    "double" or "float" => "REAL",
                    "bool" => "LOGICAL",
                    "string" => "TEXT",
                    "color" => "COLOUR",
                    "datetime" => "MOMENT",
                    _ => "WHOLE"
                };

                await using var inputCmd = tx.CreateCommand(
                    """
                    insert into catalog.strategy_inputs
                    (
                        id, tenant_id, strategy_id, ordinal, name, label, group_label, declared_type, value_kind, default_value, source_line
                    )
                    values
                    (
                        gen_random_uuid(), @tenant_id, @strategy_id, @ordinal, @name, @label, 'General', @declared_type, @value_kind, @default_value, 1
                    )
                    on conflict (tenant_id, strategy_id, name) do update set
                        ordinal = @ordinal,
                        label = @label,
                        declared_type = @declared_type,
                        value_kind = @value_kind,
                        default_value = @default_value;
                    """);
                inputCmd.Parameters.AddWithValue("tenant_id", tenantId);
                inputCmd.Parameters.AddWithValue("strategy_id", strategyId);
                inputCmd.Parameters.AddWithValue("ordinal", i);
                inputCmd.Parameters.AddWithValue("name", p.Name);
                inputCmd.Parameters.AddWithValue("label", string.IsNullOrWhiteSpace(p.Comment) ? p.Name : p.Comment);
                inputCmd.Parameters.AddWithValue("declared_type", declaredType);
                inputCmd.Parameters.AddWithValue("value_kind", valueKind);
                inputCmd.Parameters.AddWithValue("default_value", p.DefaultValue ?? "0");
                await inputCmd.ExecuteNonQueryAsync();
            }

            // Sync license record if present
            if (manifest.License != null)
            {
                long[] boundLogins = manifest.License.Claims.BoundAccounts != null
                    ? Array.ConvertAll(manifest.License.Claims.BoundAccounts.ToArray(), x => (long)x)
                    : [];
                string[] boundServers = manifest.License.Claims.BoundServers != null
                    ? manifest.License.Claims.BoundServers.ToArray()
                    : [];

                await using var licCmd = tx.CreateCommand(
                    """
                    insert into catalog.strategy_licenses
                    (
                        id, tenant_id, strategy_id, user_id, license_type,
                        bound_account_logins, bound_broker_servers, signature_token,
                        issued_at, expires_at, is_revoked
                    )
                    values
                    (
                        @id, @tenant_id, @strategy_id, @user_id, @license_type,
                        @bound_logins, @bound_servers, @signature_token,
                        @issued_at, @expires_at, false
                    )
                    on conflict (id) do update set
                        license_type = @license_type,
                        bound_account_logins = @bound_logins,
                        bound_broker_servers = @bound_servers,
                        signature_token = @signature_token,
                        expires_at = @expires_at,
                        updated_at = clock_timestamp();
                    """);

                licCmd.Parameters.AddWithValue("id", manifest.License.Claims.LicenseId);
                licCmd.Parameters.AddWithValue("tenant_id", tenantId);
                licCmd.Parameters.AddWithValue("strategy_id", strategyId);
                licCmd.Parameters.AddWithValue("user_id", DBNull.Value);
                licCmd.Parameters.AddWithValue("license_type", manifest.License.Claims.LicenseType.ToString());
                licCmd.Parameters.AddWithValue("bound_logins", boundLogins);
                licCmd.Parameters.AddWithValue("bound_servers", boundServers);
                licCmd.Parameters.AddWithValue("signature_token", manifest.License.SignatureBase64);
                licCmd.Parameters.AddWithValue("issued_at", manifest.License.Claims.IssuedAtUtc);
                licCmd.Parameters.AddWithValue("expires_at", (object?)manifest.License.Claims.ExpiresAtUtc ?? DBNull.Value);

                await licCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            Console.WriteLine($"[DRM INGESTED] {manifest.Name} (ID: {strategyId}) -> License: {licenseType} | {manifest.Parameters.Count} inputs | {packageBytes.Length:N0} bytes");
        }

        // 2. Also process standard .mq5 files
        var mq5Files = Directory.GetFiles(mq5Dir, "*.mq5", SearchOption.AllDirectories);
        Console.WriteLine($"\nFound {mq5Files.Length} MQL5 strategy source files in {mq5Dir}.");

        foreach (var file in mq5Files)
        {
            string fileName = Path.GetFileName(file);
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            byte[] nameDigest = SHA256.HashData(Encoding.UTF8.GetBytes(baseName.ToLowerInvariant()));
            byte[] guidBytes = new byte[16];
            Array.Copy(nameDigest, 0, guidBytes, 0, 16);
            guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x70);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
            Guid strategyId = new Guid(guidBytes);

            string slug = "mq-" + Regex.Replace(baseName.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
            if (slug.Length > 100) slug = slug[..100];

            string symbol = fileName.Contains("Gold", StringComparison.OrdinalIgnoreCase) || fileName.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? "XAUUSDm" : "Unspecified";
            string timeframe = fileName.Contains("1m", StringComparison.OrdinalIgnoreCase) || fileName.Contains("M1", StringComparison.OrdinalIgnoreCase) ? "M1" : "M15";

            var execContext = new TenantExecutionContext(tenantId, actorId, Guid.NewGuid(), null);
            await using var tx = await database.BeginTenantTransactionAsync(execContext);

            await using (var checkCmd = tx.CreateCommand("select count(*) from catalog.strategies where id = @id"))
            {
                checkCmd.Parameters.AddWithValue("id", strategyId);
                long count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

                if (count == 0)
                {
                    await using var insertCmd = tx.CreateCommand(
                        """
                        insert into catalog.strategies
                        (
                            id, tenant_id, slug, name, author_name, author_initials, category,
                            symbol, timeframe, version, description, summary,
                            rating_average, rating_count, active_users,
                            is_free, cloud_price_monthly_cents, cloud_price_yearly_cents, currency
                        )
                        values
                        (
                            @id, @tenant_id, @slug, @name, 'YO4X Strategy Lab', 'YO', 'MQL5 Expert',
                            @symbol, @timeframe, '1.0.0', @description, @summary,
                            0, 0, 0,
                            true, 0, 0, 'USD'
                        );
                        """);
                    insertCmd.Parameters.AddWithValue("id", strategyId);
                    insertCmd.Parameters.AddWithValue("tenant_id", tenantId);
                    insertCmd.Parameters.AddWithValue("slug", slug);
                    insertCmd.Parameters.AddWithValue("name", baseName);
                    insertCmd.Parameters.AddWithValue("symbol", symbol);
                    insertCmd.Parameters.AddWithValue("timeframe", timeframe);
                    insertCmd.Parameters.AddWithValue("description", $"MQL5 Expert Advisor {fileName}");
                    insertCmd.Parameters.AddWithValue("summary", $"MQL5 Expert Advisor source file {fileName}");

                    await insertCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"[MQ5 INGESTED] {fileName} -> ID: {strategyId}");
                }
            }

            await tx.CommitAsync();
        }

        Console.WriteLine("\n[SYNC COMPLETE] All strategies and DRM licenses synchronized with PostgreSQL catalog.");
    }
}
