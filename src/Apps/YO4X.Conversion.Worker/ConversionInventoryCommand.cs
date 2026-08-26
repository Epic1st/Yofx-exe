using System.Security.Cryptography;
using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance;

namespace YO4X.Conversion.Worker;

public static class ConversionInventoryCommand
{
    private const string CommandSwitch = "--static-inventory";

    public static bool IsRequested(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(CommandSwitch, StringComparer.Ordinal);
    }

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            string sourceRoot = GetRequiredOption(arguments, "--source-root");
            string manifestOutput = GetRequiredOption(arguments, "--manifest-output");
            string reportOutput = GetRequiredOption(arguments, "--report-output");
            string? conversionEvidenceOutput = GetOptionalOption(
                arguments,
                "--conversion-evidence-output");
            string? conversionEvidenceReportOutput = GetOptionalOption(
                arguments,
                "--conversion-evidence-report-output");
            string? compilePackagePlanOutput = GetOptionalOption(
                arguments,
                "--compile-package-plan-output");
            string? restrictedIrOutput = GetOptionalOption(arguments, "--restricted-ir-output");
            if ((conversionEvidenceOutput is null) != (conversionEvidenceReportOutput is null))
            {
                throw new ArgumentException(
                    "Conversion evidence JSON and report outputs must be requested together.");
            }

            var requestedOutputs = new List<string> { manifestOutput, reportOutput };
            int? conversionEvidenceIndex = null;
            int? compilePackagePlanIndex = null;
            int? restrictedIrIndex = null;
            if (conversionEvidenceOutput is not null)
            {
                conversionEvidenceIndex = requestedOutputs.Count;
                requestedOutputs.Add(conversionEvidenceOutput);
                requestedOutputs.Add(conversionEvidenceReportOutput!);
            }

            if (compilePackagePlanOutput is not null)
            {
                compilePackagePlanIndex = requestedOutputs.Count;
                requestedOutputs.Add(compilePackagePlanOutput);
            }

            if (restrictedIrOutput is not null)
            {
                restrictedIrIndex = requestedOutputs.Count;
                requestedOutputs.Add(restrictedIrOutput);
            }

            Mql5ArtifactPathSet paths = Mql5ArtifactOutputGuard.Resolve(
                sourceRoot,
                requestedOutputs.ToArray());
            sourceRoot = paths.SourceRoot;
            manifestOutput = paths.OutputPaths[0];
            reportOutput = paths.OutputPaths[1];
            if (conversionEvidenceOutput is not null)
            {
                conversionEvidenceOutput = paths.OutputPaths[conversionEvidenceIndex!.Value];
                conversionEvidenceReportOutput = paths.OutputPaths[conversionEvidenceIndex.Value + 1];
            }

            if (compilePackagePlanOutput is not null)
            {
                compilePackagePlanOutput = paths.OutputPaths[compilePackagePlanIndex!.Value];
            }

            if (restrictedIrOutput is not null)
            {
                restrictedIrOutput = paths.OutputPaths[restrictedIrIndex!.Value];
            }

