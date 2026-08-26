using System.Security.Cryptography;
using System.Text;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class PinnedMt5ServersDatEndpointReaderTests
{
    private static readonly string VendorArtifactPath = Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "mt5-net-api-full-binaries-main",
            "mt5api.dll"));

    [Fact]
    public void ReadsOnlyBoundedMetaQuotesDemoEndpointsFromVerifiedBytes()
    {
        byte[] content = "offline synthetic servers.dat fixture"u8.ToArray();
        string path = WriteTemporary(content);
        try
        {
            var loader = new FakeLoader(
            [
                new("Other-Demo", "ignored.example", 443),
                new("MetaQuotes-Demo", "demo.metaquotes.net", 443),
                new("MetaQuotes-Demo", "demo.metaquotes.net", 443)
            ]);
            var reader = new PinnedMt5ServersDatEndpointReader(
                VendorArtifactPath,
                path,
                Convert.ToHexString(SHA256.HashData(content)),
                loader);

            Mt5ServersDatEndpoint endpoint = Assert.Single(reader.ReadMetaQuotesDemoEndpoints());

            Assert.Equal("MetaQuotes-Demo", endpoint.ServerName);
            Assert.Equal("demo.metaquotes.net", endpoint.Host);
            Assert.Equal(443, endpoint.Port);
            Assert.Equal(1, loader.LoadCount);
            Assert.Equal(content, loader.ObservedBytes);
            Assert.Equal(
                PinnedMt5ServersDatEndpointReader.ApprovedVendorArtifactSha256,
                loader.ObservedAssemblyHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ServersDatHashMismatchFailsBeforeLoaderWithoutLeakingDetails()
    {
        byte[] content = "offline synthetic servers.dat fixture"u8.ToArray();
        string path = WriteTemporary(content);
        try
        {
            var loader = new FakeLoader([]);
            var reader = new PinnedMt5ServersDatEndpointReader(
                VendorArtifactPath,
                path,
                new string('0', 64),
                loader);

            InvalidDataException failure = Assert.Throws<InvalidDataException>(
                reader.ReadMetaQuotesDemoEndpoints);

            Assert.Equal("Pinned MT5 endpoint metadata could not be loaded.", failure.Message);
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(path, failure.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, loader.LoadCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VendorHashMismatchFailsBeforeLoader()
    {
        byte[] content = "not the approved vendor artifact"u8.ToArray();
        string artifactPath = WriteTemporary(content);
        string serversPath = WriteTemporary("fixture"u8.ToArray());
        try
        {
            var loader = new FakeLoader([]);
            var reader = new PinnedMt5ServersDatEndpointReader(
                artifactPath,
                serversPath,
                Convert.ToHexString(SHA256.HashData("fixture"u8)),
                loader);

            _ = Assert.Throws<InvalidDataException>(reader.ReadMetaQuotesDemoEndpoints);

            Assert.Equal(0, loader.LoadCount);
        }
        finally
        {
            File.Delete(artifactPath);
            File.Delete(serversPath);
        }
    }

    [Fact]
    public void InvalidOrUnapprovedProjectionIsRejectedAndRedacted()
    {
        byte[] content = "fixture"u8.ToArray();
        string path = WriteTemporary(content);
        try
        {
            var reader = new PinnedMt5ServersDatEndpointReader(
                VendorArtifactPath,
                path,
                Convert.ToHexString(SHA256.HashData(content)),
                new FakeLoader([new("Other-Demo", "example.test", 443)]));

            InvalidDataException failure = Assert.Throws<InvalidDataException>(
                reader.ReadMetaQuotesDemoEndpoints);

            Assert.Equal("Pinned MT5 endpoint metadata could not be loaded.", failure.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProjectsExactVendorMetadataShapeAndParsesAddressRecords()
    {
        var server = new ExactServer
        {
            ServerInfoEx = new ExactServerInfo { ServerName = "MetaQuotes-Demo" },
            Accesses =
            [
                new ExactAccess
                {
                    AccessRec = new ExactAccessRec { ServerName = "MetaQuotes-Demo-Access1" },
                    Addresses = [],
                    AddressesEx =
                    [
                        new ExactAddress { AddressRec = new ExactAddressRec { Address = "demo.example:444" } },
                        new ExactAddress { AddressRec = new ExactAddressRec { Address = "[2001:db8::1]:445" } },
                        new ExactAddress { AddressRec = new ExactAddressRec { Address = "fallback.example" } }
                    ]
                }
            ],
            AccessesEx = []
        };

        List<Mt5ServersDatEndpoint> endpoints = ReflectionMt5ServersDatLoader.Project(
            new[] { server });

        Assert.Contains(new("MetaQuotes-Demo", "demo.example", 444), endpoints);
        Assert.Contains(new("MetaQuotes-Demo", "2001:db8::1", 445), endpoints);
        Assert.Contains(new("MetaQuotes-Demo", "fallback.example", 443), endpoints);
    }

    [Fact]
    public void FortyEightApprovedEndpointsAreReturnedWithoutTruncation()
    {
        byte[] content = "fixture"u8.ToArray();
        string path = WriteTemporary(content);
        try
        {
            Mt5ServersDatEndpoint[] endpoints = Enumerable.Range(1, 48)
                .Select(index => new Mt5ServersDatEndpoint(
                    "MetaQuotes-Demo",
                    $"endpoint-{index}.example",
                    443))
                .ToArray();
            var reader = new PinnedMt5ServersDatEndpointReader(
                VendorArtifactPath,
                path,
                Convert.ToHexString(SHA256.HashData(content)),
                new FakeLoader(endpoints));

            Assert.Equal(48, reader.ReadMetaQuotesDemoEndpoints().Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SixtyFiveApprovedEndpointsFailsClosed()
    {
        byte[] content = "fixture"u8.ToArray();
        string path = WriteTemporary(content);
        try
        {
            Mt5ServersDatEndpoint[] endpoints = Enumerable.Range(1, 65)
                .Select(index => new Mt5ServersDatEndpoint(
                    "MetaQuotes-Demo",
                    $"endpoint-{index}.example",
                    443))
                .ToArray();
            var reader = new PinnedMt5ServersDatEndpointReader(
                VendorArtifactPath,
                path,
                Convert.ToHexString(SHA256.HashData(content)),
                new FakeLoader(endpoints));

            _ = Assert.Throws<InvalidDataException>(reader.ReadMetaQuotesDemoEndpoints);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FactoryUsesFourArgumentConstructorWhenCertificateIsAbsent()
    {
        var created = Assert.IsType<FourArgumentVendorClient>(
            PinnedMt5NetApiConnectionClientFactory.CreateVendorClient(
                typeof(FourArgumentVendorClient),
                42,
                "password",
                "host",
                443,
                [],
                string.Empty));

        Assert.Equal(4, created.Arity);
    }

    [Fact]
    public void FactoryUsesSixArgumentConstructorWhenCertificateIsPresent()
    {
        var created = Assert.IsType<SixArgumentVendorClient>(
            PinnedMt5NetApiConnectionClientFactory.CreateVendorClient(
                typeof(SixArgumentVendorClient),
                42,
                "password",
                "host",
                443,
                [1],
                "certificate-password"));

        Assert.Equal(6, created.Arity);
    }

    private static string WriteTemporary(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"yo4x-pinned-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class FakeLoader(IReadOnlyList<Mt5ServersDatEndpoint> endpoints)
        : IMt5ServersDatLoader
    {
        public int LoadCount { get; private set; }
        public byte[]? ObservedBytes { get; private set; }
        public string? ObservedAssemblyHash { get; private set; }

        public IReadOnlyList<Mt5ServersDatEndpoint> Load(
            Stream verifiedVendorAssembly,
            byte[] verifiedServersDat)
        {
            LoadCount++;
            ObservedBytes = verifiedServersDat.ToArray();
            ObservedAssemblyHash = Convert.ToHexString(SHA256.HashData(verifiedVendorAssembly));
            return endpoints;
        }
    }

    private sealed class FourArgumentVendorClient(
        ulong login,
        string password,
        string host,
        int port)
    {
        public int Arity { get; } =
            login == 42 && password == "password" && host == "host" && port == 443 ? 4 : 0;
    }

    private sealed class SixArgumentVendorClient(
        ulong login,
        string password,
        string host,
        int port,
        byte[] certificate,
        string certificatePassword)
    {
        public int Arity { get; } =
            login == 42 && password == "password" && host == "host" && port == 443 &&
            certificate.Length == 1 && certificate[0] == 1 &&
            certificatePassword == "certificate-password" ? 6 : 0;
    }

    private sealed class ExactServer
    {
        public required ExactServerInfo ServerInfoEx;
        public ExactServerInfo? ServerInfo { get; init; }
        public required ExactAccess[] Accesses;
        public required ExactAccess[] AccessesEx;
    }

    private sealed class ExactServerInfo
    {
        public required string ServerName;
    }

    private sealed class ExactAccess
    {
        public required object AccessRec;
        public required ExactAddress[] Addresses;
        public required ExactAddress[] AddressesEx;
    }

    private sealed class ExactAccessRec
    {
        public required string ServerName;
    }

    private sealed class ExactAddress
    {
        public required ExactAddressRec AddressRec;
    }

    private sealed class ExactAddressRec
    {
        public required string Address;
    }
}
