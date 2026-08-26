using System.Text.Json;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.DemoCanary;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

try
{
    DemoCanaryOptions options = DemoCanaryOptions.Parse(args);
    var vault = new DpapiLocalMt5CredentialVault(options.WorkerConfiguration.VaultRoot);
    string vaultIdentitySha256 = await vault.GetEvidenceBindingAsync(CancellationToken.None);
    var client = new IsolatedBrokerConnectionProbeClient(
        options.ProcessOptions,
        options.CreateWorkerEnvironment());
    GatewayOperationResult<BrokerConnectionProbeObservation> result =
        await client.ProbeAsync(
            options.CreateProbe(DateTimeOffset.UtcNow, vaultIdentitySha256),
            CancellationToken.None);
    Console.Out.WriteLine(JsonSerializer.Serialize(new DemoCanaryOutput(
        1,
        result.IsSuccess,
        result.Code,
        result.Value)));
    return result.IsSuccess ? 0 : 1;
}
catch
{
    Console.Out.WriteLine(JsonSerializer.Serialize(new DemoCanaryOutput(
        1,
        false,
        "demo_canary_failed",
        null)));
    return 70;
}

internal sealed record DemoCanaryOutput(
    int SchemaVersion,
    bool IsSuccess,
    string Code,
    BrokerConnectionProbeObservation? Observation);
