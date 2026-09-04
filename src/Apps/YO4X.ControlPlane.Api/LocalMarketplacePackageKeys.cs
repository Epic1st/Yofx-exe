using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace YO4X.ControlPlane.Api;

internal sealed class LocalMarketplacePackageKeyProvider
{
    private const string ApplicationName = "YO4X.AdminPortal";
    private const string Purpose = "YO4X.AdminPortal.MarketplacePackageKeys.v1";
    private readonly string documentPath;
    private readonly IDataProtector protector;

    internal LocalMarketplacePackageKeyProvider(string documentPath)
    {
        this.documentPath = Path.GetFullPath(documentPath);
        string dataDirectory = Path.GetDirectoryName(this.documentPath)
            ?? throw new InvalidOperationException("The marketplace package-key path has no parent directory.");
        string keyRing = Path.Combine(dataDirectory, "keys");
        protector = DataProtectionProvider.Create(
            new DirectoryInfo(keyRing),
            configuration => configuration.SetApplicationName(ApplicationName))
            .CreateProtector(Purpose);
    }

    internal LocalMarketplacePackageKeys Open()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(documentPath));
        JsonElement root = json.RootElement;
        if (root.GetProperty("SchemaVersion").GetInt32() != 1)
            throw new InvalidDataException("The marketplace package-key version is unsupported.");

        byte[] protectedPrivate = Convert.FromBase64String(
            root.GetProperty("ProtectedPrivateKeyPem").GetString()
            ?? throw new InvalidDataException("The marketplace signing key is absent."));
        byte[] protectedAes = Convert.FromBase64String(
            root.GetProperty("ProtectedAesKey").GetString()
            ?? throw new InvalidDataException("The marketplace AES key is absent."));
        byte[] protectedHmac = Convert.FromBase64String(
            root.GetProperty("ProtectedHmacKey").GetString()
            ?? throw new InvalidDataException("The marketplace HMAC key is absent."));
        byte[] privatePem = [];
        byte[] aes = [];
        byte[] hmac = [];
        try
        {
            privatePem = protector.Unprotect(protectedPrivate);
            aes = protector.Unprotect(protectedAes);
            hmac = protector.Unprotect(protectedHmac);
            if (aes.Length != 32 || hmac.Length != 32)
                throw new InvalidDataException("The marketplace package keys have invalid lengths.");
            var result = new LocalMarketplacePackageKeys(
                Encoding.UTF8.GetString(privatePem),
                root.GetProperty("PublicKeyPem").GetString()
                    ?? throw new InvalidDataException("The marketplace public key is absent."),
                aes,
                hmac,
                root.GetProperty("SigningKeyId").GetString()
                    ?? throw new InvalidDataException("The marketplace signing key id is absent."));
            aes = [];
            hmac = [];
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPrivate);
            CryptographicOperations.ZeroMemory(protectedAes);
            CryptographicOperations.ZeroMemory(protectedHmac);
            CryptographicOperations.ZeroMemory(privatePem);
            CryptographicOperations.ZeroMemory(aes);
            CryptographicOperations.ZeroMemory(hmac);
        }
    }
}

internal sealed class LocalMarketplacePackageKeys(
    string privateKeyPem,
    string publicKeyPem,
    byte[] aesKey,
    byte[] hmacKey,
    string signingKeyId) : IDisposable
{
    internal string PrivateKeyPem { get; } = privateKeyPem;
    internal string PublicKeyPem { get; } = publicKeyPem;
    internal byte[] AesKey { get; } = aesKey;
    internal byte[] HmacKey { get; } = hmacKey;
    internal string SigningKeyId { get; } = signingKeyId;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(AesKey);
        CryptographicOperations.ZeroMemory(HmacKey);
    }
}
