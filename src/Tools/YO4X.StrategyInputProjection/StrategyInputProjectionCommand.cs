using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YO4X.StrategyGovernance;

namespace YO4X.StrategyInputProjection;

internal sealed record StrategyInputProjectionOptions(
    string SourceRoot,
    string ManifestPath,
    string OutputPath,
    Guid TenantId);

/// <summary>
/// Projects the real MQL5 <c>input</c> parameters of the verified corpus into the
/// catalog, by running the front end over the same files the static manifest
/// records and emitting idempotent SQL for catalog.strategy_inputs and
/// catalog.strategy_enum_members.
///
/// The manifest is the authority for both the corpus digest and the set of
/// relative paths, because the strategy identifiers already in
/// catalog.strategies were derived from exactly those two things. Each file is
/// re-hashed and checked against the manifest before it is compiled, so a corpus
/// that has drifted is refused rather than silently projected against stale
/// identifiers.
/// </summary>
internal static class StrategyInputProjectionCommand
{
    private const string DefaultManifestPath = "docs/backend/mq5-static-manifest.v1.json";
    private const string DefaultTenantId = "019c8d27-763d-7000-8000-000000000001";
    private const int MaximumCorpusFileCount = 10_000;
    private const long MaximumFileBytes = 4L * 1024L * 1024L;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        StrategyInputProjectionOptions options;
        try
        {
            options = ParseOptions(arguments);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("Strategy input projection failed: " + exception.Message);
            WriteUsage();
            return 2;
        }

