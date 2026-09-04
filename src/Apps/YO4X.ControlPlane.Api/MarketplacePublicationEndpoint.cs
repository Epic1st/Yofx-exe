using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance.Packaging;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Api;

internal sealed record MarketplacePublicationRequest(
    Guid UploadId,
    string SourceSha256,
    string PackageSha256,
    string PackageBase64,
    string Name,
    string Version,
    string Symbol,
    string Timeframe,
    string Category,
    string Summary,
    long MonthlyCents,
    long YearlyCents,
    string Currency);

internal sealed record MarketplacePublicationOptions(
    string SharedSecretFile,
    string PackageKeyDocumentFile,
    string ArtifactRoot,
    Guid TenantId,
    Guid ActorId)
{
    internal static MarketplacePublicationOptions? Load(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("MarketplacePublication");
        string? secret = section["SharedSecretFile"]?.Trim();
        string? keys = section["PackageKeyDocumentFile"]?.Trim();
        string? root = section["ArtifactRoot"]?.Trim();
        return string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(keys)
            || string.IsNullOrWhiteSpace(root)
            || !Guid.TryParse(section["TenantId"], out Guid tenant)
            || !Guid.TryParse(section["ActorId"], out Guid actor)
            ? null
            : new MarketplacePublicationOptions(
                Path.GetFullPath(secret),
                Path.GetFullPath(keys),
                Path.GetFullPath(root),
                tenant,
                actor);
    }
}

internal static class MarketplacePublicationEndpoint
{
    private const int MaximumPackageBytes = 64 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SeenNonces = new(StringComparer.Ordinal);

    internal static void MapMarketplacePublicationEndpoint(this WebApplication app)
    {
        app.MapPost("/internal/v1/marketplace/publications", PublishAsync).AllowAnonymous();
    }

