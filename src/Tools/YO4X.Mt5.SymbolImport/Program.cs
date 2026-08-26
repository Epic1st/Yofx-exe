using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mt5.SymbolImport;

/// <summary>
/// Reads a broker's whole instrument catalogue from a live session and stores it locally, so
/// the dashboard can offer real symbols instead of asking the operator to type one.
///
/// <para>
/// The import is a replacement, not a merge: a symbol the broker has withdrawn must disappear
/// rather than linger as something an operator can still select. Everything is written in one
/// transaction, so a failure partway leaves the previous catalogue intact instead of a
/// half-replaced list nobody can trust.
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
            or InvalidOperationException
            or NpgsqlException)
        {
            Console.Error.WriteLine("Symbol import failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        string connectionString = Option(arguments, "--connection")
            ?? Environment.GetEnvironmentVariable("YO4X_BACKTEST_CONNECTION")
            ?? throw new ArgumentException("Pass --connection or set YO4X_BACKTEST_CONNECTION.");
        string credentialKey = Required(arguments, "--credential-key");
        string host = Required(arguments, "--host");
        int port = int.Parse(Required(arguments, "--port"), CultureInfo.InvariantCulture);
        string artifact = Path.GetFullPath(Required(arguments, "--artifact"));
        Guid tenantId = Guid.Parse(Option(arguments, "--tenant-id") ?? "019c8d27-763d-7000-8000-000000000001");

        var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, CancellationToken.None).ConfigureAwait(false);
        if (credential is null)
        {
            Console.Error.WriteLine("No credential is stored under that key.");
            return 3;
        }

        string server = credential.Server;
        IReadOnlyList<Mt5BrokerSymbol> symbols = credential.UsePassword(utf8 =>
        {
            string password = Encoding.UTF8.GetString(utf8);
            using Mt5NetApiAccountReader reader = Mt5NetApiAccountReader.Create(
                artifact, credential.Login, password, host, port);
            reader.SetConnectTimeout(60_000);
            Console.WriteLine("connecting…");
            reader.Connect();
            return reader.ReadSymbols();
        });

        Console.WriteLine($"broker            : {server}");
        Console.WriteLine($"symbols reported  : {symbols.Count}");
        if (symbols.Count == 0)
        {
            Console.Error.WriteLine("The broker reported no symbols; nothing was written.");
            return 4;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync().ConfigureAwait(false);

        await using (var delete = new NpgsqlCommand(
            "delete from bots.broker_symbols where tenant_id = @tenant_id and server = @server",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            delete.Parameters.AddWithValue("server", NpgsqlDbType.Text, server);
            await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        int written = 0;
        foreach (Mt5BrokerSymbol symbol in symbols)
        {
            await using var insert = new NpgsqlCommand(
                """
                insert into bots.broker_symbols
                    (id, tenant_id, server, symbol, description, digits,
                     contract_size, currency, observed_at)
                values
                    (@id, @tenant_id, @server, @symbol, @description, @digits,
                     @contract_size, @currency, clock_timestamp())
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            insert.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            insert.Parameters.AddWithValue("server", NpgsqlDbType.Text, server);
            insert.Parameters.AddWithValue("symbol", NpgsqlDbType.Text, symbol.Symbol);
            insert.Parameters.AddWithValue("description", NpgsqlDbType.Text,
                Absent(symbol.Description));
            insert.Parameters.AddWithValue("digits", NpgsqlDbType.Integer,
                (object?)symbol.Digits ?? DBNull.Value);
            insert.Parameters.AddWithValue("contract_size", NpgsqlDbType.Numeric,
                (object?)symbol.ContractSize ?? DBNull.Value);
            insert.Parameters.AddWithValue("currency", NpgsqlDbType.Char,
                Absent(symbol.Currency));
            written += await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        Console.WriteLine($"stored            : {written} symbols for {server}");
        return 0;
    }

    /// <summary>
    /// A value the broker did not really report. The columns refuse an empty string, and one
    /// empty description would abort the whole replacement, so blank is normalised to absent
    /// here rather than relied on upstream.
    /// </summary>
    private static object Absent(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? Option(string[] arguments, string option)
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

    private static string Required(string[] arguments, string option) =>
        Option(arguments, option) ?? throw new ArgumentException("Option '" + option + "' is required.");
}
