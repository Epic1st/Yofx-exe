using System.Globalization;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mt5.DemoExecutionTest;

/// <summary>
/// Repeats a full open-and-close cycle and reports the distribution rather than a single
/// reading.
///
/// <para>
/// One sample says nothing about a latency figure: the first call through any managed path
/// carries just-in-time compilation, and any single round trip can be an outlier. So the
/// warm-up is measured and reported separately rather than quietly dropped — hiding it would
/// make the steady-state numbers look better than the first order a strategy ever sends.
/// </para>
/// </summary>
internal static class LatencyBenchmark
{
    public static async Task RunAsync(Mt5NetApiDemoTradeClient client, int cycles)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);

        var engine = new List<double>(cycles * 2);
        var transport = new List<double>(cycles * 2);
        var total = new List<double>(cycles * 2);
        Mt5ExecutionLatency? firstEver = null;

        for (int cycle = 0; cycle <= cycles; cycle++)
        {
            Mt5DemoOrderReceipt opened = await client
                .SendAsync(Mt5DemoSide.Buy, 0.01, 0, 0, 0, "yo4x-bench").ConfigureAwait(false);
            if (opened.Ticket == 0)
            {
                Console.Error.WriteLine($"  cycle {cycle} was not filled; stopping the benchmark.");
                break;
            }

            Mt5DemoOrderReceipt closed = await client.CloseAsync(opened).ConfigureAwait(false);

            // Cycle zero is the warm-up. Reported, but kept out of the steady-state figures.
            if (cycle == 0)
            {
                firstEver = opened.Latency;
                continue;
            }

            foreach (Mt5ExecutionLatency sample in (Mt5ExecutionLatency[])[opened.Latency, closed.Latency])
            {
                engine.Add(sample.EngineMicroseconds);
                transport.Add(sample.TransportAndBrokerMicroseconds);
                total.Add(sample.TotalMicroseconds);
            }
        }

        Console.WriteLine();
        if (firstEver is { } warm)
        {
            Console.WriteLine(
                "first instruction of the session (includes just-in-time compilation)");
            Console.WriteLine(
                $"  engine {Ms(warm.EngineMicroseconds)} ms   "
                + $"transport+broker {Ms(warm.TransportAndBrokerMicroseconds)} ms   "
                + $"total {Ms(warm.TotalMicroseconds)} ms");
            Console.WriteLine();
        }

        Console.WriteLine($"steady state over {engine.Count} instructions, all figures in milliseconds");
        Report("engine path      ", engine);
        Report("transport+broker ", transport);
        Report("TOTAL end to end ", total);

        int underOne = engine.Count(sample => sample < 1000.0);
        Console.WriteLine();
        Console.WriteLine(
            $"  engine under 1 ms : {underOne}/{engine.Count} "
            + $"({(engine.Count == 0 ? 0 : 100.0 * underOne / engine.Count):F0}%)");
    }

    private static void Report(string label, List<double> microseconds)
    {
        if (microseconds.Count == 0)
        {
            Console.WriteLine($"  {label}: no samples");
            return;
        }

        double[] sorted = [.. microseconds.Order()];
        Console.WriteLine(
            $"  {label}: min {Ms(sorted[0])}  "
            + $"p50 {Ms(Percentile(sorted, 0.50))}  "
            + $"p95 {Ms(Percentile(sorted, 0.95))}  "
            + $"p99 {Ms(Percentile(sorted, 0.99))}  "
            + $"max {Ms(sorted[^1])}  "
            + $"mean {Ms(microseconds.Average())}");
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        int index = (int)Math.Ceiling(fraction * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    /// <summary>Renders microseconds as milliseconds, keeping enough places to stay truthful
    /// for values far below one millisecond.</summary>
    private static string Ms(double microseconds) =>
        (microseconds / 1000.0).ToString("F4", CultureInfo.InvariantCulture);
}