        try
        {
            return await ProjectAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException)
        {
            Console.Error.WriteLine("Strategy input projection failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> ProjectAsync(
        StrategyInputProjectionOptions options,
        CancellationToken cancellationToken)
    {
        (string corpusSha256, IReadOnlyList<ManifestFile> files) = await ReadManifestAsync(
            options.ManifestPath,
            cancellationToken).ConfigureAwait(false);

        var projections = new List<ProjectedFile>(files.Count);
        var drifted = new List<string>();
        int compiled = 0;
        int notLowered = 0;
        int skippedInputs = 0;
        int skippedEnums = 0;

        foreach (ManifestFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(
                options.SourceRoot,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(options.SourceRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A manifest path escapes the source root: " + file.RelativePath);
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                drifted.Add(file.RelativePath + " (absent)");
                continue;
            }

            if (info.Length > MaximumFileBytes)
            {
                drifted.Add(file.RelativePath + " (over the per-file size limit)");
                continue;
            }

            byte[] content = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                string sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                if (!string.Equals(sha256, file.Sha256, StringComparison.Ordinal))
                {
                    drifted.Add(file.RelativePath + " (digest differs from the manifest)");
                    continue;
                }

                var document = new Mql5SourceDocument(file.RelativePath, content);
                Mql5SourceSecretScanner.EnsureNoHighConfidenceSecrets(document);
                Mql5FrontEndResult result = Mql5FrontEnd.Compile(document);
                if (!result.Succeeded || result.Module is null)
                {
                    notLowered++;
                    continue;
                }

                compiled++;
                ProjectedFile projected = Mql5InputProjection.Project(
                    corpusSha256,
                    file.RelativePath,
                    result.Module);
                skippedInputs += projected.SkippedInputCount;
                skippedEnums += projected.SkippedEnumCount;
                if (projected.Inputs.Count > 0)
                {
                    projections.Add(projected);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        if (drifted.Count > 0)
        {
            Console.Error.WriteLine(
                "The source root does not match the manifest, so projected identifiers would be wrong:");
            foreach (string entry in drifted)
            {
                Console.Error.WriteLine("  " + entry);
            }

            return 3;
        }

        string sql = BuildSql(options, corpusSha256, projections);
        string? directory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
                options.OutputPath,
                sql,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);

        int inputCount = projections.Sum(projected => projected.Inputs.Count);
        int memberCount = projections.Sum(projected => projected.EnumMembers.Count);
        int labelled = projections
            .SelectMany(projected => projected.Inputs)
            .Count(input => input.Label is not null);
        int grouped = projections
            .SelectMany(projected => projected.Inputs)
            .Count(input => input.GroupLabel is not null);

        Console.WriteLine("Wrote " + options.OutputPath);
        Console.WriteLine(
            "  corpus "
            + corpusSha256
            + ", "
            + files.Count.ToString(CultureInfo.InvariantCulture)
            + " manifest files");
        Console.WriteLine(
            "  "
            + compiled.ToString(CultureInfo.InvariantCulture)
            + " lowered, "
            + notLowered.ToString(CultureInfo.InvariantCulture)
            + " not lowered by the front end");
        Console.WriteLine(
            "  "
            + inputCount.ToString(CultureInfo.InvariantCulture)
            + " inputs across "
            + projections.Count.ToString(CultureInfo.InvariantCulture)
            + " strategies, "
            + memberCount.ToString(CultureInfo.InvariantCulture)
            + " enumeration members");
        Console.WriteLine(
            "  "
            + labelled.ToString(CultureInfo.InvariantCulture)
            + " inputs carry a label, "
            + grouped.ToString(CultureInfo.InvariantCulture)
            + " carry a group heading");
        if (!Mql5InputProjection.LabelsAvailable)
        {
            Console.WriteLine(
                "  the front end does not expose Label or GroupLabel on Mql5IrInput, "
                + "so every projected label is null; this is a genuine gap, not a default");
        }

        Console.WriteLine(
            "  "
            + skippedInputs.ToString(CultureInfo.InvariantCulture)
            + " inputs skipped (no folded default, unclassifiable type, or not a plain name), "
            + skippedEnums.ToString(CultureInfo.InvariantCulture)
            + " enumerations skipped (a member value could not be folded)");
        Console.WriteLine("  apply with: psql -v ON_ERROR_STOP=1 -f " + options.OutputPath);
        return 0;
    }

    /// <summary>
    /// Builds one idempotent script. Existing rows for the tenant are removed and
    /// rewritten, and every insert is joined to catalog.strategies so a row can
    /// never be written for a strategy the catalog does not hold.
    /// </summary>
    private static string BuildSql(
        StrategyInputProjectionOptions options,
        string corpusSha256,
        IReadOnlyList<ProjectedFile> projections)
    {
        string tenant = Quote(options.TenantId.ToString("D", CultureInfo.InvariantCulture));
        var builder = new StringBuilder();
        builder.Append("-- Generated by YO4X.StrategyInputProjection from ")
            .Append(options.ManifestPath)
            .Append('\n');
        builder.Append("-- Corpus SHA-256 ").Append(corpusSha256).Append('\n');
        builder.Append(
            "-- Every row below is read out of the lowered MQL5 IR of the verified corpus.\n");
        builder.Append("-- Re-runnable: the tenant's existing rows are replaced, not appended to.\n\n");
        builder.Append("begin;\n\n");
        builder.Append("delete from catalog.strategy_enum_members where tenant_id = ")
            .Append(tenant)
            .Append("::uuid;\n");
        builder.Append("delete from catalog.strategy_inputs where tenant_id = ")
            .Append(tenant)
            .Append("::uuid;\n\n");

        List<ProjectedInput> inputs = projections.SelectMany(file => file.Inputs).ToList();
        if (inputs.Count > 0)
        {
            builder.Append(
                """
                insert into catalog.strategy_inputs
                    (id, tenant_id, strategy_id, ordinal, name, label, group_label,
                     declared_type, value_kind, default_value, enum_type_name, source_line)
                select
                    declared.id::uuid,
                    declared.tenant_id::uuid,
                    declared.strategy_id::uuid,
                    declared.ordinal::integer,
                    declared.name,
                    declared.label,
                    declared.group_label,
                    declared.declared_type,
                    declared.value_kind,
                    declared.default_value,
                    declared.enum_type_name,
                    declared.source_line::integer
                from (values

                """);
            AppendRows(
                builder,
                inputs,
                (row, input) => row
                    .Append(Quote(input.Id.ToString("D", CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(tenant)
                    .Append(", ")
                    .Append(Quote(input.StrategyId.ToString("D", CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(Quote(input.Ordinal.ToString(CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(Quote(input.Name))
                    .Append(", ")
                    .Append(QuoteOrNull(input.Label))
                    .Append(", ")
                    .Append(QuoteOrNull(input.GroupLabel))
                    .Append(", ")
                    .Append(Quote(input.DeclaredType))
                    .Append(", ")
                    .Append(Quote(input.ValueKind))
                    .Append(", ")
                    .Append(Quote(input.DefaultValue))
                    .Append(", ")
                    .Append(QuoteOrNull(input.EnumTypeName))
                    .Append(", ")
                    .Append(Quote(input.SourceLine.ToString(CultureInfo.InvariantCulture))));
            builder.Append(
                """

                ) as declared (id, tenant_id, strategy_id, ordinal, name, label, group_label,
                               declared_type, value_kind, default_value, enum_type_name, source_line)
                join catalog.strategies as strategy
                  on strategy.tenant_id = declared.tenant_id::uuid
                 and strategy.id = declared.strategy_id::uuid;


                """);
        }

        List<ProjectedEnumMember> members = projections
            .SelectMany(file => file.EnumMembers)
            .ToList();
        if (members.Count > 0)
        {
            builder.Append(
                """
                insert into catalog.strategy_enum_members
                    (id, tenant_id, strategy_id, enum_type_name, ordinal, member_name,
                     member_value, label)
                select
                    member.id::uuid,
                    member.tenant_id::uuid,
                    member.strategy_id::uuid,
                    member.enum_type_name,
                    member.ordinal::integer,
                    member.member_name,
                    member.member_value::bigint,
                    member.label
                from (values

                """);
            AppendRows(
                builder,
                members,
                (row, member) => row
                    .Append(Quote(member.Id.ToString("D", CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(tenant)
                    .Append(", ")
                    .Append(Quote(member.StrategyId.ToString("D", CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(Quote(member.EnumTypeName))
                    .Append(", ")
                    .Append(Quote(member.Ordinal.ToString(CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(Quote(member.MemberName))
                    .Append(", ")
                    .Append(Quote(member.MemberValue.ToString(CultureInfo.InvariantCulture)))
                    .Append(", ")
                    .Append(QuoteOrNull(member.Label)));
            builder.Append(
                """

                ) as member (id, tenant_id, strategy_id, enum_type_name, ordinal, member_name,
                             member_value, label)
                join catalog.strategies as strategy
                  on strategy.tenant_id = member.tenant_id::uuid
                 and strategy.id = member.strategy_id::uuid;


                """);
        }

        builder.Append("commit;\n\n");
        builder.Append(
            """
            select
                (select pg_catalog.count(*) from catalog.strategy_inputs) as input_rows,
                (select pg_catalog.count(*) from catalog.strategy_enum_members) as enum_member_rows;

            """);
        return builder.ToString();
    }

    private static void AppendRows<T>(
        StringBuilder builder,
        IReadOnlyList<T> rows,
        Action<StringBuilder, T> append)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            builder.Append("    (");
            append(builder, rows[index]);
            builder.Append(')');
            if (index + 1 < rows.Count)
            {
                builder.Append(",\n");
            }
        }
    }

    private static async Task<(string CorpusSha256, IReadOnlyList<ManifestFile> Files)> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string corpusSha256 = root.TryGetProperty("corpusSha256", out JsonElement digest)
            ? digest.GetString() ?? string.Empty
            : string.Empty;
        if (!IsSha256(corpusSha256))
        {
            throw new InvalidDataException("The manifest does not carry a corpus SHA-256.");
        }

        if (!root.TryGetProperty("files", out JsonElement files)
            || files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The manifest carries no file list.");
        }

        var entries = new List<ManifestFile>();
        foreach (JsonElement file in files.EnumerateArray())
        {
            if (entries.Count == MaximumCorpusFileCount)
            {
                throw new InvalidDataException("The manifest exceeds the file-count limit.");
            }

            string relativePath = file.TryGetProperty("relativePath", out JsonElement path)
                ? path.GetString() ?? string.Empty
                : string.Empty;
            string sha256 = file.TryGetProperty("sha256", out JsonElement fileDigest)
                ? fileDigest.GetString() ?? string.Empty
                : string.Empty;
            if (relativePath.Length == 0
                || relativePath.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relativePath)
                || !IsSha256(sha256))
            {
                throw new InvalidDataException(
                    "The manifest carries a file entry that cannot be trusted.");
            }

            entries.Add(new ManifestFile(relativePath, sha256));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The manifest carries no file list.");
        }

        return (corpusSha256, entries);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(static character => char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');

    private static string Quote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string QuoteOrNull(string? value) =>
        value is null ? "null" : Quote(value);

    private static StrategyInputProjectionOptions ParseOptions(IReadOnlyList<string> arguments)
    {
        string sourceRoot = Path.GetFullPath(
            GetOption(arguments, "--source-root") ?? Path.Combine("Testing", "Mq5"));
        string manifestPath = Path.GetFullPath(
            GetOption(arguments, "--manifest") ?? DefaultManifestPath);
        string outputPath = Path.GetFullPath(
            GetOption(arguments, "--output")
            ?? Path.Combine(".local", "development", "strategy-input-projection.sql"));
        string tenant = GetOption(arguments, "--tenant-id") ?? DefaultTenantId;

        if (!Directory.Exists(sourceRoot))
        {
            throw new ArgumentException("The source root does not exist: " + sourceRoot);
        }

        if (!File.Exists(manifestPath))
        {
            throw new ArgumentException("The static manifest does not exist: " + manifestPath);
        }

        if (!Guid.TryParseExact(tenant, "D", out Guid tenantId) || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Option '--tenant-id' must be a canonical UUID.");
        }

        return new StrategyInputProjectionOptions(
            sourceRoot,
            manifestPath,
            outputPath,
            tenantId);
    }

    private static string? GetOption(IReadOnlyList<string> arguments, string option)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (!arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new ArgumentException("Option '" + option + "' can be specified only once.");
            }

            index = candidate;
        }

        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= arguments.Count
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Option '" + option + "' has no value.");
        }

        return arguments[index + 1];
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            """
            usage: YO4X.StrategyInputProjection
                       [--source-root <directory>]   default Testing/Mq5
                       [--manifest <file>]           default docs/backend/mq5-static-manifest.v1.json
                       [--output <file>]             default .local/development/strategy-input-projection.sql
                       [--tenant-id <uuid>]          default 019c8d27-763d-7000-8000-000000000001

            Runs the MQL5 front end over the corpus the manifest records and writes
            idempotent SQL for catalog.strategy_inputs and catalog.strategy_enum_members.
            Apply it with: psql -v ON_ERROR_STOP=1 -f <output>
            """);
    }

    private sealed record ManifestFile(string RelativePath, string Sha256);
}