    private static async Task<IResult> PublishAsync(
        MarketplacePublicationRequest request,
        HttpContext http,
        IConfiguration configuration,
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        MarketplacePublicationOptions? options = MarketplacePublicationOptions.Load(configuration);
        if (options is null)
            return Results.Problem(statusCode: 503, title: "Marketplace publication is not configured.");
        if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None))
            return Results.NotFound();

        if (!Authenticate(request, http.Request.Headers, options.SharedSecretFile))
            return Results.Unauthorized();

        byte[] package;
        try
        {
            package = Convert.FromBase64String(request.PackageBase64);
        }
        catch (FormatException)
        {
            return Results.Problem(statusCode: 400, title: "The package encoding is invalid.");
        }

        try
        {
            if (package.Length is < 1 or > MaximumPackageBytes)
                return Results.Problem(statusCode: 400, title: "The package size is invalid.");
            string packageSha = Convert.ToHexStringLower(SHA256.HashData(package));
            if (!FixedTimeHexEquals(packageSha, request.PackageSha256))
                return Results.Problem(statusCode: 400, title: "The package digest is invalid.");

            Yo4xStrategyManifest manifest = Yo4xStrategyPackage.ReadManifest(package);
            StrategyPublicationClaims claims = StrategyPublicationAuthority.Validate(
                manifest.Publication
                    ?? throw new CryptographicException("The package has no publication signature."),
                ReadPublicationPublicKey(options.PackageKeyDocumentFile));
            if (!string.Equals(claims.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                || !string.Equals(claims.StrategyName, manifest.Name, StringComparison.Ordinal)
                || !string.Equals(claims.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                || !FixedTimeHexEquals(claims.AssemblySha256, manifest.AssemblySha256 ?? string.Empty)
                || !string.Equals(request.Version, manifest.Version, StringComparison.Ordinal)
                || !manifest.SupportedSymbols.Contains(request.Symbol, StringComparer.OrdinalIgnoreCase)
                || !manifest.SupportedTimeframes.Contains(request.Timeframe, StringComparer.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: 400, title: "The signed publication metadata is inconsistent.");
            }

            Directory.CreateDirectory(options.ArtifactRoot);
            string artifactPath = Path.Combine(options.ArtifactRoot, packageSha + ".yo4x");
            if (!File.Exists(artifactPath))
            {
                string staging = artifactPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(staging, package, cancellationToken);
                    File.Move(staging, artifactPath);
                }
                finally
                {
                    if (File.Exists(staging)) File.Delete(staging);
                }
            }

            Guid strategyId = DeterministicGuid("yo4x-publication:" + manifest.StrategyId);
            var context = new TenantExecutionContext(options.TenantId, options.ActorId, Guid.CreateVersion7(), null);
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(context, cancellationToken)
                .ConfigureAwait(false);
            await UpsertStrategyAsync(
                transaction,
                strategyId,
                request,
                manifest,
                packageSha,
                package.LongLength,
                cancellationToken);
            await ReplaceInputsAsync(transaction, strategyId, manifest, cancellationToken);
            await RebindPackagedBotsAsync(
                transaction,
                strategyId,
                manifest.Name + ".yo4x",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new
            {
                strategyId,
                packageName = manifest.Name + ".yo4x",
                packageSha256 = packageSha,
                status = "PUBLISHED"
            });
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidDataException or JsonException)
        {
            return Results.Problem(statusCode: 400, title: "The strategy publication could not be verified.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(package);
        }
    }

    private static bool Authenticate(
        MarketplacePublicationRequest request,
        IHeaderDictionary headers,
        string secretPath)
    {
        string timestampText = headers["X-YO4X-Publication-Timestamp"].ToString();
        string nonce = headers["X-YO4X-Publication-Nonce"].ToString();
        string supplied = headers["X-YO4X-Publication-Signature"].ToString();
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || nonce.Length is < 32 or > 128
            || nonce.Any(character => !char.IsAsciiLetterOrDigit(character))
            || supplied.Length != 64
            || !File.Exists(secretPath))
            return false;

        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if ((now - timestamp).Duration() > TimeSpan.FromMinutes(2))
            return false;
        foreach ((string key, DateTimeOffset value) in SeenNonces)
            if (now - value > TimeSpan.FromMinutes(3)) SeenNonces.TryRemove(key, out _);
        if (!SeenNonces.TryAdd(nonce, now))
            return false;

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(File.ReadAllText(secretPath).Trim());
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            return false;
        }
        if (secret.Length != 32)
        {
            CryptographicOperations.ZeroMemory(secret);
            return false;
        }

        byte[] canonical = Canonical(request, timestampText, nonce);
        try
        {
            using var hmac = new HMACSHA256(secret);
            string expected = Convert.ToHexStringLower(hmac.ComputeHash(canonical));
            return FixedTimeHexEquals(expected, supplied);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    internal static byte[] Canonical(
        MarketplacePublicationRequest request,
        string timestamp,
        string nonce)
    {
        string[] fields =
        [
            timestamp, nonce, request.UploadId.ToString("D"), request.SourceSha256,
            request.PackageSha256, request.Name, request.Version, request.Symbol,
            request.Timeframe, request.Category, request.Summary,
            request.MonthlyCents.ToString(CultureInfo.InvariantCulture),
            request.YearlyCents.ToString(CultureInfo.InvariantCulture), request.Currency
        ];
        var builder = new StringBuilder();
        foreach (string field in fields)
            builder.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string ReadPublicationPublicKey(string keyDocumentPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(keyDocumentPath));
        return document.RootElement.GetProperty("PublicKeyPem").GetString()
            ?? throw new InvalidDataException("The publication key document has no public key.");
    }

    private static async Task UpsertStrategyAsync(
        TenantPostgresTransaction transaction,
        Guid strategyId,
        MarketplacePublicationRequest request,
        Yo4xStrategyManifest manifest,
        string packageSha,
        long packageBytes,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into catalog.strategies
            (
                id, tenant_id, slug, name, author_name, author_initials, category,
                symbol, timeframe, version, description, summary,
                is_free, cloud_price_monthly_cents, cloud_price_yearly_cents, currency,
                is_drm_protected, package_format_version, package_sha256,
                package_size_bytes, drm_license_type, package_strategy_id,
                package_entry_type, assembly_sha256
            )
            values
            (
                @id, @tenant, @slug, @name, @author, @initials, @category,
                @symbol, @timeframe, @version, @description, @summary,
                @is_free, @monthly, @yearly, @currency,
                true, 2, @package_sha, @package_bytes, 'Community',
                @package_strategy_id, @entry_type, @assembly_sha
            )
            on conflict (id) do update set
                slug = excluded.slug, name = excluded.name, author_name = excluded.author_name,
                author_initials = excluded.author_initials, category = excluded.category,
                symbol = excluded.symbol, timeframe = excluded.timeframe, version = excluded.version,
                description = excluded.description, summary = excluded.summary,
                is_free = excluded.is_free,
                cloud_price_monthly_cents = excluded.cloud_price_monthly_cents,
                cloud_price_yearly_cents = excluded.cloud_price_yearly_cents,
                currency = excluded.currency, is_drm_protected = true,
                package_format_version = 2, package_sha256 = excluded.package_sha256,
                package_size_bytes = excluded.package_size_bytes,
                package_strategy_id = excluded.package_strategy_id,
                package_entry_type = excluded.package_entry_type,
                assembly_sha256 = excluded.assembly_sha256,
                updated_at = clock_timestamp()
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, strategyId);
        command.Parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("slug", NpgsqlDbType.Text, Slug(manifest.Name, manifest.StrategyId));
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, Trim(manifest.Name + ".yo4x", 200));
        command.Parameters.AddWithValue("author", NpgsqlDbType.Text, Trim(manifest.Author, 200));
        command.Parameters.AddWithValue("initials", NpgsqlDbType.Text, "YO");
        command.Parameters.AddWithValue("category", NpgsqlDbType.Text, Trim(request.Category, 100));
        command.Parameters.AddWithValue("symbol", NpgsqlDbType.Text, Trim(request.Symbol, 50));
        command.Parameters.AddWithValue("timeframe", NpgsqlDbType.Text, Trim(request.Timeframe, 50));
        command.Parameters.AddWithValue("version", NpgsqlDbType.Text, Trim(manifest.Version, 50));
        command.Parameters.AddWithValue("description", NpgsqlDbType.Text, Trim(manifest.Description, 20_000));
        command.Parameters.AddWithValue("summary", NpgsqlDbType.Text, Trim(request.Summary, 4_000));
        command.Parameters.AddWithValue("is_free", NpgsqlDbType.Boolean, request.MonthlyCents == 0 && request.YearlyCents == 0);
        command.Parameters.AddWithValue("monthly", NpgsqlDbType.Integer, checked((int)request.MonthlyCents));
        command.Parameters.AddWithValue("yearly", NpgsqlDbType.Integer, checked((int)request.YearlyCents));
        command.Parameters.AddWithValue("currency", NpgsqlDbType.Char, request.Currency);
        command.Parameters.AddWithValue("package_sha", NpgsqlDbType.Text, packageSha);
        command.Parameters.AddWithValue("package_bytes", NpgsqlDbType.Bigint, packageBytes);
        command.Parameters.AddWithValue("package_strategy_id", NpgsqlDbType.Text, manifest.StrategyId);
        command.Parameters.AddWithValue("entry_type", NpgsqlDbType.Text, manifest.EntryTypeName!);
        command.Parameters.AddWithValue("assembly_sha", NpgsqlDbType.Text, manifest.AssemblySha256!);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceInputsAsync(
        TenantPostgresTransaction transaction,
        Guid strategyId,
        Yo4xStrategyManifest manifest,
        CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand delete = transaction.CreateCommand(
            "delete from catalog.strategy_inputs where tenant_id = @tenant and strategy_id = @strategy"))
        {
            delete.Parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, transaction.Context.TenantId);
            delete.Parameters.AddWithValue("strategy", NpgsqlDbType.Uuid, strategyId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        for (int index = 0; index < manifest.Parameters.Count; index++)
        {
            StrategyParameterInfo parameter = manifest.Parameters[index];
            await using NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into catalog.strategy_inputs
                    (id, tenant_id, strategy_id, ordinal, name, label, group_label,
                     declared_type, value_kind, default_value, source_line)
                values
                    (@id, @tenant, @strategy, @ordinal, @name, @label, 'General',
                     @type, @kind, @default, 1)
                """);
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            insert.Parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, transaction.Context.TenantId);
            insert.Parameters.AddWithValue("strategy", NpgsqlDbType.Uuid, strategyId);
            insert.Parameters.AddWithValue("ordinal", NpgsqlDbType.Integer, index);
            insert.Parameters.AddWithValue("name", NpgsqlDbType.Text, parameter.Name);
            insert.Parameters.AddWithValue("label", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(parameter.Comment) ? parameter.Name : parameter.Comment);
            insert.Parameters.AddWithValue("type", NpgsqlDbType.Text, parameter.Type);
            insert.Parameters.AddWithValue("kind", NpgsqlDbType.Text, ValueKind(parameter.Type));
            insert.Parameters.AddWithValue("default", NpgsqlDbType.Text, parameter.DefaultValue);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RebindPackagedBotsAsync(
        TenantPostgresTransaction transaction,
        Guid strategyId,
        string strategyName,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update bots.bots as bot
            set strategy_id = @strategy_id,
                name = @strategy_name,
                updated_at = clock_timestamp()
            from catalog.strategies as previous
            where bot.tenant_id = @tenant_id
              and previous.tenant_id = bot.tenant_id
              and previous.id = bot.strategy_id
              and previous.id <> @strategy_id
              and previous.is_drm_protected
              and previous.package_format_version >= 2
              and regexp_replace(lower(btrim(previous.name)), '\.(mq5|yo4x)$', '')
                  = regexp_replace(lower(btrim(@strategy_name)), '\.(mq5|yo4x)$', '')
              and bot.status in ('DRAFT', 'STOPPED', 'PAUSED', 'FAULTED')
            """);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyId);
        command.Parameters.AddWithValue("strategy_name", NpgsqlDbType.Text, Trim(strategyName, 200));
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ValueKind(string type) => type.ToLowerInvariant() switch
    {
        "double" or "float" => "REAL",
        "bool" => "LOGICAL",
        "string" => "TEXT",
        "color" => "COLOUR",
        "datetime" => "MOMENT",
        _ => "WHOLE"
    };

    private static Guid DeterministicGuid(string value)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = digest.AsSpan(0, 16);
        // Guid(byte[]) stores the third textual group little-endian. Byte 7, not byte 6,
        // owns the UUID version nibble. Retain the original byte-6 normalization for stable
        // IDs already published, and mark the identifier as a custom deterministic UUIDv8.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string Slug(string name, string strategyId)
    {
        string core = new(name.ToLowerInvariant().Select(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'
                ? character : '-').ToArray());
        core = core.Trim('-');
        if (core.Length == 0) core = "strategy";
        string slug = "yo4x-" + core + "-" + strategyId[..Math.Min(8, strategyId.Length)];
        return Trim(slug, 100);
    }

    private static string Trim(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private static bool FixedTimeHexEquals(string first, string second)
    {
        if (first.Length != 64 || second.Length != 64) return false;
        try
        {
            byte[] left = Convert.FromHexString(first);
            byte[] right = Convert.FromHexString(second);
            try { return CryptographicOperations.FixedTimeEquals(left, right); }
            finally
            {
                CryptographicOperations.ZeroMemory(left);
                CryptographicOperations.ZeroMemory(right);
            }
        }
        catch (FormatException) { return false; }
    }
}
