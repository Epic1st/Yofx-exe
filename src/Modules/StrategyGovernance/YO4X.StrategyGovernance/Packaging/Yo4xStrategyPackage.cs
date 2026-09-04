using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YO4X.StrategyGovernance.Licensing;

namespace YO4X.StrategyGovernance.Packaging;

public sealed record StrategyParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("defaultValue")] string DefaultValue,
    [property: JsonPropertyName("comment")] string Comment);

public sealed record Yo4xStrategyManifest(
    [property: JsonPropertyName("strategyId")] string StrategyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("parameters")] IReadOnlyList<StrategyParameterInfo> Parameters,
    [property: JsonPropertyName("supportedSymbols")] IReadOnlyList<string> SupportedSymbols,
    [property: JsonPropertyName("supportedTimeframes")] IReadOnlyList<string> SupportedTimeframes,
    [property: JsonPropertyName("license")] StrategyLicenseToken? License = null,
    [property: JsonPropertyName("entryTypeName")] string? EntryTypeName = null,
    [property: JsonPropertyName("assemblySha256")] string? AssemblySha256 = null,
    [property: JsonPropertyName("publication")] StrategyPublicationToken? Publication = null);

public static class Yo4xStrategyPackage
{
    private static readonly byte[] MagicBytes = "YO4X"u8.ToArray();
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private const ushort CurrentVersion = 2;
    private const ushort LegacyVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HmacSize = 32;
    private const int MaximumPackageSize = 64 * 1024 * 1024;
    private const int MaximumManifestSize = 1024 * 1024;
    private const int MaximumAssemblySize = 32 * 1024 * 1024;

    public static byte[] Pack(
        Yo4xStrategyManifest manifest,
        byte[] compiledAssemblyBytes,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(compiledAssemblyBytes);

        if (aesKey.Length != 32)
            throw new ArgumentException("AES-256 key must be 32 bytes.", nameof(aesKey));
        if (hmacKey.Length != 32)
            throw new ArgumentException("HMAC key must be 32 bytes.", nameof(hmacKey));

        ValidateV2Manifest(manifest, compiledAssemblyBytes);

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);

        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[compiledAssemblyBytes.Length];

