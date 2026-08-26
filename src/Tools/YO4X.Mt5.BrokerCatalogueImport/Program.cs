using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

// Offline importer for the MetaTrader 5 broker-server directory.
//
// The Control API is hermetic: it must never reach an external host while
// serving a request, because a vendor outage would then take out account
// linking. So the vendor directory is fetched here, frozen into a digest-named
// artifact, and only then loaded into the database the API reads. The two verbs
// are deliberately separate processes: `fetch` touches the network and no
// database, `import` touches the database and no network, and the SHA-256 the
// operator carries between them is what binds the two halves together.
//
// Nothing in this tool handles a broker login, password or any other credential.
//
//   fetch  --output <fully qualified path>
//   import --input  <fully qualified path> --sha256 <64 lowercase hex>
//          (connection string in YO4X_BROKER_CATALOGUE_CONNECTION)

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        return arguments.Length == 0
            ? Usage()
            : arguments[0] switch
            {
                "fetch" => await FetchAsync(arguments).ConfigureAwait(false),
                "import" => await ImportAsync(arguments).ConfigureAwait(false),
                _ => Usage()
            };
    }
    catch (ArgumentException)
    {
        return Usage();
    }
    catch (Exception exception) when (exception is
        IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or NotSupportedException or JsonException or
        HttpRequestException or NpgsqlException or CryptographicException)
    {
        Console.Error.WriteLine("broker_catalogue_failed_closed");
        return 3;
    }
}

static int Usage()
{
    Console.Error.WriteLine("broker_catalogue_usage_invalid");
    return 2;
}

// ---------------------------------------------------------------------------
// fetch
// ---------------------------------------------------------------------------
static async Task<int> FetchAsync(string[] arguments)
{
    if (!TryReadOptions(arguments, ["--output"], out Dictionary<string, string>? options)
        || options is null)
    {
        return Usage();
    }

    string outputPath = ValidateOutputPath(options["--output"]);
    List<string> terms = BuildSweepTerms();
    DateTimeOffset fetchedAt = DateTimeOffset.UtcNow;
    SortedDictionary<string, SortedDictionary<string, string[]>> directory = new(StringComparer.Ordinal);
    using var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        MaxConnectionsPerServer = 4,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(CatalogueSource.Authority, UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(60)
    };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("YO4X-BrokerCatalogueImport/1.0");

    // Four in flight matches MaxConnectionsPerServer: enough to finish the sweep
    // in a couple of minutes without behaving like a scraper against a service
    // this project does not own.
    int completed = 0;
    var merge = new Lock();
    await Parallel.ForEachAsync(
        terms,
        new ParallelOptions { MaxDegreeOfParallelism = 4 },
        async (term, _) =>
        {
            string? payload = await ReadDirectoryPageAsync(client, term).ConfigureAwait(false);
            lock (merge)
            {
                completed++;
                if (completed % 100 == 0)
                {
                    Console.Error.WriteLine(
                        Invariant($"broker_catalogue_fetch_progress {completed}/{terms.Count}"));
                }

                if (payload is not null)
                {
                    MergeDirectoryPage(directory, payload);
                }
            }
        }).ConfigureAwait(false);

    int serverCount = directory.Values.Sum(servers => servers.Count);
    if (directory.Count is 0 or > CatalogueSource.MaximumCompanyCount
        || serverCount is 0 or > CatalogueSource.MaximumServerCount)
    {
        throw new InvalidDataException("The fetched broker directory is empty or implausibly large.");
    }

    byte[] artifact = WriteArtifact(directory, fetchedAt);
    if (artifact.Length > CatalogueSource.MaximumArtifactByteCount)
    {
        throw new InvalidDataException("The fetched broker directory artifact is too large.");
    }

    WriteAtomic(outputPath, artifact);
    Console.WriteLine(Invariant($"broker_catalogue_fetch_sha256 {Sha256Hex(artifact)}"));
    Console.WriteLine(Invariant($"broker_catalogue_fetch_counts {directory.Count} {serverCount}"));
    return 0;
}

static List<string> BuildSweepTerms()
{
    // The vendor exposes no "list everything" route: /Search requires a
    // `company` substring and answers a single character with 404. Sweeping
    // every two-character alphanumeric substring therefore enumerates the whole
    // directory, because any company name of two or more alphanumeric
    // characters contains at least one of them.
    string alphabet = CatalogueSource.SweepAlphabet;
    var terms = new List<string>(alphabet.Length * alphabet.Length);
    foreach (char first in alphabet)
    {
        foreach (char second in alphabet)
        {
            terms.Add(string.Create(2, (First: first, Second: second), static (span, seed) =>
            {
                span[0] = seed.First;
                span[1] = seed.Second;
            }));
        }
    }

    return terms;
}

