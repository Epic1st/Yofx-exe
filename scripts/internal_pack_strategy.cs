using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using YO4X.Mql5.Compilation.Packaging;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: PackStrategyCli <source.mq5> <out.yo4x> [licenseType] [boundLogin] [boundServer] [expiresInDays] [author]");
            return 1;
        }

        string sourcePath = Path.GetFullPath(args[0]);
        string outPath = Path.GetFullPath(args[1]);
        string licenseTypeStr = args.Length > 2 ? args[2] : "Lifetime";
        ulong boundLogin = args.Length > 3 && ulong.TryParse(args[3], CultureInfo.InvariantCulture, out var login) ? login : 0;
        string boundServer = args.Length > 4 ? args[4] : "Exness-MT5Trial7";
        int expiresInDays = args.Length > 5 && int.TryParse(args[5], CultureInfo.InvariantCulture, out var exp) ? exp : 0;
        string author = args.Length > 6 ? args[6] : "YO4X Creator";

        string dir = AppContext.BaseDirectory;
        string keysPath = "";
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, ".local", "development", "platform_keys.json");
            if (File.Exists(candidate))
            {
                keysPath = candidate;
                break;
            }
            string parent = Path.GetDirectoryName(dir)!;
            if (parent == dir) break;
            dir = parent;
        }

        if (string.IsNullOrEmpty(keysPath))
        {
            keysPath = @"C:\Users\Dev23\Desktop\yo4x\.local\development\platform_keys.json";
        }

        string privKey = "";
        string pubKey = "";

        if (File.Exists(keysPath))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(keysPath));
            privKey = doc.RootElement.GetProperty("PrivateKey").GetString()!;
            pubKey = doc.RootElement.GetProperty("PublicKey").GetString()!;
        }
        else
        {
            var kp = LicenseAuthority.GenerateMasterKeyPair();
            privKey = kp.PrivateKeyPem;
            pubKey = kp.PublicKeyPem;
            Directory.CreateDirectory(Path.GetDirectoryName(keysPath)!);
            File.WriteAllText(keysPath, JsonSerializer.Serialize(new { PrivateKey = privKey, PublicKey = pubKey }, JsonOpts));
        }

        string sourceText = File.ReadAllText(sourcePath);
        string strategyName = Path.GetFileName(sourcePath);

        Enum.TryParse<LicenseType>(licenseTypeStr, true, out var licenseType);
        DateTimeOffset? expiresAt = expiresInDays > 0 ? DateTimeOffset.UtcNow.AddDays(expiresInDays) : null;

        var claims = new StrategyLicenseClaims(
            Guid.NewGuid(),
            Guid.Parse("019c8d27-763d-7000-8000-000000000001"),
            Guid.Parse("019c8d27-763d-7000-8000-000000000002"),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)))[..16].ToLowerInvariant(),
            Path.GetFileNameWithoutExtension(strategyName),
            licenseType,
            boundLogin > 0 ? new[] { boundLogin } : Array.Empty<ulong>(),
            !string.IsNullOrWhiteSpace(boundServer) ? new[] { boundServer } : Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            expiresAt,
            5);

        var licenseToken = LicenseAuthority.IssueLicenseToken(claims, privKey);

        byte[] aesKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("YO4X_PLATFORM_AES_MASTER_KEY_2026_PRODUCTION"));
        byte[] hmacKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("YO4X_PLATFORM_HMAC_MASTER_KEY_2026_PRODUCTION"));

        Console.WriteLine($"[PACKER] Compiling & Encrypting '{strategyName}' -> '{Path.GetFileName(outPath)}'...");
        var (pkgBytes, manifest) = Yo4xStrategyPacker.PackMql5Source(
            strategyName,
            sourceText,
            aesKey,
            hmacKey,
            licenseToken,
            author,
            "Proprietary strategy protected by YO4X DRM Engine.");

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, pkgBytes);
        Console.WriteLine($"[SUCCESS] Protected package written: {outPath} ({pkgBytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)");
        Console.WriteLine($"[LICENSE] Bound to Login: {(boundLogin > 0 ? boundLogin.ToString(CultureInfo.InvariantCulture) : "ALL")} | Server: {boundServer} | Type: {licenseType}");

        return 0;
    }
}
