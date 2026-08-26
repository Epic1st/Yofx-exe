using System.Globalization;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mt5.AccountInspector;

/// <summary>
/// Connects to a linked MetaTrader 5 account and reports what the broker holds: balance,
/// equity, and every currently open order.
///
/// <para>
/// Read-only by construction. This tool cannot open, close or modify a position — it exists
/// to answer whether trades are there and what state they are in, which is a question about
/// observation, not one that needs the power to trade.
/// </para>
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
            or InvalidOperationException)
        {
            Console.Error.WriteLine("Account inspection failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0].Equals("sweep", StringComparison.Ordinal))
        {
            return await RunSweepAsync(arguments).ConfigureAwait(false);
        }

        string credentialKey = Required(arguments, "--credential-key");
        string host = Required(arguments, "--host");
        int port = int.Parse(Required(arguments, "--port"), CultureInfo.InvariantCulture);
        string artifact = Path.GetFullPath(Required(arguments, "--artifact"));

        var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, CancellationToken.None)
            .ConfigureAwait(false);
        if (credential is null)
        {
            Console.Error.WriteLine("No credential is stored under that key.");
            return 3;
        }

        credential.UsePassword(utf8 =>
        {
            string password = Encoding.UTF8.GetString(utf8);
            using Mt5NetApiAccountReader reader = Mt5NetApiAccountReader.Create(
                artifact, credential.Login, password, host, port);
            reader.SetConnectTimeout(60_000);
            Console.WriteLine("connecting…");
            reader.Connect();

            Mt5AccountState account = reader.ReadAccount();
            Console.WriteLine();
            Console.WriteLine($"  broker     : {account.Company}");
            Console.WriteLine($"  acct type  : '{account.AccountType}'");
            Console.WriteLine($"  server     : '{account.ServerName}'");
            Console.WriteLine($"  server tz  : UTC{(account.ServerTimeZoneInMinutes is { } o ? (o >= 0 ? "+" : "") + (o / 60.0).ToString("0.#", CultureInfo.InvariantCulture) : "?")}");
            Console.WriteLine($"  balance    : {account.Balance:F2} {account.Currency}");
            Console.WriteLine($"  equity     : {account.Equity:F2} {account.Currency}");
            Console.WriteLine($"  margin     : {account.Margin:F2} free {account.FreeMargin:F2}");
            Console.WriteLine($"  floating   : {account.Profit:F2}");

IReadOnlyList<Mt5BrokerSymbol> symbols = reader.ReadSymbols();
            Console.WriteLine();
            Console.WriteLine($"  symbols    : {symbols.Count}");
            foreach (Mt5BrokerSymbol s in symbols.Take(8))
            {
                Console.WriteLine($"    {s.Symbol,-14} d={s.Digits?.ToString(CultureInfo.InvariantCulture) ?? "-",-3} cs={s.ContractSize?.ToString(CultureInfo.InvariantCulture) ?? "-",-10} ccy={s.Currency ?? "-",-5} {s.Description}");
            }

            IReadOnlyList<Mt5OpenOrder> open = reader.ReadOpenOrders();
            Console.WriteLine();
            Console.WriteLine($"  open orders: {open.Count}");
            foreach (Mt5OpenOrder order in open)
            {
                Console.WriteLine(
                    $"    #{order.Ticket} {order.Symbol,-10} {order.Type,-10} {order.Volume,6:F2} lots "
                    + $"@ {order.OpenPrice,10:F5}  sl {order.StopLoss,10:F5}  tp {order.TakeProfit,10:F5}  "
                    + $"P/L {order.Profit,10:F2}  opened {order.OpenTime:yyyy-MM-dd HH:mm}");
            }

            return 0;
        });

        return 0;
    }

    /// <summary>
    /// Walks the whole vault and reports, per account, whether the broker can actually be
    /// reached — and what the broker says the account is. A server name ending in "Demo" is
    /// not taken as evidence of anything; the account group read from the live session is.
    /// </summary>
    private static async Task<int> RunSweepAsync(string[] arguments)
    {
        string artifact = Path.GetFullPath(Required(arguments, "--artifact"));
        string output = Path.GetFullPath(Required(arguments, "--output"));
        string vaultRoot = Optional(arguments, "--vault-root")
            ?? DpapiLocalMt5CredentialVault.GetDefaultVaultRoot();
        int connectTimeout = int.Parse(
            Optional(arguments, "--connect-timeout-ms") ?? "30000",
            CultureInfo.InvariantCulture);
        int directoryAttempts = int.Parse(
            Optional(arguments, "--directory-attempts") ?? "3",
            CultureInfo.InvariantCulture);

        Console.WriteLine($"vault      : {vaultRoot}");
        Console.WriteLine($"artifact   : {artifact}");
        Console.WriteLine($"timeout    : {connectTimeout} ms per attempt, {directoryAttempts} directory endpoint(s)");
        Console.WriteLine();

        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<AccountConnectivityResult> results = await ConnectivitySweep
            .RunAsync(vaultRoot, artifact, connectTimeout, directoryAttempts, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(ConnectivitySweep.RenderTable(results));

        foreach (AccountConnectivityResult result in results)
        {
            if (result.Connected && !result.AccountGroupIndicatesDemo)
            {
                Console.WriteLine(
                    $"WARNING  {result.CredentialKey[..10]} on {result.Server} connected but its account "
                    + $"group is '{result.AccountGroup}', which does not state demo. Do not trade it.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(
            output,
            ConnectivitySweep.RenderJson(results, PinnedVendorAssembly.ApprovedArtifactSha256, observedAt),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"wrote {output}");

        int connected = results.Count(result => result.Connected);
        Console.WriteLine(
            $"{connected}/{results.Count} account(s) connected; "
            + $"{results.Count(result => result.SafeForDemoExecution)} usable as demo.");
        return connected == results.Count ? 0 : 1;
    }

    private static string? Optional(string[] arguments, string option)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals(option, StringComparison.Ordinal))
            {
                return arguments[index + 1];
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
