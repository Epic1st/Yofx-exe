using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Writes the LEAN on-disk tick-quote layout for forex and CFD symbols:
/// <c>{data-root}/{securityType}/{market}/tick/{ticker}/{yyyyMMdd}_quote.zip</c>, one entry named
/// <c>{yyyyMMdd}_{ticker}_quote.csv</c> whose lines are
/// <c>&lt;milliseconds-since-midnight-UTC&gt;,&lt;bid&gt;,&lt;ask&gt;</c> in ascending order.
/// The archive is built in a temporary file and moved into place, so a partial zip is never
/// observable at the target path.
/// </summary>
internal static class LeanTickZipWriter
{
    internal const string LineEndingName = "LF";
    internal const string TickTypeName = "quote";
    internal const string ResolutionName = "tick";

    /// <summary>
    /// A fixed entry timestamp. Zip entries otherwise carry the wall clock of the run, which would
    /// make the archive bytes, and therefore its digest, irreproducible.
    /// </summary>
    private static readonly DateTimeOffset EntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static string RelativePath(
        string securityTypeFolder,
        string market,
        string ticker,
        DateOnly date) =>
        string.Join(
            '/',
            securityTypeFolder,
            market,
            ResolutionName,
            ticker,
            FileName(date));

    internal static string FileName(DateOnly date) =>
        string.Concat(DateStamp(date), "_", TickTypeName, ".zip");

    internal static string EntryName(string ticker, DateOnly date) =>
        string.Concat(DateStamp(date), "_", ticker, "_", TickTypeName, ".csv");

    internal static string DateStamp(DateOnly date) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Renders the CSV payload for one symbol-day. Ticks must already be ascending.</summary>
    internal static byte[] RenderDay(IReadOnlyList<Mt5QuoteRow> ascendingTicks)
    {
        var builder = new StringBuilder();
        foreach (Mt5QuoteRow tick in ascendingTicks)
        {
            long milliseconds = tick.TimestampUtc.TimeOfDay.Ticks / TimeSpan.TicksPerMillisecond;
            builder.Append(milliseconds.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(tick.Bid.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(tick.Ask.ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Writes one day archive and returns its SHA-256. The caller has already proved the target is
    /// writable; this method still refuses to clobber an existing temporary file.
    /// </summary>
    internal static async Task<string> WriteDayAsync(
        string targetPath,
        string entryName,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException($"The target path '{targetPath}' has no directory.");
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            string.Concat(Path.GetFileName(targetPath), ".", Guid.NewGuid().ToString("N"), ".partial"));
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                entry.LastWriteTime = EntryTimestamp;
                await using Stream entryStream = entry.Open();
                await entryStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            string digest;
            await using (FileStream reader = File.OpenRead(temporaryPath))
            {
                digest = Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(reader, cancellationToken).ConfigureAwait(false));
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            return digest;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
