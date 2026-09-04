using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using YO4X.Mql5.Backtest;
using YO4X.Mql5.Compilation.Packaging;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;
using YO4X.Mql5.Runtime;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.StrategyPackage.Tool;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            return arguments.FirstOrDefault() switch
            {
                "pack" => Pack(arguments[1..]),
                "pack-publication" => PackPublication(arguments[1..]),
                "verify" => Verify(arguments[1..]),
                "self-test-straddle" => SelfTestStraddle(arguments[1..]),
                _ => Usage()
            };
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or CryptographicException
            or LicenseValidationException)
        {
            Console.Error.WriteLine($"package operation failed ({exception.GetType().Name})");
            if (string.Equals(
                    Environment.GetEnvironmentVariable("YO4X_PACKAGE_TOOL_DIAGNOSTICS"),
                    "1",
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(exception.Message);
            }
            return 2;
        }
    }

    private static int Pack(string[] arguments)
    {
        string sourcePath = ExistingFile(Required(arguments, "--source"));
        string outputPath = Path.GetFullPath(Required(arguments, "--out"));
        string privateKey = File.ReadAllText(ExistingFile(Required(arguments, "--private-key")));
        byte[] aesKey = ReadSecretKey(Required(arguments, "--aes-key"));
        byte[] hmacKey = ReadSecretKey(Required(arguments, "--hmac-key"));
        try
        {
            Guid tenantId = Guid.Parse(Required(arguments, "--tenant"));
            Guid userId = Guid.Parse(Required(arguments, "--user"));
            ulong account = ulong.Parse(Required(arguments, "--account"), CultureInfo.InvariantCulture);
            string server = Required(arguments, "--server");
            LicenseType licenseType = Enum.Parse<LicenseType>(Required(arguments, "--license-type"), ignoreCase: true);
            int maximumBots = int.Parse(Required(arguments, "--max-bots"), CultureInfo.InvariantCulture);
            string keyId = Required(arguments, "--signing-key-id");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset? expiresAt = ReadExpiry(arguments, licenseType, now);
            string source = File.ReadAllText(sourcePath);

            (byte[] package, Yo4xStrategyManifest manifest) = Yo4xStrategyPacker.PackMql5Source(
                Path.GetFileName(sourcePath),
                source,
                aesKey,
                hmacKey,
                licenseToken: null,
                author: Option(arguments, "--author") ?? "YO4X Creator",
                description: "Proprietary strategy protected by the YO4X package boundary.",
                licenseIssuer: binding => LicenseAuthority.IssueLicenseToken(
                    new StrategyLicenseClaims(
                        Guid.CreateVersion7(),
                        tenantId,
                        userId,
                        binding.StrategyId,
                        binding.StrategyName,
                        licenseType,
                        [account],
                        [server],
                        now,
                        expiresAt,
                        maximumBots,
                        now,
                        binding.StrategyVersion,
                        binding.AssemblySha256,
                        keyId),
                    privateKey));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException("The output directory is invalid."));
            string staging = outputPath + ".stage-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(staging, package);
            try
            {
                File.Move(staging, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(staging))
                    File.Delete(staging);
            }

            Console.WriteLine($"package={outputPath}");
            Console.WriteLine($"strategyId={manifest.StrategyId}");
            Console.WriteLine($"assemblySha256={manifest.AssemblySha256}");
            Console.WriteLine($"packageSha256={Sha256(package)}");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static int PackPublication(string[] arguments)
    {
        string sourcePath = ExistingFile(Required(arguments, "--source"));
        string outputPath = Path.GetFullPath(Required(arguments, "--out"));
        string privateKey = File.ReadAllText(ExistingFile(Required(arguments, "--private-key")));
        byte[] aesKey = ReadSecretKey(Required(arguments, "--aes-key"));
        byte[] hmacKey = ReadSecretKey(Required(arguments, "--hmac-key"));
        try
        {
            string keyId = Required(arguments, "--signing-key-id");
            (byte[] package, Yo4xStrategyManifest manifest) = Yo4xStrategyPacker.PackMql5Source(
                Path.GetFileName(sourcePath),
                File.ReadAllText(sourcePath),
                aesKey,
                hmacKey,
                author: Option(arguments, "--author") ?? "YO4X Creator",
                description: Option(arguments, "--description")
                    ?? "Publisher-verified strategy protected by the YO4X package boundary.",
                publicationIssuer: binding => StrategyPublicationAuthority.Issue(
                    new StrategyPublicationClaims(
                        Guid.CreateVersion7(),
                        binding.StrategyId,
                        binding.StrategyName,
                        binding.StrategyVersion,
                        binding.AssemblySha256,
                        DateTimeOffset.UtcNow,
                        keyId),
                    privateKey));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException("The output directory is invalid."));
            string staging = outputPath + ".stage-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(staging, package);
                File.Move(staging, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }

            Console.WriteLine($"package={outputPath}");
            Console.WriteLine($"strategyId={manifest.StrategyId}");
            Console.WriteLine($"assemblySha256={manifest.AssemblySha256}");
            Console.WriteLine($"packageSha256={Sha256(package)}");
            Console.WriteLine("publication=verified-common-artifact");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static int Verify(string[] arguments)
    {
        byte[] package = File.ReadAllBytes(ExistingFile(Required(arguments, "--package")));
        byte[] aesKey = ReadSecretKey(Required(arguments, "--aes-key"));
        byte[] hmacKey = ReadSecretKey(Required(arguments, "--hmac-key"));
        try
        {
            var context = new StrategyLicenseValidationContext(
                Guid.Parse(Required(arguments, "--tenant")),
                Guid.Parse(Required(arguments, "--user")),
                Required(arguments, "--strategy-id"),
                Required(arguments, "--strategy-version"),
                Required(arguments, "--assembly-sha256"),
                ulong.Parse(Required(arguments, "--account"), CultureInfo.InvariantCulture),
                Required(arguments, "--server"),
                DateTimeOffset.UtcNow);
            (Yo4xStrategyManifest manifest, byte[] assembly) = Yo4xStrategyPackage.UnpackAndValidate(
                package,
                context,
                File.ReadAllText(ExistingFile(Required(arguments, "--public-key"))),
                aesKey,
                hmacKey);
            try
            {
                ValidateLoadableStrategy(manifest, assembly);
                Console.WriteLine($"verified={manifest.StrategyId}@{manifest.Version}");
                Console.WriteLine($"entryType={manifest.EntryTypeName}");
                Console.WriteLine($"packageSha256={Sha256(package)}");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(assembly);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static int SelfTestStraddle(string[] arguments)
    {
        string sourcePath = ExistingFile(Required(arguments, "--source"));
        byte[] aesKey = RandomNumberGenerator.GetBytes(32);
        byte[] hmacKey = RandomNumberGenerator.GetBytes(32);
        (string privateKey, string publicKey) = LicenseAuthority.GenerateMasterKeyPair();
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        const ulong account = 433470984;
        const string server = "Exness-MT5Trial7";
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string straddleSource = File.ReadAllText(sourcePath);
            (byte[] package, Yo4xStrategyManifest packagedManifest) = Yo4xStrategyPacker.PackMql5Source(
                Path.GetFileName(sourcePath),
                straddleSource,
                aesKey,
                hmacKey,
                licenseIssuer: binding => LicenseAuthority.IssueLicenseToken(
                    new StrategyLicenseClaims(
                        Guid.CreateVersion7(), tenantId, userId,
                        binding.StrategyId, binding.StrategyName, LicenseType.Trial,
                        [account], [server], now, now.AddHours(1), 1, now,
                        binding.StrategyVersion, binding.AssemblySha256, "self-test-p256"),
                    privateKey));

            var context = new StrategyLicenseValidationContext(
                tenantId, userId,
                packagedManifest.StrategyId,
                packagedManifest.Version,
                packagedManifest.AssemblySha256!,
                account, server, now.AddMinutes(1));
            (Yo4xStrategyManifest manifest, byte[] assembly) = Yo4xStrategyPackage.UnpackAndValidate(
                package, context, publicKey, aesKey, hmacKey);
            try
            {
                ValidateLoadableStrategy(manifest, assembly);
                string sourceText = straddleSource;
                Mql5RunOptions runOptions = StraddleRunOptions();
                (byte[] independentlyPacked, Yo4xStrategyManifest independentManifest) =
                    Yo4xStrategyPacker.PackMql5Source(
                        Path.GetFileName(sourcePath),
                        sourceText,
                        aesKey,
                        hmacKey,
                        licenseIssuer: binding => LicenseAuthority.IssueLicenseToken(
                            new StrategyLicenseClaims(
                                Guid.CreateVersion7(), tenantId, userId,
                                binding.StrategyId, binding.StrategyName, LicenseType.Trial,
                                [account], [server], now, now.AddHours(1), 1, now,
                                binding.StrategyVersion, binding.AssemblySha256, "self-test-p256"),
                            privateKey));
                (Yo4xStrategyManifest _, byte[] independentAssembly) =
                    Yo4xStrategyPackage.UnpackAndValidate(
                        independentlyPacked,
                        context with
                        {
                            StrategyId = independentManifest.StrategyId,
                            StrategyVersion = independentManifest.Version,
                            AssemblySha256 = independentManifest.AssemblySha256!
                        },
                        publicKey,
                        aesKey,
                        hmacKey);
                Mql5BacktestResult sourceResult;
                try
                {
                    sourceResult = Mql5BacktestRunner.RunCompiledAssembly(
                        independentAssembly,
                        independentManifest.EntryTypeName!,
                        StraddleFeed(),
                        runOptions,
                        periodMinutes: 1);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(independentAssembly);
                }
                var replayLog = new Mql5LogRecorder();
                Mql5BacktestResult packageResult = Mql5BacktestRunner.RunCompiledAssembly(
                    assembly,
                    manifest.EntryTypeName!,
                    StraddleFeed(),
                    runOptions,
                    periodMinutes: 1,
                    replayLog);
                if (sourceResult.Outcome != packageResult.Outcome
                    || sourceResult.Report is null
                    || packageResult.Report is null
                    || !string.Equals(
                        ReportDigest(sourceResult.Report),
                        ReportDigest(packageResult.Report),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The authenticated Straddle package does not preserve deterministic packaged-source behavior "
                        + $"(source={sourceResult.Outcome}/{(sourceResult.Report is null ? "none" : ReportDigest(sourceResult.Report))}, "
                        + $"package={packageResult.Outcome}/{(packageResult.Report is null ? "none" : ReportDigest(packageResult.Report))}).");
                }
                Console.WriteLine(
                    $"straddle-replay=outcome:{packageResult.Outcome},"
                    + $"init:{packageResult.Report.InitRetcode},"
                    + $"ticks:{packageResult.Report.TicksProcessed},"
                    + $"trades:{packageResult.Report.TotalTrades},"
                    + $"events:{packageResult.Report.Events.Count},"
                    + $"fault:{(packageResult.Report.StrategyFault.Length == 0 ? "none" : "present")}");
                if (packageResult.Report.TotalTrades == 0)
                {
                    foreach (Mql5LogEntry entry in replayLog.Entries.Where(entry =>
                        entry.Message.Contains("fail", StringComparison.OrdinalIgnoreCase)
                        || entry.Message.Contains("reject", StringComparison.OrdinalIgnoreCase)
                        || entry.Message.Contains("margin", StringComparison.OrdinalIgnoreCase)
                        || entry.Message.Contains("order", StringComparison.OrdinalIgnoreCase)
                        || entry.Message.Contains("grid", StringComparison.OrdinalIgnoreCase))
                        .DistinctBy(entry => entry.Message)
                        .Take(20))
                        Console.WriteLine($"straddle-log={entry.Channel}:{entry.Message}");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(assembly);
            }

            ExpectRejected(() => Yo4xStrategyPackage.UnpackAndValidate(
                package, context with { BrokerLogin = 999999999 }, publicKey, aesKey, hmacKey));
            ExpectRejected(() => Yo4xStrategyPackage.UnpackAndValidate(
                package, context with { BrokerServer = server + "-evil" }, publicKey, aesKey, hmacKey));
            byte[] tampered = (byte[])package.Clone();
            tampered[tampered.Length / 2] ^= 0x40;
            ExpectRejected(() => Yo4xStrategyPackage.UnpackAndValidate(
                tampered, context, publicKey, aesKey, hmacKey));

            (byte[] marketplacePackage, Yo4xStrategyManifest marketplaceManifest) =
                Yo4xStrategyPacker.PackMql5Source(
                    Path.GetFileName(sourcePath),
                    straddleSource,
                    aesKey,
                    hmacKey,
                    publicationIssuer: binding => StrategyPublicationAuthority.Issue(
                        new StrategyPublicationClaims(
                            Guid.CreateVersion7(),
                            binding.StrategyId,
                            binding.StrategyName,
                            binding.StrategyVersion,
                            binding.AssemblySha256,
                            now,
                            "self-test-publication-p256"),
                        privateKey));
            StrategyLicenseToken detachedLicense = LicenseAuthority.IssueLicenseToken(
                new StrategyLicenseClaims(
                    Guid.CreateVersion7(), tenantId, userId,
                    marketplaceManifest.StrategyId, marketplaceManifest.Name, LicenseType.Trial,
                    [account], [server], now, now.AddHours(1), 1, now,
                    marketplaceManifest.Version, marketplaceManifest.AssemblySha256,
                    "self-test-license-p256"),
                privateKey);
            var marketplaceContext = context with
            {
                StrategyId = marketplaceManifest.StrategyId,
                StrategyVersion = marketplaceManifest.Version,
                AssemblySha256 = marketplaceManifest.AssemblySha256!
            };
            (Yo4xStrategyManifest _, byte[] marketplaceAssembly) =
                Yo4xStrategyPackage.UnpackAndValidate(
                    marketplacePackage,
                    detachedLicense,
                    marketplaceContext,
                    publicKey,
                    publicKey,
                    aesKey,
                    hmacKey);
            CryptographicOperations.ZeroMemory(marketplaceAssembly);
            ExpectRejected(() => Yo4xStrategyPackage.UnpackAndValidate(
                marketplacePackage,
                detachedLicense,
                marketplaceContext with { BrokerLogin = account + 1 },
                publicKey,
                publicKey,
                aesKey,
                hmacKey));

            Console.WriteLine("straddle-v2-package-self-test=passed");
            Console.WriteLine("straddle-packaged-source-parity=passed");
            Console.WriteLine("marketplace-publication-detached-license-self-test=passed");
            Console.WriteLine($"packageSha256={Sha256(package)}");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static void ValidateLoadableStrategy(Yo4xStrategyManifest manifest, byte[] assemblyBytes)
    {
        string entryTypeName = manifest.EntryTypeName
            ?? throw new InvalidDataException("The package has no entry type.");
        var loadContext = new AssemblyLoadContext("yo4x-package-verifier", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
            Type type = assembly.GetType(entryTypeName, throwOnError: true, ignoreCase: false)
                ?? throw new InvalidDataException("The package entry type was not found.");
            if (type.IsAbstract || !typeof(YO4X.Mql5.Runtime.IMql5Strategy).IsAssignableFrom(type))
                throw new InvalidDataException("The package entry type is not an MQL5 strategy.");
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static Mql5SyntheticMarketFeed StraddleFeed() => new("XAUUSDm", 1136, 240)
    {
        StartPrice = 5000.0,
        Point = 0.01,
        VolatilityPoints = 25.0,
        PeriodMinutes = 1,
        SpreadPoints = 10,
        MinimumPrice = 100.0
    };

    private static Mql5RunOptions StraddleRunOptions() => new()
    {
        Symbol = new Mql5SymbolSpec
        {
            Name = "XAUUSDm",
            Digits = 2,
            ContractSize = 100.0,
            VolumeMin = 0.01,
            VolumeMax = 100.0,
            VolumeStep = 0.01
        },
        InitialDeposit = 10_000.0,
        Leverage = 500,
        MarginMode = Mql5MarginMode.Hedging,
        SpreadPoints = 10,
        InitialBid = 5000.0,
        MaxOrdersPerTick = 128,
        MaxPendingOrders = 256,
        MaxTicks = 240,
        Seed = 1136
    };

    private static string ReportDigest(Mql5RunReport report) => Sha256(
        JsonSerializer.SerializeToUtf8Bytes(report));

    private static void ExpectRejected(Func<object> action)
    {
        try
        {
            object result = action();
            if (result is ValueTuple<Yo4xStrategyManifest, byte[]> unpacked)
                CryptographicOperations.ZeroMemory(unpacked.Item2);
        }
        catch (Exception exception) when (exception is LicenseValidationException
            or CryptographicException
            or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("A negative package test was unexpectedly accepted.");
    }

    private static DateTimeOffset? ReadExpiry(string[] arguments, LicenseType type, DateTimeOffset now)
    {
        string? daysText = Option(arguments, "--expires-days");
        if (daysText is null)
        {
            if (type is LicenseType.Subscription or LicenseType.Trial)
                throw new ArgumentException("Subscription and trial licenses require --expires-days.");
            return null;
        }

        int days = int.Parse(daysText, CultureInfo.InvariantCulture);
        if (days is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(arguments), "Expiry days must be between 1 and 3650.");
        return now.AddDays(days);
    }

    private static byte[] ReadSecretKey(string path)
    {
        string encoded = File.ReadAllText(ExistingFile(path)).Trim();
        byte[] value = Convert.FromBase64String(encoded);
        if (value.Length != 32 || Convert.ToBase64String(value) != encoded)
        {
            CryptographicOperations.ZeroMemory(value);
            throw new InvalidDataException("A package key file must contain canonical Base64 for exactly 32 bytes.");
        }
        return value;
    }

    private static string Required(string[] arguments, string name) =>
        Option(arguments, name) ?? throw new ArgumentException($"Missing {name}.");

    private static string? Option(string[] arguments, string name)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                return arguments[index + 1];
        }
        return null;
    }

    private static string ExistingFile(string value)
    {
        string path = Path.GetFullPath(value);
        return File.Exists(path) ? path : throw new FileNotFoundException("A required file was not found.", path);
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static int Usage()
    {
        Console.Error.WriteLine("Use pack, verify, or self-test-straddle. Keys are accepted only through files.");
        return 1;
    }
}
