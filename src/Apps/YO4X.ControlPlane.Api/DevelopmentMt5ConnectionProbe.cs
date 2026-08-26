using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace YO4X.ControlPlane.Api;

public sealed record DevelopmentMt5ConnectionProbeObservation(
    string AccountMode,
    string Environment,
    string TradingAccess,
    string Currency,
    bool DisconnectConfirmed,
    DateTimeOffset ObservedAtUtc);

public sealed record DevelopmentMt5ConnectionProbeResult(
    int SchemaVersion,
    bool IsSuccess,
    string Code,
    DevelopmentMt5ConnectionProbeObservation? Observation);

public interface IDevelopmentMt5ConnectionProbe
{
    Task<DevelopmentMt5ConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

public static class DevelopmentMt5ConnectionProbeRegistration
{
    private const string SectionName = "DevelopmentMt5ConnectionProbe";

    public static IServiceCollection AddDevelopmentMt5ConnectionProbe(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        IConfigurationSection section = configuration.GetSection(SectionName);
        bool enabled = section.GetValue<bool>("Enabled");
        if (!enabled)
        {
            return services;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The direct MT5 connection probe can be enabled only in Development.");
        }

        DevelopmentMt5ConnectionProbeOptions options =
            DevelopmentMt5ConnectionProbeOptions.Load(section);
        services.TryAddSingleton(options);
        services.TryAddSingleton<IDevelopmentMt5ConnectionProbe, DevelopmentMt5ConnectionProbe>();
        return services;
    }

    public static RouteGroupBuilder MapDevelopmentMt5ConnectionProbe(
        this RouteGroupBuilder user,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment()
            || !configuration.GetSection(SectionName).GetValue<bool>("Enabled"))
        {
            return user;
        }

        user.MapPost("/development/mt5-connection-probe", async (
            HttpContext context,
            IDevelopmentMt5ConnectionProbe probe,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (context.Connection.RemoteIpAddress is null
                || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                return Results.NotFound();
            }

            DevelopmentMt5ConnectionProbeResult result =
                await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        return user;
    }
}

internal sealed record DevelopmentMt5ConnectionProbeOptions(
    string CanaryPath,
    string CanarySha256,
    Guid BrokerAccountId,
    string CredentialKey,
    Guid ArtifactId,
    string ArtifactSha256,
    string ArtifactPath,
    string VaultRoot,
    string BrokerCompany,
    string ServerName,
    string Host,
    int Port,
    string WorkerPath,
    string WorkerSha256,
    string ManifestPath,
    string ManifestSha256,
    TimeSpan Timeout)
{
    public static DevelopmentMt5ConnectionProbeOptions Load(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        string Required(string name) => string.IsNullOrWhiteSpace(section[name])
            ? throw new InvalidOperationException($"Development MT5 probe setting {name} is required.")
            : section[name]!.Trim();
        string FullPath(string name)
        {
            string value = Required(name);
            if (!Path.IsPathFullyQualified(value))
            {
                throw new InvalidOperationException(
                    $"Development MT5 probe setting {name} must be an absolute path.");
            }

            return Path.GetFullPath(value);
        }

        Guid ExactGuid(string name)
        {
            Guid value = Guid.Parse(Required(name));
            return value == Guid.Empty
                ? throw new InvalidOperationException(
                    $"Development MT5 probe setting {name} cannot be empty.")
                : value;
        }

        string ExactSha(string name)
        {
            string value = Required(name);
            if (value.Length != 64
                || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            {
                throw new InvalidOperationException(
                    $"Development MT5 probe setting {name} requires a lowercase SHA-256.");
            }

            return value;
        }

        string Bounded(string name)
        {
            string value = Required(name);
            if (value.Length > 255 || value.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    $"Development MT5 probe setting {name} is invalid.");
            }

            return value;
        }

        if (!int.TryParse(Required("Port"), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException("Development MT5 probe setting Port is invalid.");
        }

        if (!int.TryParse(Required("TimeoutMilliseconds"), NumberStyles.None, CultureInfo.InvariantCulture, out int timeout)
            || timeout is < 500 or > 30_000)
        {
            throw new InvalidOperationException(
                "Development MT5 probe setting TimeoutMilliseconds is invalid.");
        }

        return new DevelopmentMt5ConnectionProbeOptions(
            FullPath("CanaryPath"),
            ExactSha("CanarySha256"),
            ExactGuid("BrokerAccountId"),
            ExactSha("CredentialKey"),
            ExactGuid("ArtifactId"),
            ExactSha("ArtifactSha256"),
            FullPath("ArtifactPath"),
            FullPath("VaultRoot"),
            Bounded("BrokerCompany"),
            Bounded("ServerName"),
            Bounded("Host"),
            port,
            FullPath("WorkerPath"),
            ExactSha("WorkerSha256"),
            FullPath("ManifestPath"),
            ExactSha("ManifestSha256"),
            TimeSpan.FromMilliseconds(timeout));
    }

    public override string ToString() =>
        "DevelopmentMt5ConnectionProbeOptions { Credential = [REDACTED], Endpoint = [REDACTED], Paths = [REDACTED] }";
}

internal sealed class DevelopmentMt5ConnectionProbe(
    DevelopmentMt5ConnectionProbeOptions options) : IDevelopmentMt5ConnectionProbe, IDisposable
{
    private const int MaximumOutputCharacters = 16 * 1024;
    private readonly SemaphoreSlim operation = new(1, 1);

    public async Task<DevelopmentMt5ConnectionProbeResult> ProbeAsync(
        CancellationToken cancellationToken)
    {
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Failure("mt5_connect_probe_busy");
        }

        try
        {
            return await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure("mt5_connect_probe_failed");
        }
        finally
        {
            operation.Release();
        }
    }

    private async Task<DevelopmentMt5ConnectionProbeResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout + TimeSpan.FromSeconds(2));

