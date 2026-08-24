using System.Net;
using Npgsql;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// Central fail-closed policy for every PostgreSQL connection that crosses a
/// YO4X runtime security boundary.
/// </summary>
public static class PostgresRuntimeConnectionPolicy
{
    public static bool HasSafeSessionConfiguration(NpgsqlConnectionStringBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);
#pragma warning disable CS0618 // Npgsql still accepts this legacy TLS-bypass switch; true must fail closed.
        bool trustServerCertificate = options.TrustServerCertificate;
#pragma warning restore CS0618
        return !options.IncludeErrorDetail
            && !options.LogParameters
            && !trustServerCertificate
            && string.IsNullOrWhiteSpace(options.Options)
            && string.IsNullOrWhiteSpace(options.SearchPath)
            && !options.NoResetOnClose
            && !options.Multiplexing;
    }

    public static bool HasRequiredTransport(
        NpgsqlConnectionStringBuilder options,
        bool allowInsecureLoopbackForDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.SslMode == SslMode.VerifyFull
            || allowInsecureLoopbackForDevelopment
                && options.SslMode == SslMode.Disable
                && IsExplicitLoopback(options.Host);
    }

    private static bool IsExplicitLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        string candidate = host.Trim();
        if (string.Equals(candidate, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (candidate.Length > 2
            && candidate[0] == '['
            && candidate[^1] == ']')
        {
            candidate = candidate[1..^1];
        }

        return IPAddress.TryParse(candidate, out IPAddress? address)
            && IPAddress.IsLoopback(address);
    }
}
