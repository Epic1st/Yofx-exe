using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.ConnectionProbe.Windows;

public sealed record Mt5ServersDatEndpoint(string ServerName, string Host, int Port);

public interface IMt5ServersDatLoader
{
    IReadOnlyList<Mt5ServersDatEndpoint> Load(
        Stream verifiedVendorAssembly,
        byte[] verifiedServersDat);
}

/// <summary>
/// Reads endpoint metadata offline from two exactly pinned artifacts. Both files remain
/// locked from before hashing until their verified bytes have been consumed, preventing
/// a path replacement between verification and load.
/// </summary>
public sealed class PinnedMt5ServersDatEndpointReader
{
    internal const int MaximumProjectedEndpoints = 64;
    public const string ApprovedVendorArtifactSha256 =
        PinnedMt5NetApiConnectionClientFactory.ApprovedArtifactSha256;
    public const string ApprovedServerName = "MetaQuotes-Demo";

    private readonly string artifactPath;
    private readonly string serversDatPath;
    private readonly string approvedServersDatSha256;
    private readonly IMt5ServersDatLoader loader;

    public PinnedMt5ServersDatEndpointReader(
        string artifactPath,
        string serversDatPath,
        string approvedServersDatSha256,
        IMt5ServersDatLoader? loader = null)
    {
        this.artifactPath = NormalizePath(artifactPath);
        this.serversDatPath = NormalizePath(serversDatPath);
        this.approvedServersDatSha256 = NormalizeSha256(approvedServersDatSha256);
        this.loader = loader ?? new ReflectionMt5ServersDatLoader();
    }

    public IReadOnlyList<Mt5ServersDatEndpoint> ReadMetaQuotesDemoEndpoints()
    {
        try
        {
            using FileStream artifact = OpenLocked(artifactPath);
            VerifyStreamHash(artifact, ApprovedVendorArtifactSha256);

            using FileStream serversDat = OpenLocked(serversDatPath);
            byte[] verifiedBytes = ReadAndVerifyServersDat(serversDat);
            try
            {
                IReadOnlyList<Mt5ServersDatEndpoint> loaded = loader.Load(artifact, verifiedBytes);
                Mt5ServersDatEndpoint[] projected = loaded
                    .Where(endpoint => string.Equals(
                        endpoint.ServerName,
                        ApprovedServerName,
                        StringComparison.Ordinal))
                    .Select(ValidateAndSnapshot)
                    .Distinct()
                    .ToArray();

                if (projected.Length is 0 or > MaximumProjectedEndpoints)
                {
                    throw new InvalidDataException();
                }

                return projected;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verifiedBytes);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Paths, hashes, vendor types and parsed content are deliberately omitted.
            throw new InvalidDataException(
                "Pinned MT5 endpoint metadata could not be loaded.");
        }
    }

    private byte[] ReadAndVerifyServersDat(FileStream stream)
    {
        if (stream.Length is <= 0 or > 16 * 1024 * 1024)
        {
            throw new InvalidDataException();
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, approvedServersDatSha256, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException();
        }

        return bytes;
    }

    private static Mt5ServersDatEndpoint ValidateAndSnapshot(Mt5ServersDatEndpoint endpoint)
    {
        if (endpoint is null ||
            !string.Equals(endpoint.ServerName, ApprovedServerName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            endpoint.Host.Length > 253 ||
            endpoint.Host.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) ||
            endpoint.Port is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException();
        }

        return new Mt5ServersDatEndpoint(ApprovedServerName, endpoint.Host.Trim(), endpoint.Port);
    }

    private static FileStream OpenLocked(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.SequentialScan);

    private static void VerifyStreamHash(FileStream stream, string expected)
    {
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        stream.Position = 0;
    }

    private static string NormalizePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value);
    }

    private static string NormalizeSha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 value must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        return value.ToUpperInvariant();
    }
}

internal sealed class ReflectionMt5ServersDatLoader : IMt5ServersDatLoader
{
    public IReadOnlyList<Mt5ServersDatEndpoint> Load(
        Stream verifiedVendorAssembly,
        byte[] verifiedServersDat)
    {
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(verifiedVendorAssembly);
        Type apiType = assembly.GetType("mtapi.mt5.MT5API", true, false)!;
        MethodInfo method = apiType.GetMethod(
            "LoadServersDat",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(byte[])],
            modifiers: null)
            ?? throw new MissingMethodException(apiType.FullName, "LoadServersDat(byte[])");

