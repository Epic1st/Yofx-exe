#nullable enable
using System.Net.Http;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YO4X.StrategyGovernance.Licensing;

namespace YO4X.Desktop;

internal sealed record DesktopAuthorizedBot(
    string Id,
    string Name,
    string StrategyId,
    string StrategyName,
    string BrokerAccountId,
    string MaskedLogin,
    string Login,
    string Server,
    string BindingFingerprint,
    string Symbol,
    string RiskLabel,
    string PackageSha256);

internal sealed record DesktopExecutionBundleWire(
    string ExecutionId,
    string ExecutionToken,
    DateTimeOffset ExpiresAt,
    DesktopAuthorizedBot Bot,
    string PackageBase64,
    string PackageSha256,
    string AesKeyBase64,
    string HmacKeyBase64,
    string PublicationPublicKeyPem,
    string LicensePublicKeyPem,
    StrategyLicenseToken License);

internal sealed class DesktopExecutionBundle(
    Guid executionId,
    string executionToken,
    DateTimeOffset expiresAt,
    DesktopBotInstance bot,
    ulong login,
    string bindingFingerprint,
    byte[] package,
    string packageSha256,
    byte[] aesKey,
    byte[] hmacKey,
    string publicationPublicKeyPem,
    string licensePublicKeyPem,
    StrategyLicenseToken license) : IDisposable
{
    internal Guid ExecutionId { get; } = executionId;
    internal string ExecutionToken { get; } = executionToken;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    internal DesktopBotInstance Bot { get; } = bot;
    internal ulong Login { get; } = login;
    internal string BindingFingerprint { get; } = bindingFingerprint;
    internal byte[] Package { get; } = package;
    internal string PackageSha256 { get; } = packageSha256;
    internal byte[] AesKey { get; } = aesKey;
    internal byte[] HmacKey { get; } = hmacKey;
    internal string PublicationPublicKeyPem { get; } = publicationPublicKeyPem;
    internal string LicensePublicKeyPem { get; } = licensePublicKeyPem;
    internal StrategyLicenseToken License { get; } = license;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Package);
        CryptographicOperations.ZeroMemory(AesKey);
        CryptographicOperations.ZeroMemory(HmacKey);
    }
}

internal sealed class DesktopControlPlaneRuntime(
    Uri origin,
    string accessToken,
    string? developmentCertificateSha256) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static DesktopControlPlaneRuntime()
    {
        Json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
    }
    private readonly HttpClient client = CreateClient(origin, accessToken, developmentCertificateSha256);

    internal async Task<DesktopExecutionBundle> AcquireAsync(Guid botId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(origin, $"/v1/bots/{botId:D}/local-execution-bundles"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await SafeFailureAsync(response, cancellationToken).ConfigureAwait(false));
        DesktopExecutionBundleWire wire = await JsonSerializer.DeserializeAsync<DesktopExecutionBundleWire>(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), Json, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Control Plane returned an empty execution bundle.");

        if (!Guid.TryParse(wire.ExecutionId, out Guid executionId)
            || !Guid.TryParse(wire.Bot.Id, out Guid returnedBotId)
            || returnedBotId != botId
            || !Guid.TryParse(wire.Bot.StrategyId, out _)
            || !Guid.TryParse(wire.Bot.BrokerAccountId, out _)
            || !DesktopLocalRuntime.TryParseLogin(wire.Bot.Login, out ulong login)
            || wire.ExecutionToken.Length is < 32 or > 256
            || wire.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("Control Plane returned an invalid execution binding.");
        }

        byte[] package = Convert.FromBase64String(wire.PackageBase64);
        byte[] aes = Convert.FromBase64String(wire.AesKeyBase64);
        byte[] hmac = Convert.FromBase64String(wire.HmacKeyBase64);
        try
        {
            string packageSha = Convert.ToHexStringLower(SHA256.HashData(package));
            if (aes.Length != 32 || hmac.Length != 32
                || !FixedTimeHexEquals(packageSha, wire.PackageSha256)
                || !FixedTimeHexEquals(packageSha, wire.Bot.PackageSha256))
            {
                throw new CryptographicException("The authorized strategy package binding is invalid.");
            }

            var bot = new DesktopBotInstance(
                wire.Bot.Id, wire.Bot.Name, wire.Bot.StrategyId, wire.Bot.StrategyName,
                wire.Bot.BrokerAccountId, wire.Bot.MaskedLogin, wire.Bot.Symbol,
                wire.Bot.RiskLabel, "STARTING", "LOCAL", null, null, [],
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            {
                Server = wire.Bot.Server,
                Timeframe = "M1"
            };
            var bundle = new DesktopExecutionBundle(
                executionId, wire.ExecutionToken, wire.ExpiresAt, bot, login,
                wire.Bot.BindingFingerprint, package, packageSha, aes, hmac,
                wire.PublicationPublicKeyPem, wire.LicensePublicKeyPem, wire.License);
            package = [];
            aes = [];
            hmac = [];
            return bundle;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(package);
            CryptographicOperations.ZeroMemory(aes);
            CryptographicOperations.ZeroMemory(hmac);
        }
    }

    internal async Task ReportAsync(
        Guid executionId,
        string executionToken,
        string state,
        string? error,
        CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            token = executionToken,
            state,
            error
        }, Json);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(origin, $"/v1/local-executions/{executionId:D}/state"))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Control Plane rejected the local runtime heartbeat.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    public void Dispose() => client.Dispose();

    private static HttpClient CreateClient(Uri origin, string accessToken, string? certificateSha256)
    {
        var handler = new HttpClientHandler();
        if (origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(certificateSha256))
        {
            string expected = certificateSha256.Trim().Replace(":", string.Empty).ToUpperInvariant();
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                ValidateDevelopmentCertificate(certificate, errors, expected);
        }
        var client = new HttpClient(handler)
        {
            BaseAddress = origin,
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static bool ValidateDevelopmentCertificate(
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        string expectedSha256)
    {
        if (certificate is null)
            return false;
        string actual = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        return actual.Length == expectedSha256.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedSha256));
    }

    private static bool FixedTimeHexEquals(string left, string right) =>
        left.Length == right.Length && left.Length == 64
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(right.ToLowerInvariant()));

    private static async Task<string> SafeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("title", out JsonElement title))
                return title.GetString() ?? "Control Plane rejected local execution.";
        }
        catch (JsonException)
        {
        }
        return "Control Plane rejected local execution.";
    }
}