static async Task<string?> ReadDirectoryPageAsync(HttpClient client, string term)
{
    // `mt5=true` is not a filter over the default result set: it selects the
    // MetaTrader 5 directory, which is a strict superset of the unqualified
    // response and the only one carrying the MT5 access nodes.
    var route = new Uri($"/Search?company={Uri.EscapeDataString(term)}&mt5=true", UriKind.Relative);
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(route).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // The vendor answers an unmatched substring with 404. That is a
                // normal empty page, not a transport failure.
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (attempt == 2)
            {
                throw new HttpRequestException("The broker directory page could not be fetched.", exception);
            }
        }
    }

    throw new HttpRequestException("The broker directory page could not be fetched.");
}

static void MergeDirectoryPage(
    SortedDictionary<string, SortedDictionary<string, string[]>> directory,
    string payload)
{
    using JsonDocument document = JsonDocument.Parse(payload);
    if (document.RootElement.ValueKind != JsonValueKind.Object
        || !document.RootElement.TryGetProperty("result", out JsonElement companies)
        || companies.ValueKind != JsonValueKind.Array)
    {
        return;
    }

    foreach (JsonElement company in companies.EnumerateArray())
    {
        if (company.ValueKind != JsonValueKind.Object
            || !company.TryGetProperty("company", out JsonElement companyName)
            || companyName.ValueKind != JsonValueKind.String
            || !TryNormalizeName(companyName.GetString(), 300, out string normalizedCompany)
            || !company.TryGetProperty("results", out JsonElement servers)
            || servers.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        if (!directory.TryGetValue(normalizedCompany, out SortedDictionary<string, string[]>? knownServers))
        {
            knownServers = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            directory.Add(normalizedCompany, knownServers);
        }

        foreach (JsonElement server in servers.EnumerateArray())
        {
            if (server.ValueKind == JsonValueKind.Object
                && server.TryGetProperty("name", out JsonElement serverName)
                && serverName.ValueKind == JsonValueKind.String
                && TryNormalizeName(serverName.GetString(), 500, out string normalizedServer))
            {
                knownServers[normalizedServer] = ReadAccessEndpoints(server, "access");
            }
        }
    }
}

static string[] ReadAccessEndpoints(JsonElement server, string propertyName)
{
    if (!server.TryGetProperty(propertyName, out JsonElement access)
        || access.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var endpoints = new SortedSet<string>(StringComparer.Ordinal);
    foreach (JsonElement endpoint in access.EnumerateArray())
    {
        if (endpoint.ValueKind != JsonValueKind.String)
        {
            continue;
        }

        string? value = endpoint.GetString()?.Trim();
        // Host:port reference data only. Anything that is not a plain host and
        // port is dropped rather than stored, so a hostile directory entry can
        // never become a URL, a path or a shell fragment downstream.
        if (value is null
            || value.Length is < 3 or > 255
            || value.Count(character => character == ':') != 1
            || !value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or ':' or '_'))
        {
            continue;
        }

        endpoints.Add(value);
        if (endpoints.Count == CatalogueSource.MaximumAccessEndpointCount)
        {
            break;
        }
    }

    return [.. endpoints];
}

static bool TryNormalizeName(string? value, int maximumLength, out string normalized)
{
    normalized = value?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
    if (normalized.Length < 1 || normalized.Length > maximumLength || normalized.Any(char.IsControl))
    {
        normalized = string.Empty;
        return false;
    }

    return true;
}

static byte[] WriteArtifact(
    SortedDictionary<string, SortedDictionary<string, string[]>> directory,
    DateTimeOffset fetchedAt)
{
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", CatalogueSource.SchemaVersion);
        writer.WriteString("sourceUrl", CatalogueSource.Url);
        writer.WriteString("fetchedAt", fetchedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteStartArray("companies");
        foreach ((string company, SortedDictionary<string, string[]> servers) in directory)
        {
            writer.WriteStartObject();
            writer.WriteString("company", company);
            writer.WriteStartArray("servers");
            foreach ((string server, string[] endpoints) in servers)
            {
                writer.WriteStartObject();
                writer.WriteString("name", server);
                writer.WriteStartArray("access");
                foreach (string endpoint in endpoints)
                {
                    writer.WriteStringValue(endpoint);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    return buffer.ToArray();
}

// ---------------------------------------------------------------------------
// import
// ---------------------------------------------------------------------------
static async Task<int> ImportAsync(string[] arguments)
{
    if (!TryReadOptions(arguments, ["--input", "--sha256"], out Dictionary<string, string>? options)
        || options is null)
    {
        return Usage();
    }

    string expectedSha256 = options["--sha256"].Trim().ToLowerInvariant();
    if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
    {
        return Usage();
    }

    string inputPath = ValidateExistingFixedLocalFile(options["--input"]);
    byte[] artifact = await File.ReadAllBytesAsync(inputPath).ConfigureAwait(false);
    if (artifact.Length > CatalogueSource.MaximumArtifactByteCount)
    {
        throw new InvalidDataException("The broker directory artifact is too large.");
    }

    // The digest is checked before the artifact is parsed, so an artifact that
    // was edited between fetch and import never reaches the database.
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sha256Hex(artifact)),
            Encoding.ASCII.GetBytes(expectedSha256)))
    {
        Console.Error.WriteLine("broker_catalogue_artifact_digest_mismatch");
        return 4;
    }

    (DateTimeOffset fetchedAt, List<(string Company, string Server, string[] Access)> servers) =
        ReadArtifact(artifact);
    string connectionString = Environment.GetEnvironmentVariable(CatalogueSource.ConnectionVariable)
        ?? throw new InvalidOperationException($"{CatalogueSource.ConnectionVariable} is required.");
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Pooling = false,
        IncludeErrorDetail = false,
        LogParameters = false
    };
    if (string.IsNullOrWhiteSpace(builder.Database) || string.IsNullOrWhiteSpace(builder.Username))
    {
        throw new InvalidOperationException("The broker directory import connection is incomplete.");
    }

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync().ConfigureAwait(false);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

    Guid snapshotId = Guid.CreateVersion7();
    int companyCount = servers.Select(server => server.Company).Distinct(StringComparer.Ordinal).Count();
    await using (var snapshot = new NpgsqlCommand(
        """
        insert into brokerdirectory.catalogue_snapshots
            (id, source_url, snapshot_sha256, fetched_at, company_count, server_count)
        values
            (@id, @source_url, @snapshot_sha256, @fetched_at, @company_count, @server_count)
        """,
        connection,
        transaction))
    {
        snapshot.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, snapshotId);
        snapshot.Parameters.AddWithValue("source_url", NpgsqlDbType.Text, CatalogueSource.Url);
        snapshot.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, expectedSha256);
        snapshot.Parameters.AddWithValue("fetched_at", NpgsqlDbType.TimestampTz, fetchedAt);
        snapshot.Parameters.AddWithValue("company_count", NpgsqlDbType.Integer, companyCount);
        snapshot.Parameters.AddWithValue("server_count", NpgsqlDbType.Integer, servers.Count);
        await snapshot.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // A re-import keeps the existing row identity for a server that is still
    // listed: brokerdirectory.tenant_demo_approvals references it, so a churned
    // id would silently drop a tenant's approval. A server that disappears from
    // the vendor directory is deliberately left in place rather than deleted,
    // for the same reason.
    int inserted = 0;
    int refreshed = 0;
    await using (var upsert = new NpgsqlCommand(
        """
        insert into brokerdirectory.servers
            (id, snapshot_id, broker_company, server_name, access_endpoints)
        values
            (@id, @snapshot_id, @broker_company, @server_name, @access_endpoints)
        on conflict (broker_company, server_name) do update
            set snapshot_id = excluded.snapshot_id,
                access_endpoints = excluded.access_endpoints
        returning (xmax = 0)
        """,
        connection,
        transaction))
    {
        NpgsqlParameter id = upsert.Parameters.Add("id", NpgsqlDbType.Uuid);
        upsert.Parameters.AddWithValue("snapshot_id", NpgsqlDbType.Uuid, snapshotId);
        NpgsqlParameter company = upsert.Parameters.Add("broker_company", NpgsqlDbType.Text);
        NpgsqlParameter server = upsert.Parameters.Add("server_name", NpgsqlDbType.Text);
        NpgsqlParameter access = upsert.Parameters.Add("access_endpoints", NpgsqlDbType.Array | NpgsqlDbType.Text);
        await upsert.PrepareAsync().ConfigureAwait(false);
        foreach ((string Company, string Server, string[] Access) entry in servers)
        {
            id.Value = Guid.CreateVersion7();
            company.Value = entry.Company;
            server.Value = entry.Server;
            access.Value = entry.Access;
            if (await upsert.ExecuteScalarAsync().ConfigureAwait(false) is true)
            {
                inserted++;
            }
            else
            {
                refreshed++;
            }
        }
    }

    await transaction.CommitAsync().ConfigureAwait(false);
    Console.WriteLine(Invariant($"broker_catalogue_import_snapshot {snapshotId:D}"));
    Console.WriteLine(Invariant($"broker_catalogue_import_counts {inserted} {refreshed}"));
    return 0;
}

