using YO4X.Mt5.ConnectionProbe.Windows;

try
{
    Mt5ConnectionProbeWorkerConfiguration configuration =
        Mt5ConnectionProbeWorkerConfiguration.LoadFromEnvironment();
    var server = Mt5ConnectionProbeWorkerComposition.CreateServer(configuration);
    return await server.RunOnceAsync(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        CancellationToken.None);
}
catch
{
    // Configuration, artifact, and endpoint failures remain silent because this
    // process shares stdout exclusively with authenticated protocol frames.
    return 78;
}
