using System.Globalization;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mt5.DemoExecutionTest;

/// <summary>
/// Exercises the full order lifecycle against a live demo account and measures each step:
/// open a market position, move its stop and target, close it, then place and cancel a
/// pending order. Every instruction is reported with its own latency, split into the half
/// this engine controls and the half the broker does.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            return await RunAsync(arguments).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or TimeoutException)
        {
            Console.Error.WriteLine("Execution test failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        string credentialKey = Required(arguments, "--credential-key");
        string host = Required(arguments, "--host");
        int port = int.Parse(Required(arguments, "--port"), CultureInfo.InvariantCulture);
        string artifact = Path.GetFullPath(Required(arguments, "--artifact"));
        string symbol = Required(arguments, "--symbol");
        string enableFile = Path.GetFullPath(Required(arguments, "--enable-file"));
        Mt5TradingEnvironment environment = Optional(arguments, "--environment") is "live"
            ? Mt5TradingEnvironment.Live
            : Mt5TradingEnvironment.Demo;

        var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, CancellationToken.None).ConfigureAwait(false);
        if (credential is null)
        {
            Console.Error.WriteLine("No credential is stored under that key.");
            return 3;
        }

        string password = credential.UsePassword(Encoding.UTF8.GetString);
        using var client = Mt5NetApiDemoTradeClient.Create(
            artifact, credential.Login, password, host, port, symbol, enableFile,
            line => Console.WriteLine("  " + line), environment);
        client.SetConnectTimeout(60_000);
        client.Connect();
        Console.WriteLine($"  declared environment: {environment}");
        client.StartQuoteStream();

        if (Optional(arguments, "--cycles") is { } raw && int.TryParse(raw, out int cycles) && cycles > 0)
        {
            await LatencyBenchmark.RunAsync(client, cycles).ConfigureAwait(false);
            return 0;
        }

        var latencies = new List<Mt5ExecutionLatency>();

        Console.WriteLine();
        Console.WriteLine("[1] open market position");
        Mt5DemoOrderReceipt opened = await client
            .SendAsync(Mt5DemoSide.Buy, 0.01, 0, 0, 0, "yo4x-lifecycle").ConfigureAwait(false);
        latencies.Add(opened.Latency);
        if (opened.Ticket == 0)
        {
            Console.Error.WriteLine("The broker did not return a ticket; stopping before any further step.");
            return 4;
        }

        Console.WriteLine();
        Console.WriteLine("[2] modify stop and target");
        double stop = Math.Round(opened.Price * 0.995, 5);
        double target = Math.Round(opened.Price * 1.005, 5);
        latencies.Add(await client.ModifyAsync(opened, stop, target).ConfigureAwait(false));

        Console.WriteLine();
        Console.WriteLine("[3] close position");
        Mt5DemoOrderReceipt closed = await client.CloseAsync(opened).ConfigureAwait(false);
        latencies.Add(closed.Latency);

        Console.WriteLine();
        Console.WriteLine("[4] place pending stop order");
        double trigger = Math.Round(opened.Price * 1.01, 5);
        Mt5DemoOrderReceipt placed = await client
            .SendAsync(Mt5DemoSide.BuyStop, 0.01, trigger, 0, 0, "yo4x-pending").ConfigureAwait(false);
        latencies.Add(placed.Latency);

        if (placed.Ticket != 0)
        {
            Console.WriteLine();
            Console.WriteLine("[5] cancel pending order");
            latencies.Add(await client.CancelAsync(placed).ConfigureAwait(false));
        }

        Console.WriteLine();
        Console.WriteLine("latency summary");
        Console.WriteLine($"  instructions      : {latencies.Count}");
        Console.WriteLine(
            "  engine path        : "
            + $"min {latencies.Min(l => l.EngineMicroseconds):F1}us  "
            + $"max {latencies.Max(l => l.EngineMicroseconds):F1}us  "
            + $"mean {latencies.Average(l => l.EngineMicroseconds):F1}us");
        Console.WriteLine(
            "  transport + broker: "
            + $"min {latencies.Min(l => l.TransportAndBrokerMicroseconds) / 1000:F1}ms  "
            + $"max {latencies.Max(l => l.TransportAndBrokerMicroseconds) / 1000:F1}ms  "
            + $"mean {latencies.Average(l => l.TransportAndBrokerMicroseconds) / 1000:F1}ms");
        return 0;
    }

    private static string? Optional(string[] arguments, string option)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals(option, StringComparison.Ordinal))
            {
                return arguments[index + 1].Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static string Required(string[] arguments, string option)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals(option, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        throw new ArgumentException("Option '" + option + "' is required.");
    }
}