static (DateTimeOffset FetchedAt, List<(string Company, string Server, string[] Access)> Servers)
    ReadArtifact(byte[] artifact)
{
    using JsonDocument document = JsonDocument.Parse(artifact);
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object
        || !root.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
        || schemaVersion.ValueKind != JsonValueKind.String
        || !string.Equals(schemaVersion.GetString(), CatalogueSource.SchemaVersion, StringComparison.Ordinal)
        || !root.TryGetProperty("sourceUrl", out JsonElement sourceUrl)
        || sourceUrl.ValueKind != JsonValueKind.String
        || !string.Equals(sourceUrl.GetString(), CatalogueSource.Url, StringComparison.Ordinal)
        || !root.TryGetProperty("fetchedAt", out JsonElement fetchedAtElement)
        || fetchedAtElement.ValueKind != JsonValueKind.String
        || !DateTimeOffset.TryParse(
            fetchedAtElement.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset fetchedAt)
        || fetchedAt > DateTimeOffset.UtcNow
        || !root.TryGetProperty("companies", out JsonElement companies)
        || companies.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidDataException("The broker directory artifact is not the expected shape.");
    }

    var servers = new List<(string Company, string Server, string[] Access)>();
    foreach (JsonElement company in companies.EnumerateArray())
    {
        if (company.ValueKind != JsonValueKind.Object
            || !company.TryGetProperty("company", out JsonElement companyName)
            || companyName.ValueKind != JsonValueKind.String
            || !TryNormalizeName(companyName.GetString(), 300, out string normalizedCompany)
            || !company.TryGetProperty("servers", out JsonElement companyServers)
            || companyServers.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The broker directory artifact has an invalid company entry.");
        }

        foreach (JsonElement server in companyServers.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object
                || !server.TryGetProperty("name", out JsonElement serverName)
                || serverName.ValueKind != JsonValueKind.String
                || !TryNormalizeName(serverName.GetString(), 500, out string normalizedServer))
            {
                throw new InvalidDataException("The broker directory artifact has an invalid server entry.");
            }

            servers.Add((normalizedCompany, normalizedServer, ReadAccessEndpoints(server, "access")));
        }
    }

    if (servers.Count is 0 or > CatalogueSource.MaximumServerCount)
    {
        throw new InvalidDataException("The broker directory artifact is empty or implausibly large.");
    }

    return (fetchedAt, servers);
}

