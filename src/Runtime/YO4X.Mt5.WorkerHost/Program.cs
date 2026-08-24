using YO4X.Trading.Mt5;
using YO4X.Trading.ProcessIsolation;

var server = new AuthenticatedBrokerWorkerServer(
    new Mt5ProofOnlyBrokerWorkerExecutor());
return await server.RunOnceAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    CancellationToken.None);
