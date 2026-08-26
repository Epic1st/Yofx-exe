using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

if (args is ["--yo4x-descendant-hang"])
{
    await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
    return 98;
}

Stream input = Console.OpenStandardInput();
Stream output = Console.OpenStandardOutput();
byte[]? sessionKey = null;
byte[]? requestPayload = null;
byte[]? responsePayload = null;
try
{
    sessionKey = await BrokerProcessProtocol.ReadBootstrapAsync(
        input,
        CancellationToken.None);
    requestPayload = await BrokerProcessProtocol.ReadRequestAsync(
        input,
        sessionKey,
        BrokerProcessProtocol.DefaultMaximumRequestBytes,
        CancellationToken.None);
    BrokerWorkerRequest request = BrokerProcessProtocol.DeserializeRequest(requestPayload);
    BrokerWorkerContractValidator.ValidateRequest(request, DateTimeOffset.UtcNow);

    string mode = request.Send?.Command.Symbol
        ?? request.ConnectProbe?.Server.ServerName
        ?? "reconcile";
    if (mode == "__YO4X_TEST_HANG__")
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return 99;
    }

    if (mode == "__YO4X_TEST_DESCENDANT_HANG__")
    {
        string markerPath = request.Send?.Command.TargetBrokerId
            ?? throw new InvalidOperationException("The descendant marker is missing.");
        await SpawnDescendantAsync(markerPath);
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return 99;
    }

    if (mode == "__YO4X_TEST_MALFORMED__")
    {
        responsePayload = Encoding.UTF8.GetBytes("{\"contractVersion\":1}");
        await BrokerProcessProtocol.WriteTestResponseAsync(
            output,
            responsePayload,
            sessionKey,
            corruptAuthenticationTag: false,
            CancellationToken.None);
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return 99;
    }

    BrokerWorkerResponse response = CreateResponse(request);
    responsePayload = BrokerProcessProtocol.SerializeResponse(
        response,
        BrokerProcessProtocol.DefaultMaximumResponseBytes);
    await BrokerProcessProtocol.WriteTestResponseAsync(
        output,
        responsePayload,
        sessionKey,
        corruptAuthenticationTag: mode == "__YO4X_TEST_BAD_AUTH__",
        CancellationToken.None);
    if (mode == "__YO4X_TEST_BAD_AUTH__")
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return 99;
    }

    return 0;
}
catch
{
    return 70;
}
finally
{
    Zero(requestPayload);
    Zero(responsePayload);
    Zero(sessionKey);
}

static BrokerWorkerResponse CreateResponse(BrokerWorkerRequest request)
{
    if (request.Operation == BrokerWorkerProtocolContract.SendOperation)
    {
        var result = new GatewaySendResult(
            GatewayCommandDisposition.SubmissionDisabled,
            "test_worker_submission_disabled",
            null,
            null,
            null,
            request.Send!.Command.CreatedAtUtc,
            true);
        return new BrokerWorkerResponse(
            BrokerWorkerProtocolContract.Version,
            request.RequestId,
            request.Operation,
            false,
            result.Code,
            result,
            null);
    }

    if (request.Operation == BrokerWorkerProtocolContract.ConnectProbeOperation)
    {
        return new BrokerWorkerResponse(
            BrokerWorkerProtocolContract.Version,
            request.RequestId,
            request.Operation,
            false,
            BrokerWorkerProtocolContract.ConnectProbeUnavailableCode,
            null,
            null);
    }

    return new BrokerWorkerResponse(
        BrokerWorkerProtocolContract.Version,
        request.RequestId,
        request.Operation,
        false,
        "test_worker_reconciliation_unavailable",
        null,
        null);
}

static async Task SpawnDescendantAsync(string markerPath)
{
    string executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The test worker executable is unavailable.");
    var startInfo = new ProcessStartInfo
    {
        FileName = executablePath,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("--yo4x-descendant-hang");
    using Process descendant = Process.Start(startInfo)
        ?? throw new InvalidOperationException("The test descendant did not start.");
    descendant.StandardInput.Close();
    await File.WriteAllTextAsync(
        markerPath,
        descendant.Id.ToString(CultureInfo.InvariantCulture),
        CancellationToken.None);
}

static void Zero(byte[]? value)
{
    if (value is not null)
    {
        CryptographicOperations.ZeroMemory(value);
    }
}