// ---------------------------------------------------------------------------
// Argument and path handling, matching YO4X.Mt5.EndpointDiscovery.
// ---------------------------------------------------------------------------
static bool TryReadOptions(
    string[] arguments,
    string[] required,
    out Dictionary<string, string>? options)
{
    options = null;
    var read = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 1; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length
            || !required.Contains(arguments[index], StringComparer.Ordinal)
            || !read.TryAdd(arguments[index], arguments[index + 1]))
        {
            return false;
        }
    }

    if (read.Count != required.Length)
    {
        return false;
    }

    options = read;
    return true;
}

static string ValidateExistingFixedLocalFile(string path)
{
    string fullPath = RequireAbsoluteLocalPath(path);
    var info = new FileInfo(fullPath);
    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    return info.FullName;
}

static string ValidateOutputPath(string path)
{
    string fullPath = RequireAbsoluteLocalPath(path);
    string directoryPath = Path.GetDirectoryName(fullPath) ?? throw new IOException();
    var directory = new DirectoryInfo(directoryPath);
    if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    if (File.Exists(fullPath) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    return fullPath;
}

static string RequireAbsoluteLocalPath(string path)
{
    if (string.IsNullOrWhiteSpace(path)
        || !Path.IsPathFullyQualified(path)
        || path.StartsWith("\\\\", StringComparison.Ordinal))
    {
        throw new ArgumentException("A fully qualified fixed-local path is required.", nameof(path));
    }

    string fullPath = Path.GetFullPath(path);
    string root = Path.GetPathRoot(fullPath)
        ?? throw new ArgumentException("The path must have a local drive root.", nameof(path));
    if (new DriveInfo(root).DriveType != DriveType.Fixed)
    {
        throw new IOException();
    }

    return fullPath;
}

static void WriteAtomic(string outputPath, byte[] content)
{
    string outputDirectory = Path.GetDirectoryName(outputPath)!;
    string temporaryPath = Path.Combine(
        outputDirectory,
        $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
    try
    {
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, outputPath, overwrite: true);
        temporaryPath = string.Empty;
    }
    finally
    {
        if (temporaryPath.Length != 0 && File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}

static string Sha256Hex(byte[] content) =>
    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

internal static class CatalogueSource
{
    internal const string Authority = "https://search.mtapi.io";

    internal const string Url = Authority + "/Search";

    internal const string SchemaVersion = "yo4x.mt5.broker-server-directory.v1";

    internal const string ConnectionVariable = "YO4X_BROKER_CATALOGUE_CONNECTION";

    internal const string SweepAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    internal const int MaximumCompanyCount = 100_000;

    internal const int MaximumServerCount = 500_000;

    internal const int MaximumAccessEndpointCount = 64;

    internal const int MaximumArtifactByteCount = 64 * 1024 * 1024;
}