        using (var aesGcm = new AesGcm(aesKey, TagSize))
        {
            aesGcm.Encrypt(nonce, compiledAssemblyBytes, ciphertext, tag, manifestBytes);
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(MagicBytes);
        writer.Write(CurrentVersion);
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);
        writer.Write(nonce);
        writer.Write(tag);
        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);

        writer.Flush();
        byte[] bodyBytes = ms.ToArray();

        using var hmac = new HMACSHA256(hmacKey.ToArray());
        byte[] signature = hmac.ComputeHash(bodyBytes);

        using var finalMs = new MemoryStream();
        finalMs.Write(bodyBytes);
        finalMs.Write(signature);

        return finalMs.ToArray();
    }

    public static Yo4xStrategyManifest ReadManifest(byte[] packageBytes)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);

        ValidatePackageLength(packageBytes);

        using var ms = new MemoryStream(packageBytes);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        byte[] magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid .yo4x package magic identifier.");

        ushort version = reader.ReadUInt16();
        if (version is not LegacyVersion and not CurrentVersion)
            throw new InvalidDataException($"Unsupported .yo4x package version: {version}");

        int manifestLength = reader.ReadInt32();
        if (manifestLength <= 0 || manifestLength > MaximumManifestSize
            || manifestLength > packageBytes.Length - MinimumPackageSize())
            throw new InvalidDataException("Invalid .yo4x manifest length.");

        byte[] manifestBytes = ReadExactly(reader, manifestLength, "manifest");
        var manifest = JsonSerializer.Deserialize<Yo4xStrategyManifest>(manifestBytes, StrictJson);

        return manifest ?? throw new InvalidDataException("Could not parse .yo4x strategy manifest.");
    }

    public static byte[] UnpackAssembly(
        byte[] packageBytes,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);

        if (aesKey.Length != 32)
            throw new ArgumentException("AES-256 key must be 32 bytes.", nameof(aesKey));
        if (hmacKey.Length != 32)
            throw new ArgumentException("HMAC key must be 32 bytes.", nameof(hmacKey));

        ValidatePackageLength(packageBytes);

        int bodyLength = packageBytes.Length - HmacSize;
        ReadOnlySpan<byte> bodyBytes = packageBytes.AsSpan(0, bodyLength);
        ReadOnlySpan<byte> signature = packageBytes.AsSpan(bodyLength, HmacSize);

        using (var hmac = new HMACSHA256(hmacKey.ToArray()))
        {
            byte[] computedSig = hmac.ComputeHash(packageBytes, 0, bodyLength);
            if (!CryptographicOperations.FixedTimeEquals(computedSig, signature))
                throw new CryptographicException("Tamper detection: .yo4x package signature verification failed.");
        }

        using var ms = new MemoryStream(packageBytes, 0, bodyLength, writable: false);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        byte[] magic = ReadExactly(reader, 4, "magic");
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid .yo4x package magic identifier.");
        ushort version = reader.ReadUInt16();
        if (version is not LegacyVersion and not CurrentVersion)
            throw new InvalidDataException($"Unsupported .yo4x package version: {version}");
        int manifestLength = reader.ReadInt32();
        if (manifestLength <= 0 || manifestLength > MaximumManifestSize)
            throw new InvalidDataException("Invalid .yo4x manifest length.");
        byte[] manifestBytes = ReadExactly(reader, manifestLength, "manifest");

        byte[] nonce = ReadExactly(reader, NonceSize, "nonce");
        byte[] tag = ReadExactly(reader, TagSize, "authentication tag");
        int ciphertextLength = reader.ReadInt32();
        if (ciphertextLength <= 0 || ciphertextLength > MaximumAssemblySize
            || ciphertextLength != bodyLength - checked((int)ms.Position))
            throw new InvalidDataException("Invalid .yo4x ciphertext length.");
        byte[] ciphertext = ReadExactly(reader, ciphertextLength, "ciphertext");
        if (ms.Position != bodyLength)
            throw new InvalidDataException("The .yo4x package contains trailing body data.");

        byte[] plaintext = new byte[ciphertextLength];
        using (var aesGcm = new AesGcm(aesKey, TagSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, manifestBytes);
        }

        return plaintext;
    }

    public static byte[] UnpackAndValidate(
        byte[] packageBytes,
        ulong currentBrokerLogin,
        string currentBrokerServer,
        string publicKeyPem,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        byte[] assembly = UnpackAssembly(packageBytes, aesKey, hmacKey);
        try
        {
            Yo4xStrategyManifest manifest = ReadManifest(packageBytes);
            StrategyLicenseToken license = manifest.License
                ?? throw new LicenseValidationException(
                    LicenseStatus.Invalid,
                    "A signed strategy license is required.");
            LicenseAuthority.ValidateLicense(
                license,
                currentBrokerLogin,
                currentBrokerServer,
                publicKeyPem);
            ValidateAssemblyDigest(manifest, assembly, requireDigest: false);
            return assembly;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(assembly);
            throw;
        }
    }

    public static (Yo4xStrategyManifest Manifest, byte[] AssemblyBytes) UnpackAndValidate(
        byte[] packageBytes,
        StrategyLicenseValidationContext context,
        string publicKeyPem,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        byte[] assembly = UnpackAssembly(packageBytes, aesKey, hmacKey);
        try
        {
            Yo4xStrategyManifest manifest = ReadManifest(packageBytes);
            if (manifest.License is null)
                throw new LicenseValidationException(LicenseStatus.Invalid, "A signed strategy license is required.");
            ValidateAssemblyDigest(manifest, assembly, requireDigest: true);
            string digest = Sha256(assembly);
            if (!string.Equals(manifest.StrategyId, context.StrategyId, StringComparison.Ordinal)
                || !string.Equals(manifest.Version, context.StrategyVersion, StringComparison.Ordinal)
                || !FixedTimeHexEquals(digest, context.AssemblySha256))
            {
                throw new LicenseValidationException(
                    LicenseStatus.Invalid,
                    "The authenticated package does not match the authoritative strategy binding.");
            }
            LicenseAuthority.ValidateLicense(manifest.License, context, publicKeyPem);
            ValidateV2Manifest(manifest, assembly);
            return (manifest, assembly);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(assembly);
            throw;
        }
    }

    /// <summary>
    /// Validates a common publisher-signed marketplace package and a detached user/broker
    /// license. One package can therefore be listed once without embedding another user's
    /// identity, while execution remains strictly personalized.
    /// </summary>
    public static (Yo4xStrategyManifest Manifest, byte[] AssemblyBytes) UnpackAndValidate(
        byte[] packageBytes,
        StrategyLicenseToken detachedLicense,
        StrategyLicenseValidationContext context,
        string publicationPublicKeyPem,
        string licensePublicKeyPem,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        byte[] assembly = UnpackAssembly(packageBytes, aesKey, hmacKey);
        try
        {
            Yo4xStrategyManifest manifest = ReadManifest(packageBytes);
            StrategyPublicationToken publication = manifest.Publication
                ?? throw new CryptographicException("A signed marketplace publication is required.");
            StrategyPublicationClaims published = StrategyPublicationAuthority.Validate(
                publication,
                publicationPublicKeyPem);
            ValidateAssemblyDigest(manifest, assembly, requireDigest: true);
            string digest = Sha256(assembly);
            if (!string.Equals(published.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                || !string.Equals(published.StrategyName, manifest.Name, StringComparison.Ordinal)
                || !string.Equals(published.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                || !FixedTimeHexEquals(published.AssemblySha256, digest)
                || !string.Equals(context.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                || !string.Equals(context.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                || !FixedTimeHexEquals(context.AssemblySha256, digest))
            {
                throw new CryptographicException("The marketplace package publication binding is invalid.");
            }

            LicenseAuthority.ValidateLicense(detachedLicense, context, licensePublicKeyPem);
            ValidateV2Manifest(manifest, assembly);
            return (manifest, assembly);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(assembly);
            throw;
        }
    }

    /// <summary>Verifies a common marketplace artifact before it is published.</summary>
    public static (Yo4xStrategyManifest Manifest, byte[] AssemblyBytes) UnpackAndValidatePublication(
        byte[] packageBytes,
        string publicationPublicKeyPem,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey)
    {
        byte[] assembly = UnpackAssembly(packageBytes, aesKey, hmacKey);
        try
        {
            Yo4xStrategyManifest manifest = ReadManifest(packageBytes);
            StrategyPublicationClaims published = StrategyPublicationAuthority.Validate(
                manifest.Publication
                    ?? throw new CryptographicException("A signed marketplace publication is required."),
                publicationPublicKeyPem);
            ValidateAssemblyDigest(manifest, assembly, requireDigest: true);
            if (!string.Equals(published.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                || !string.Equals(published.StrategyName, manifest.Name, StringComparison.Ordinal)
                || !string.Equals(published.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                || !FixedTimeHexEquals(published.AssemblySha256, Sha256(assembly)))
            {
                throw new CryptographicException("The marketplace publication does not bind this package.");
            }
            ValidateV2Manifest(manifest, assembly);
            return (manifest, assembly);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(assembly);
            throw;
        }
    }

    private static void ValidateV2Manifest(Yo4xStrategyManifest manifest, byte[] assemblyBytes)
    {
        if ((manifest.License is null) == (manifest.Publication is null)
            || string.IsNullOrWhiteSpace(manifest.StrategyId)
            || manifest.StrategyId.Length > 200
            || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Name.Length > 300
            || string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.Version.Length > 100
            || string.IsNullOrWhiteSpace(manifest.EntryTypeName)
            || manifest.EntryTypeName.Length > 500
            || !string.Equals(manifest.AssemblySha256, Sha256(assemblyBytes), StringComparison.Ordinal)
            || manifest.License is not null &&
                (!string.Equals(manifest.License.Claims.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                 || !string.Equals(manifest.License.Claims.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                 || !string.Equals(manifest.License.Claims.AssemblySha256, manifest.AssemblySha256, StringComparison.Ordinal))
            || manifest.Publication is not null &&
                (!string.Equals(manifest.Publication.Claims.StrategyId, manifest.StrategyId, StringComparison.Ordinal)
                 || !string.Equals(manifest.Publication.Claims.StrategyName, manifest.Name, StringComparison.Ordinal)
                 || !string.Equals(manifest.Publication.Claims.StrategyVersion, manifest.Version, StringComparison.Ordinal)
                 || !string.Equals(manifest.Publication.Claims.AssemblySha256, manifest.AssemblySha256, StringComparison.Ordinal))
            || manifest.Parameters is null
            || manifest.Parameters.Count > 10_000
            || manifest.SupportedSymbols is null
            || manifest.SupportedSymbols.Count > 1_000
            || manifest.SupportedTimeframes is null
            || manifest.SupportedTimeframes.Count > 1_000)
        {
            throw new InvalidDataException("The .yo4x v2 manifest is incomplete or inconsistent.");
        }
    }

    private static void ValidateAssemblyDigest(
        Yo4xStrategyManifest manifest,
        byte[] assemblyBytes,
        bool requireDigest)
    {
        if (manifest.AssemblySha256 is null)
        {
            if (requireDigest)
                throw new InvalidDataException("The .yo4x manifest has no assembly digest.");
            return;
        }

        string actual = Sha256(assemblyBytes);
        if (!FixedTimeHexEquals(manifest.AssemblySha256, actual))
            throw new CryptographicException("The decrypted assembly digest does not match the manifest.");
    }

    private static void ValidatePackageLength(byte[] packageBytes)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);
        if (packageBytes.Length < MinimumPackageSize() || packageBytes.Length > MaximumPackageSize)
            throw new InvalidDataException("Invalid .yo4x package length.");
    }

    private static int MinimumPackageSize() => 4 + 2 + 4 + 1 + NonceSize + TagSize + 4 + 1 + HmacSize;

    private static byte[] ReadExactly(BinaryReader reader, int count, string field)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new EndOfStreamException($"The .yo4x {field} is truncated.");
        return value;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        if (expected.Length != actual.Length || expected.Length != 64)
            return false;
        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expected);
            actualBytes = Convert.FromHexString(actual);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }
}