        await using FileStream pinnedCanary = new(
            options.CanaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        string actualSha256 = Convert.ToHexString(
            await SHA256.HashDataAsync(pinnedCanary, deadline.Token).ConfigureAwait(false))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualSha256),
                Convert.FromHexString(options.CanarySha256)))
        {
            return Failure("mt5_connect_probe_unavailable");
        }

        using var process = new Process { StartInfo = CreateStartInfo() };
        if (!process.Start())
        {
            return Failure("mt5_connect_probe_unavailable");
        }

        try
        {
            string output = await ReadBoundedAsync(
                process.StandardOutput,
                deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return ParseOutput(output, process.ExitCode);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(options.CanaryPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        Add("broker-account-id", options.BrokerAccountId.ToString("D"));
        Add("credential-key", options.CredentialKey);
        Add("artifact-id", options.ArtifactId.ToString("D"));
        Add("artifact-sha256", options.ArtifactSha256);
        Add("artifact-path", options.ArtifactPath);
        Add("vault-root", options.VaultRoot);
        Add("broker-company", options.BrokerCompany);
        Add("server-name", options.ServerName);
        Add("host", options.Host);
        Add("port", options.Port.ToString(CultureInfo.InvariantCulture));
        Add("worker-path", options.WorkerPath);
        Add("worker-sha256", options.WorkerSha256);
        Add("manifest-path", options.ManifestPath);
        Add("manifest-sha256", options.ManifestSha256);
        Add("timeout-ms", ((int)options.Timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        return info;

        void Add(string name, string value)
        {
            info.ArgumentList.Add("--" + name);
            info.ArgumentList.Add(value);
        }
    }

    private DevelopmentMt5ConnectionProbeResult ParseOutput(string output, int exitCode)
    {
        using JsonDocument document = JsonDocument.Parse(output, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        JsonElement root = document.RootElement;
        int schemaVersion = Property(root, "SchemaVersion").GetInt32();
        bool isSuccess = Property(root, "IsSuccess").GetBoolean();
        string code = Property(root, "Code").GetString() ?? string.Empty;
        if (schemaVersion != 1
            || exitCode != (isSuccess ? 0 : 1)
            || code is not ("mt5_connect_probe_succeeded"
                or "mt5_connect_probe_rejected"
                or "mt5_connect_probe_failed"
                or "mt5_connect_probe_unavailable"))
        {
            return Failure("mt5_connect_probe_failed");
        }

        if (!isSuccess)
        {
            return Failure(code);
        }

        JsonElement observation = Property(root, "Observation");
        if (observation.ValueKind != JsonValueKind.Object
            || Property(observation, "BrokerAccountId").GetGuid() != options.BrokerAccountId
            || Property(observation, "GatewayArtifactId").GetGuid() != options.ArtifactId
            || !string.Equals(
                Property(observation, "GatewayArtifactSha256").GetString(),
                options.ArtifactSha256,
                StringComparison.Ordinal)
            || !string.Equals(
                Property(observation, "BrokerCompany").GetString(),
                options.BrokerCompany,
                StringComparison.Ordinal)
            || !string.Equals(
                Property(observation, "ServerName").GetString(),
                options.ServerName,
                StringComparison.Ordinal)
            || !Property(observation, "DisconnectConfirmed").GetBoolean())
        {
            return Failure("mt5_connect_probe_failed");
        }

        string accountMode = AccountModeText(Property(observation, "AccountMode"));
        string environment = EnvironmentText(Property(observation, "Environment"));
        string tradingAccess = TradingAccessText(Property(observation, "TradingAccess"));
        string currency = Property(observation, "Currency").GetString() ?? string.Empty;
        if (accountMode == "UNKNOWN"
            || environment != "DEMO"
            || currency.Length is < 1 or > 16
            || currency.Any(char.IsControl)
            || !string.Equals(currency, currency.Trim(), StringComparison.Ordinal))
        {
            return Failure("mt5_connect_probe_failed");
        }

        return new DevelopmentMt5ConnectionProbeResult(
            1,
            true,
            code,
            new DevelopmentMt5ConnectionProbeObservation(
                accountMode,
                environment,
                tradingAccess,
                currency,
                true,
                Property(observation, "ObservedAtUtc").GetDateTimeOffset()));
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value)
            || element.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out value))
        {
            return value;
        }

        throw new JsonException("The connection-probe output contract is invalid.");
    }

    private static string AccountModeText(JsonElement value) => ReadEnumValue(value) switch
    {
        "0" or "UNKNOWN" => "UNKNOWN",
        "1" or "HEDGING" => "HEDGING",
        "2" or "NETTING" => "NETTING",
        "3" or "EXCHANGE" => "EXCHANGE",
        _ => throw new JsonException("The account-mode observation is invalid.")
    };

    private static string EnvironmentText(JsonElement value) => ReadEnumValue(value) switch
    {
        "0" or "UNKNOWN" => "UNKNOWN",
        "1" or "DEMO" => "DEMO",
        "2" or "LIVE" => "LIVE",
        "3" or "CONTEST" => "CONTEST",
        "4" or "ARCHIVED" => "ARCHIVED",
        _ => throw new JsonException("The environment observation is invalid.")
    };

    private static string TradingAccessText(JsonElement value) => ReadEnumValue(value) switch
    {
        "0" or "UNKNOWN" => "UNKNOWN",
        "1" or "READONLY" or "READ_ONLY" => "READ_ONLY",
        "2" or "TRADINGALLOWED" or "TRADING_ALLOWED" => "TRADING_ALLOWED",
        "3" or "TRADINGBLOCKED" or "TRADING_BLOCKED" => "TRADING_BLOCKED",
        _ => throw new JsonException("The trading-access observation is invalid.")
    };

    private static string ReadEnumValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetInt32().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.String => (value.GetString() ?? string.Empty).ToUpperInvariant(),
        _ => throw new JsonException("The enum observation is invalid.")
    };

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var output = new char[MaximumOutputCharacters + 1];
        int count = 0;
        while (count < output.Length)
        {
            int read = await reader.ReadAsync(output.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return new string(output, 0, count);
            }

            count += read;
        }

        throw new InvalidDataException("The connection-probe output exceeded its bound.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between inspection and termination.
        }
    }

    private static DevelopmentMt5ConnectionProbeResult Failure(string code) =>
        new(1, false, code, null);

    public void Dispose() => operation.Dispose();
}
