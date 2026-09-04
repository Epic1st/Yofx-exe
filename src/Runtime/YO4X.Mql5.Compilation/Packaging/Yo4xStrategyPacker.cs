using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YO4X.Mql5.CodeGen;
using YO4X.StrategyGovernance;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.Mql5.Compilation.Packaging;

public sealed record Yo4xLicenseBinding(
    string StrategyId,
    string StrategyName,
    string StrategyVersion,
    string AssemblySha256);

public static class Yo4xStrategyPacker
{
    private static string SanitizeMql5Source(string source)
    {
        // 1. Replace Windows OS DLL import blocks with safe in-memory C# compatible stubs
        source = Regex.Replace(
            source,
            @"#import\s+""(shell32|user32|kernel32|gdi32|wininet)\.dll"".*?#import",
            "int ShellExecuteW(int hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd) { return 0; }",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2. Strip single-line DLL imports if any
        source = Regex.Replace(
            source,
            @"#import\s+""(shell32|user32|kernel32|gdi32|wininet)\.dll""",
            "// [YO4X-DRM] Native DLL import removed.",
            RegexOptions.IgnoreCase);

        // 3. Convert multi-statement macro LogFmt into standard MQL5 inline function
        source = Regex.Replace(
            source,
            @"#define\s+LogFmt\([^\)]+\)\s*\{[^\}]+\}",
            "void LogFmt(int lvl, string fmtExpr) { if(lvl <= EffectiveLogLevel()) Log(lvl, fmtExpr); }",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 4. Pre-expand macro string literal concatenation (e.g. #define STR_UI_SCAN STR_UI_PREFIX "SCAN")
        source = Regex.Replace(
            source,
            @"#define\s+([A-Za-z0-9_]+)\s+STR_UI_PREFIX\s*""([^""]*)""",
            @"#define $1 ""STR_UI_$2""");

        source = Regex.Replace(
            source,
            @"#define\s+([A-Za-z0-9_]+)\s+([A-Za-z0-9_]+)\s+""([^""]*)""",
            @"#define $1 ""$3""");

        // 5. Inject built-in MetaTrader enum constants if missing
        source = @"
#define COLOR_FORMAT_XRGB_NOALPHA 0
#define COLOR_FORMAT_ARGB_RAW 1
#define COLOR_FORMAT_ARGB_NORMALIZE 2
#define LOG_LEVEL_NO 0
#define LOG_LEVEL_ERRORS 1
#define LOG_LEVEL_ALL 2
" + source;

        return source;
    }

    public static (byte[] PackageBytes, Yo4xStrategyManifest Manifest) PackMql5Source(
        string strategyName,
        string mq5SourceText,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> hmacKey,
        StrategyLicenseToken? licenseToken = null,
        string author = "YO4X Creator",
        string description = "Proprietary algorithmic strategy compiled and protected by YO4X DRM.",
        Func<Yo4xLicenseBinding, StrategyLicenseToken>? licenseIssuer = null,
        Func<Yo4xLicenseBinding, StrategyPublicationToken>? publicationIssuer = null,
        string strategyVersion = "1.0.0",
        IReadOnlyList<string>? supportedSymbols = null,
        IReadOnlyList<string>? supportedTimeframes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mq5SourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyVersion);
        if (strategyVersion.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(strategyVersion));

        string sanitizedText = SanitizeMql5Source(mq5SourceText);
        byte[] sourceBytes = Encoding.UTF8.GetBytes(sanitizedText);
        var sourceDoc = new Mql5SourceDocument(strategyName, sourceBytes);

        // 1. FrontEnd Parse & Lower
        var feResult = Mql5FrontEnd.Compile(sourceDoc);
        if (!feResult.Succeeded || feResult.Module is null)
        {
            var firstErr = feResult.Diagnostics.FirstOrDefault(d => d.Severity == Mql5RestrictedDiagnosticSeverity.Error);
            throw new InvalidOperationException($"MQL5 parse failed: {firstErr?.Message ?? "Unknown error"} (Line {firstErr?.Line})");
        }

        // 2. Extract Declared Inputs for Manifest
        var parameterList = new List<StrategyParameterInfo>();
        foreach (var inp in feResult.Module.Inputs)
        {
            parameterList.Add(new StrategyParameterInfo(
                inp.Name,
                inp.Type?.Name ?? "double",
                inp.CanonicalDefault ?? inp.DefaultValue?.ToString() ?? "",
                inp.Label ?? ""));
        }

        // 3. CodeGen C#
        var codeGenResult = Mql5CodeGenerator.Generate(feResult.Module, null!);
        if (!codeGenResult.Succeeded || string.IsNullOrEmpty(codeGenResult.CSharpSource))
        {
            var firstErr = codeGenResult.Diagnostics.FirstOrDefault(d => d.Severity == Mql5RestrictedDiagnosticSeverity.Error);
            throw new InvalidOperationException($"C# CodeGen failed: {firstErr?.Message ?? "Unknown error"}");
        }

        // 4. Compile to Roslyn In-Memory Assembly
        var compHost = new RoslynMql5CompilationHost();
        var generatedSource = new Mql5GeneratedSource(strategyName + ".cs", codeGenResult.CSharpSource, codeGenResult.FullTypeName);
        var compResult = compHost.Compile(strategyName, [generatedSource]);

        if (!compResult.Succeeded || compResult.AssemblyBytes is null)
        {
            var lines = codeGenResult.CSharpSource.Split('\n');
            var errorDetails = new StringBuilder();
            foreach (var err in compResult.Diagnostics.Where(d => d.Severity == Mql5RestrictedDiagnosticSeverity.Error))
            {
                int l = err.Line - 1;
                string snippet = (l >= 0 && l < lines.Length) ? lines[l].Trim() : "";
                errorDetails.Append(CultureInfo.InvariantCulture, $"Line {err.Line}: {err.Message}\n   Snippet: {snippet}\n");
            }

            throw new InvalidOperationException($"Roslyn compilation failed:\n{errorDetails}");
        }

        // 5. Bind the signed license to the exact emitted assembly.
        string cleanId = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant()[..16];
        string assemblySha256 = Convert.ToHexString(SHA256.HashData(compResult.AssemblyBytes))
            .ToLowerInvariant();
        string cleanName = Path.GetFileNameWithoutExtension(strategyName);
        if (licenseIssuer is not null)
        {
            if (licenseToken is not null)
                throw new ArgumentException("Supply either a license token or a license issuer, not both.");
            licenseToken = licenseIssuer(new Yo4xLicenseBinding(
                cleanId,
                cleanName,
                strategyVersion,
                assemblySha256));
        }

        StrategyPublicationToken? publicationToken = null;
        if (publicationIssuer is not null)
        {
            if (licenseToken is not null)
                throw new ArgumentException("A package cannot embed both a user license and a marketplace publication.");
            publicationToken = publicationIssuer(new Yo4xLicenseBinding(
                cleanId,
                cleanName,
                strategyVersion,
                assemblySha256));
        }

        if (licenseToken is null && publicationToken is null)
        {
            var (privKey, _) = LicenseAuthority.GenerateMasterKeyPair();
            var claims = new StrategyLicenseClaims(
                LicenseId: Guid.NewGuid(),
                TenantId: Guid.NewGuid(),
                UserId: Guid.NewGuid(),
                StrategyId: cleanId,
                StrategyName: cleanName,
                LicenseType: LicenseType.Lifetime,
                BoundAccounts: new List<ulong> { 433470984 },
                BoundServers: new List<string> { "Exness-MT5Trial7", "Exness-Real", "VantageInternational-Live", "MetaQuotes-Demo" },
                IssuedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddYears(10),
                MaxConcurrentBots: 100,
                StrategyVersion: strategyVersion,
                AssemblySha256: assemblySha256);
            licenseToken = LicenseAuthority.IssueLicenseToken(claims, privKey);
        }

        if ((licenseToken is null) == (publicationToken is null))
            throw new InvalidOperationException("Exactly one signed license or marketplace publication is required for a .yo4x v2 package.");

        // 6. Build Manifest
        var manifest = new Yo4xStrategyManifest(
            cleanId,
            cleanName,
            description,
            strategyVersion,
            author,
            parameterList,
            supportedSymbols ?? ["XAUUSDm", "XAUUSD", "EURUSD", "GBPUSD", "BTCUSD"],
            supportedTimeframes ?? ["M1", "M5", "M15", "H1", "D1"],
            licenseToken,
            codeGenResult.FullTypeName,
            assemblySha256,
            publicationToken);

        // 7. Encrypt and Pack Container
        byte[] packageBytes = Yo4xStrategyPackage.Pack(manifest, compResult.AssemblyBytes, aesKey, hmacKey);

        return (packageBytes, manifest);
    }
}
