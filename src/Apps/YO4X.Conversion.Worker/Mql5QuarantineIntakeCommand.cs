using YO4X.StrategyGovernance;

namespace YO4X.Conversion.Worker;

public static class Mql5QuarantineIntakeCommand
{
    private const string CommandSwitch = "--quarantine-intake";

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
            if (arguments.Contains("--static-inventory", StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    "Quarantine intake and canonical static inventory are separate commands.");
            }

            EnsureSwitchOccursOnce(arguments, CommandSwitch);
            string sourceRoot = GetRequiredOption(arguments, "--source-root");
            string evidenceOutput = GetRequiredOption(arguments, "--evidence-output");
            string reportOutput = GetRequiredOption(arguments, "--report-output");
            Mql5ArtifactPathSet paths = Mql5ArtifactOutputGuard.Resolve(
                sourceRoot,
                evidenceOutput,
                reportOutput);
            sourceRoot = paths.SourceRoot;
            evidenceOutput = paths.OutputPaths[0];
            reportOutput = paths.OutputPaths[1];

            var canonicalJob = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
            Mql5CorpusManifest canonicalManifest = await canonicalJob
                .AnalyzeDirectoryAsync(sourceRoot, cancellationToken)
                .ConfigureAwait(false);
            var intakeJob = new Mql5QuarantineIntakeJob();
            Mql5QuarantineIntakeEvidence evidence = await intakeJob
                .AnalyzeDirectoryAsync(sourceRoot, canonicalManifest, cancellationToken)
                .ConfigureAwait(false);
            await Mql5QuarantineIntakeJob.WriteArtifactsAsync(
                    evidence,
                    sourceRoot,
                    evidenceOutput,
                    reportOutput,
                    cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"Quarantine intake complete: {evidence.Summary.NonCanonicalFileCount} non-canonical files, evidence {evidence.EvidenceSha256}; canonical corpus remains {evidence.CanonicalCorpus.FileCount} exact .mq5/.mqh files.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"Quarantine intake failed: {exception.Message}");
            return 2;
        }
    }

    private static void EnsureSwitchOccursOnce(
        IReadOnlyList<string> arguments,
        string option)
    {
        if (arguments.Count(argument => argument.Equals(option, StringComparison.Ordinal)) != 1)
        {
            throw new ArgumentException($"Option '{option}' must be specified exactly once.");
        }
    }

    private static string GetRequiredOption(
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

        if (index < 0
            || index + 1 >= arguments.Count
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Required option '{option}' is missing or has no value.");
        }

        return arguments[index + 1];
    }

}
