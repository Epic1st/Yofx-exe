using System.Globalization;
using System.Net;
using System.Text.Json;

namespace YO4X.Mt5.AccountInspector;

/// <summary>One broker access node, as the public directory publishes it.</summary>
/// <param name="Host">The host name or literal address.</param>
/// <param name="Port">The access port.</param>
internal sealed record BrokerAccessNode(string Host, int Port)
{
    /// <summary>Renders the node the way the directory does.</summary>
    public override string ToString() =>
        Host.Contains(':', StringComparison.Ordinal)
            ? "[" + Host + "]:" + Port.ToString(CultureInfo.InvariantCulture)
            : Host + ":" + Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether this node is reachable from outside the broker's own network. The directory
    /// publishes internal addresses alongside public ones, and dialling those only buys a
    /// long timeout.
    /// </summary>
    public bool IsPubliclyRoutable()
    {
        if (!IPAddress.TryParse(Host, out IPAddress? address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        byte[] octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 or 127 => false,
            172 => octets[1] is < 16 or > 31,
            192 => octets[1] != 168,
            169 => octets[1] != 254,
            _ => true
        };
    }
}

/// <summary>
/// Resolves broker access nodes from the public MetaTrader broker search, used only as a
/// fallback for servers the vendor cannot resolve from the name alone.
/// </summary>
internal static class BrokerEndpointDirectory
{
    private const string SearchEndpoint = "https://search.mtapi.io/Search";

    /// <summary>The directory query issued for a given server, recorded as provenance.</summary>
    /// <param name="serverName">The broker server name.</param>
    internal static string DescribeQuery(string serverName) =>
        SearchEndpoint + "?company=" + Uri.EscapeDataString(serverName) + "&mt5=true";

    /// <summary>
    /// Looks up the access nodes the directory publishes for one exact server name. Only an
    /// exact name match counts: the search returns every server the broker company runs, and
    /// a live server sitting next to a demo one is precisely what must not be dialled.
    /// </summary>
    /// <param name="client">The HTTP client to use.</param>
    /// <param name="serverName">The broker server name to match exactly.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    internal static async Task<IReadOnlyList<BrokerAccessNode>> ResolveAsync(
        HttpClient client,
        string serverName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri(DescribeQuery(serverName)), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var nodes = new List<BrokerAccessNode>();
        if (!document.RootElement.TryGetProperty("result", out JsonElement companies)
            || companies.ValueKind != JsonValueKind.Array)
        {
            return nodes;
        }

        foreach (JsonElement company in companies.EnumerateArray())
        {
            if (!company.TryGetProperty("results", out JsonElement servers)
                || servers.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement server in servers.EnumerateArray())
            {
                if (!server.TryGetProperty("name", out JsonElement name)
                    || !string.Equals(name.GetString(), serverName, StringComparison.OrdinalIgnoreCase)
                    || !server.TryGetProperty("access", out JsonElement access)
                    || access.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement node in access.EnumerateArray())
                {
                    if (TryParseAccessNode(node.GetString(), out BrokerAccessNode? parsed)
                        && !nodes.Contains(parsed))
                    {
                        nodes.Add(parsed);
                    }
                }
            }
        }

        return nodes;
    }

    /// <summary>
    /// Parses a directory access entry, which is either <c>host:port</c> or the bracketed
    /// <c>[v6:address]:port</c> form.
    /// </summary>
    private static bool TryParseAccessNode(string? value, out BrokerAccessNode node)
    {
        node = new BrokerAccessNode(string.Empty, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        string host;
        string port;
        if (trimmed.StartsWith('['))
        {
            int close = trimmed.IndexOf(']', StringComparison.Ordinal);
            if (close < 0 || close + 2 > trimmed.Length - 1 || trimmed[close + 1] != ':')
            {
                return false;
            }

            host = trimmed[1..close];
            port = trimmed[(close + 2)..];
        }
        else
        {
            int separator = trimmed.LastIndexOf(':');
            if (separator <= 0 || separator == trimmed.Length - 1)
            {
                return false;
            }

            host = trimmed[..separator];
            port = trimmed[(separator + 1)..];
        }

        if (host.Length == 0
            || !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort)
            || parsedPort is <= 0 or > ushort.MaxValue)
        {
            return false;
        }

        node = new BrokerAccessNode(host, parsedPort);
        return true;
    }
}