        object result = method.Invoke(null, [verifiedServersDat])
            ?? throw new InvalidDataException();
        return Project(result);
    }

    internal static List<Mt5ServersDatEndpoint> Project(object result)
    {
        var endpoints = new List<Mt5ServersDatEndpoint>();
        foreach (object serverItem in Enumerate(result, 256))
        {
            object server = ReadMember(serverItem, "Value") ?? serverItem;
            object serverInfoEx = ReadMember(server, "ServerInfoEx") ?? server;
            object serverInfo = ReadMember(server, "ServerInfo") ?? serverInfoEx;
            string? serverName = AsString(serverInfoEx)
                ?? ReadString(serverInfoEx, "ServerName", "Name")
                ?? AsString(serverInfo)
                ?? ReadString(serverInfo, "ServerName", "Name")
                ?? ReadString(server, "ServerName", "Name")
                ?? AsString(ReadMember(serverItem, "Key"));

            foreach (string accessMember in new[] { "Accesses", "AccessesEx" })
            {
                foreach (object access in Enumerate(
                             ReadMember(server, accessMember),
                             PinnedMt5ServersDatEndpointReader.MaximumProjectedEndpoints))
                {
                    object accessRec = ReadMember(access, "AccessRec", "AccessRecEx") ?? access;
                    // AccessRec is the authoritative per-cluster server identity.
                    // ServerInfo can describe the broader broker group and must not
                    // mask an exact MetaQuotes-Demo access record.
                    string? accessServerName = ReadString(accessRec, "ServerName", "Name");
                    bool approved = string.Equals(
                            accessServerName,
                            PinnedMt5ServersDatEndpointReader.ApprovedServerName,
                            StringComparison.Ordinal)
                        || string.Equals(
                            serverName,
                            PinnedMt5ServersDatEndpointReader.ApprovedServerName,
                            StringComparison.Ordinal);
                    if (!approved)
                    {
                        continue;
                    }

                    foreach (string addressMember in new[] { "Addresses", "AddressesEx" })
                    {
                        foreach (object address in Enumerate(ReadMember(access, addressMember), 32))
                        {
                            object addressRec = ReadMember(address, "AddressRec", "AddressRecEx") ?? address;
                            string? value = ReadString(addressRec, "Address");
                            if (TryParseAddress(value, out string? host, out int port))
                            {
                                endpoints.Add(new Mt5ServersDatEndpoint(
                                    PinnedMt5ServersDatEndpointReader.ApprovedServerName,
                                    host,
                                    port));
                            }
                        }
                    }

                }
            }
        }

        return endpoints;
    }

    private static IEnumerable<object> Enumerate(object? value, int limit)
    {
        if (value is null || value is string || value is not IEnumerable enumerable)
        {
            yield break;
        }

        int count = 0;
        foreach (object? item in enumerable)
        {
            if (++count > limit)
            {
                throw new InvalidDataException();
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static string? ReadString(object instance, params string[] names)
    {
        object? value = ReadMember(instance, names);
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? AsString(object? value) => value as string;

    private static bool TryParseAddress(string? value, out string host, out int port)
    {
        host = string.Empty;
        port = 443;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        if (candidate.Length > 0 && candidate[0] == '[')
        {
            int closingBracket = candidate.IndexOf(']');
            if (closingBracket <= 1)
            {
                return false;
            }

            host = candidate[1..closingBracket];
            if (closingBracket + 1 == candidate.Length)
            {
                return true;
            }

            return candidate[closingBracket + 1] == ':' &&
                int.TryParse(
                    candidate.AsSpan(closingBracket + 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port) &&
                port is > 0 and <= ushort.MaxValue;
        }

        int firstColon = candidate.IndexOf(':');
        int lastColon = candidate.LastIndexOf(':');
        if (firstColon >= 0 && firstColon == lastColon)
        {
            host = candidate[..firstColon];
            return host.Length > 0 &&
                int.TryParse(
                    candidate.AsSpan(firstColon + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port) &&
                port is > 0 and <= ushort.MaxValue;
        }

        // Unbracketed IPv6 and host-only records use the MT5 default endpoint port.
        host = candidate;
        return true;
    }

    private static object? ReadMember(object instance, params string[] names)
    {
        Type type = instance.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance);
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field is not null)
            {
                return field.GetValue(instance);
            }
        }

        return null;
    }
}