            var job = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
            using Mql5AnalyzedCorpus corpus = await job
                .AnalyzeDirectoryForPersistenceAsync(sourceRoot, cancellationToken)
                .ConfigureAwait(false);
            bool requiresConversionEvidence = conversionEvidenceOutput is not null
                || compilePackagePlanOutput is not null
                || restrictedIrOutput is not null;
            Mql5ConversionCorpusEvidence? conversionEvidence = !requiresConversionEvidence
                ? null
                : new Mql5ConversionEvidenceAnalyzer().Analyze(corpus.Documents);
            Mql5CompilePackagePlan? compilePackagePlan = compilePackagePlanOutput is null
                ? null
                : Mql5CompilePackageDossierPlanner.Plan(
                    corpus.Manifest,
                    conversionEvidence!,
                    corpus.Documents,
                    approvedPlatformLibrarySnapshot: null);
            Mql5RestrictedCorpusArtifact? restrictedArtifact = restrictedIrOutput is null
                ? null
                : Mql5RestrictedCorpusCompiler.Compile(
                    corpus.Manifest,
                    conversionEvidence!,
                    corpus.Documents);
            await Mql5CorpusInventoryJob.WriteArtifactsAsync(
                    corpus.Manifest,
                    sourceRoot,
                    manifestOutput,
                    reportOutput,
                    cancellationToken)
                .ConfigureAwait(false);
            if (conversionEvidence is not null)
            {
                if (conversionEvidenceOutput is not null)
                {
                    await Mql5CorpusInventoryJob.WriteConversionEvidenceArtifactsAsync(
                            conversionEvidence,
                            sourceRoot,
                            conversionEvidenceOutput,
                            conversionEvidenceReportOutput!,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (compilePackagePlan is not null)
                {
                    await Mql5CorpusInventoryJob.WriteCompilePackagePlanArtifactAsync(
                            compilePackagePlan,
                            sourceRoot,
                            compilePackagePlanOutput!,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (restrictedArtifact is not null)
                {
                    await Mql5CorpusInventoryJob.WriteRestrictedCorpusArtifactAsync(
                            restrictedArtifact,
                            sourceRoot,
                            restrictedIrOutput!,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (HasSwitch(arguments, "--persist-postgres"))
            {
                await PersistAsync(arguments, corpus, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine(
                $"Static inventory complete: {corpus.Manifest.FileCount} files, corpus {corpus.Manifest.CorpusSha256}.");
            if (conversionEvidence is not null)
            {
                Console.WriteLine(
                    $"Conversion evidence complete: {conversionEvidence.FileCount} files, evidence {conversionEvidence.EvidenceSha256}; no semantic conversion is claimed.");
            }


            if (compilePackagePlan is not null)
            {
                Console.WriteLine(
                    $"Compile-package planning complete: {compilePackagePlan.Targets.Count} targets, plan {compilePackagePlan.PlanSha256}; no compile or runtime proof is claimed.");
            }

            if (restrictedArtifact is not null)
            {
                Console.WriteLine(
                    $"Restricted compilation complete: {restrictedArtifact.AttemptedCount} attempted, {restrictedArtifact.LoweredCount} lowered, artifact {restrictedArtifact.ArtifactSha256}; no runtime proof is claimed.");
            }

            return 0;
        }
        catch (NpgsqlException)
        {
            Console.Error.WriteLine("Static inventory persistence failed without exposing database details.");
            return 3;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"Static inventory failed: {exception.Message}");
            return 2;
        }
    }

    private static async Task PersistAsync(
        IReadOnlyList<string> arguments,
        Mql5AnalyzedCorpus corpus,
        CancellationToken cancellationToken)
    {
        RejectSelfAssertedAuthorityOptions(arguments);
        string? configuredConnection = Environment.GetEnvironmentVariable(
            "YO4X_CONVERSION_POSTGRES_CONNECTION");
        bool allowInsecureDevelopment = HasSwitch(
            arguments,
            "--allow-insecure-development-postgres");
        string connectionString = ValidateConnectionString(
            configuredConnection,
            allowInsecureDevelopment);
        byte[] capability = ReadCapability();
        Mql5CorpusPersistenceRequest request;
        try
        {
            request = new Mql5CorpusPersistenceRequest(
                GetRequiredGuid(arguments, "--import-job-id"),
                capability);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }

        using (request)
        {
            await using var database = new PostgresDatabase(
                connectionString,
                PostgresDatabaseUsage.Runtime,
                allowInsecureLoopbackForDevelopment: allowInsecureDevelopment);
            var store = new PostgresMql5CorpusStore(database);
            Mql5CorpusPersistenceResult result = await store.PersistAsync(
                    request,
                    corpus,
                    cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(
                $"Static inventory database persistence {(result.Replayed ? "replayed" : "completed")}: import {result.ImportId:D}, manifest {result.ManifestSha256}.");
        }
    }

    private static byte[] ReadCapability()
    {
        const string environmentName = "YO4X_CONVERSION_IMPORT_CAPABILITY";
        string? capability = Environment.GetEnvironmentVariable(environmentName);
        Environment.SetEnvironmentVariable(environmentName, null);
        if (capability is not { Length: 43 }
            || capability.Any(character => character is not (>= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_')))
        {
            throw new ArgumentException(
                "YO4X_CONVERSION_IMPORT_CAPABILITY must contain one valid single-use capability.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(
                capability.Replace('-', '+').Replace('_', '/') + "=");
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "YO4X_CONVERSION_IMPORT_CAPABILITY is invalid.",
                exception);
        }

        if (decoded.Length != 32
            || decoded.All(static value => value == 0)
            || !string.Equals(ToBase64Url(decoded), capability, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new ArgumentException(
                "YO4X_CONVERSION_IMPORT_CAPABILITY is invalid.");
        }

        return decoded;
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void RejectSelfAssertedAuthorityOptions(IReadOnlyList<string> arguments)
    {
        string? forbidden = arguments.FirstOrDefault(argument => argument is
            "--tenant-id" or "--user-id" or "--source-label" or "--correlation-id" or "--capability");
        if (forbidden is not null)
        {
            throw new ArgumentException(
                $"Option '{forbidden}' is forbidden; import authority must come from the authenticated database job.");
        }
    }

    internal static string ValidateConnectionString(
        string? value,
        bool allowInsecureDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "YO4X_CONVERSION_POSTGRES_CONNECTION is required for database persistence.");
        }

        var builder = new NpgsqlConnectionStringBuilder(value);
        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || !string.Equals(builder.Username, "yo4x_conversion_worker", StringComparison.Ordinal)
            || !PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
            || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                builder,
                allowInsecureDevelopment))
        {
            throw new ArgumentException("The conversion PostgreSQL connection is not safely configured.");
        }

        return builder.ConnectionString;
    }

    private static Guid GetRequiredGuid(IReadOnlyList<string> arguments, string option)
    {
        string value = GetRequiredOption(arguments, option);
        return Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be a non-empty canonical UUID.");
    }

    private static bool HasSwitch(IReadOnlyList<string> arguments, string option)
    {
        int count = arguments.Count(argument => argument.Equals(option, StringComparison.Ordinal));
        if (count > 1)
        {
            throw new ArgumentException($"Option '{option}' can be specified only once.");
        }

        return count == 1;
    }

    private static string GetRequiredOption(IReadOnlyList<string> arguments, string option)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                if (index >= 0)
                {
                    throw new ArgumentException($"Option '{option}' can be specified only once.");
                }

                index = candidate;
            }
        }

        if (index < 0 || index + 1 >= arguments.Count)
        {
            throw new ArgumentException($"Required option '{option}' is missing.");
        }

        string value = arguments[index + 1];
        if (value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Required option '{option}' has no value.");
        }

        return value;
    }

    private static string? GetOptionalOption(
        IReadOnlyList<string> arguments,
        string option)
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
                throw new ArgumentException($"Option '{option}' can be specified only once.");
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
            throw new ArgumentException($"Option '{option}' has no value.");
        }

        return arguments[index + 1];
    }

}
