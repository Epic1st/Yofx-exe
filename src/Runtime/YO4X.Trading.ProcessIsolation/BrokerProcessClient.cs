using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace YO4X.Trading.ProcessIsolation;

internal interface IBrokerProcessObserver
{
    void OnStarted(int processId);

    void OnTerminated(int processId, bool processTreeKillRequested);
}

internal interface IBrokerProcessLaunchCheckpoint
{
    ValueTask DuringLaunchClosureVerificationAsync(CancellationToken cancellationToken);
}

internal sealed class BrokerProcessBoundaryException : Exception
{
    internal BrokerProcessBoundaryException(string code, bool processStarted)
        : base("The isolated broker worker failed closed.")
    {
        Code = code;
        ProcessStarted = processStarted;
    }

    internal string Code { get; }

    internal bool ProcessStarted { get; }
}

internal sealed class BrokerProcessClient
{
    private readonly IsolatedBrokerProcessOptions options;
    private readonly TimeProvider timeProvider;
    private readonly IBrokerProcessObserver? observer;
    private readonly IBrokerProcessLaunchCheckpoint? launchCheckpoint;

    internal BrokerProcessClient(
        IsolatedBrokerProcessOptions options,
        TimeProvider? timeProvider = null,
        IBrokerProcessObserver? observer = null,
        IBrokerProcessLaunchCheckpoint? launchCheckpoint = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.observer = observer;
        this.launchCheckpoint = launchCheckpoint;
    }

    internal async Task<BrokerWorkerResponse> ExecuteAsync(
        BrokerWorkerRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new BrokerProcessBoundaryException(
                "mt5_process_boundary_disabled",
                processStarted: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = timeProvider.GetUtcNow();
        BrokerWorkerContractValidator.ValidateRequest(request, now);
        TimeSpan remaining = request.DeadlineUtc - now;
        TimeSpan operationWindow = remaining < options.OperationTimeout
            ? remaining
            : options.OperationTimeout;
        if (operationWindow <= TimeSpan.Zero)
        {
            throw new BrokerProcessBoundaryException(
                "mt5_process_deadline_expired",
                processStarted: false);
        }

        DateTimeOffset absoluteDeadlineUtc = now.Add(operationWindow);
        using var deadlineCancellation = new CancellationTokenSource(
            operationWindow,
            timeProvider);
        using var boundaryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadlineCancellation.Token);
        CancellationToken boundaryToken = boundaryCancellation.Token;

        byte[]? requestPayload = null;
        byte[]? responsePayload = null;
        byte[] sessionKey = RandomNumberGenerator.GetBytes(
            BrokerProcessProtocol.SessionKeyBytes);
        Process? process = null;
        BrokerWorkerLaunchClosure? launchClosure = null;
        bool processStarted = false;
        bool treeKillRequested = false;
        bool terminationConfirmed = true;
        int processId = 0;
        BrokerWorkerResponse? completedResponse = null;
        BrokerProcessBoundaryException? boundaryFailure = null;
        OperationCanceledException? propagatedCancellation = null;
        try
        {
            requestPayload = BrokerProcessProtocol.SerializeRequest(
                request,
                options.MaximumRequestBytes);
            ThrowIfBoundaryClosed(absoluteDeadlineUtc, boundaryToken);
            try
            {
                launchClosure = await BrokerWorkerLaunchManifestVerifier.OpenAndVerifyAsync(
                        options,
                        boundaryToken,
                        launchCheckpoint)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new BrokerProcessBoundaryException(
                    "mt5_process_launch_closure_invalid",
                    processStarted: false);
            }

            ThrowIfBoundaryClosed(absoluteDeadlineUtc, boundaryToken);
            process = CreateProcess();
            ThrowIfBoundaryClosed(absoluteDeadlineUtc, boundaryToken);
            if (!process.Start())
            {
                ThrowIfBoundaryClosed(absoluteDeadlineUtc, boundaryToken);
                throw new BrokerProcessBoundaryException(
                    "mt5_process_start_failed",
                    processStarted: false);
            }

            processStarted = true;
            processId = process.Id;
            observer?.OnStarted(processId);
            _ = DrainStandardErrorAsync(process.StandardError.BaseStream);

            // Process.Start is synchronous and cannot be preempted. A late
            // successful return is detected before any authenticated bootstrap;
            // the child is then terminated by the fail-closed cleanup path.
            ThrowIfBoundaryClosed(absoluteDeadlineUtc, boundaryToken);
            await BrokerProcessProtocol.WriteBootstrapAsync(
                    process.StandardInput.BaseStream,
                    sessionKey,
                    boundaryToken)
                .ConfigureAwait(false);
            await BrokerProcessProtocol.WriteRequestAsync(
                    process.StandardInput.BaseStream,
                    requestPayload,
                    sessionKey,
                    boundaryToken)
                .ConfigureAwait(false);
            process.StandardInput.Close();

            responsePayload = await BrokerProcessProtocol.ReadResponseAsync(
                    process.StandardOutput.BaseStream,
                    sessionKey,
                    options.MaximumResponseBytes,
                    boundaryToken)
                .ConfigureAwait(false);
            BrokerWorkerResponse response = BrokerProcessProtocol.DeserializeResponse(
                responsePayload);
            BrokerWorkerContractValidator.ValidateResponse(response, request);

            await process.WaitForExitAsync(boundaryToken).ConfigureAwait(false);
            byte[] trailingOutput = new byte[1];
            int trailingBytes;
            try
            {
                trailingBytes = await process.StandardOutput.BaseStream.ReadAsync(
                        trailingOutput,
                        boundaryToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(trailingOutput);
            }

            if (process.ExitCode != 0 || trailingBytes != 0)
            {
                throw new BrokerProcessBoundaryException(
                    "mt5_process_exit_invalid",
                    processStarted: true);
            }

            completedResponse = response;
        }
        catch (OperationCanceledException exception)
        {
            if (!processStarted && cancellationToken.IsCancellationRequested)
            {
                propagatedCancellation = exception;
            }
            else
            {
                boundaryFailure = new BrokerProcessBoundaryException(
                    processStarted
                        ? "mt5_process_deadline_terminated"
                        : "mt5_process_deadline_expired",
                    processStarted);
            }
        }
        catch (BrokerProcessBoundaryException exception)
        {
            boundaryFailure = exception;
        }
        catch
        {
            boundaryFailure = new BrokerProcessBoundaryException(
                processStarted
                    ? "mt5_process_response_untrusted"
                    : "mt5_process_start_failed",
                processStarted);
        }
        finally
        {
            if (process is not null && processStarted && !HasExited(process))
            {
                treeKillRequested = true;
                // Kill(entireProcessTree) provides no inspectable proof that
                // every descendant stopped. Requiring it therefore makes the
                // boundary outcome unconfirmable even when the root exits.
                terminationConfirmed = false;
                try
                {
                    await RequestProcessTreeTerminationAsync(process).ConfigureAwait(false);
                }
                catch
                {
                    terminationConfirmed = false;
                }
            }

            if (processStarted)
            {
                try
                {
                    observer?.OnTerminated(processId, treeKillRequested);
                }
                catch
                {
                    // Observability hooks cannot mask the fixed boundary result.
                }
            }

            try
            {
                process?.Dispose();
            }
            catch
            {
                // Cleanup cannot mask the fixed, sanitized boundary outcome.
            }

            launchClosure?.Dispose();
            Zero(requestPayload);
            Zero(responsePayload);
            CryptographicOperations.ZeroMemory(sessionKey);
        }

        if (!terminationConfirmed)
        {
            throw new BrokerProcessBoundaryException(
                "mt5_process_termination_unconfirmed",
                processStarted: true);
        }

        if (propagatedCancellation is not null)
        {
            ExceptionDispatchInfo.Capture(propagatedCancellation).Throw();
        }

        if (boundaryFailure is not null)
        {
            throw boundaryFailure;
        }

        return completedResponse
            ?? throw new BrokerProcessBoundaryException(
                "mt5_process_response_untrusted",
                processStarted);
    }

    private Process CreateProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.WorkerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(options.WorkerExecutablePath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        string? temp = Environment.GetEnvironmentVariable("TEMP");
        string dotnetRoot = CurrentDotNetRoot();
        startInfo.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            startInfo.Environment["SystemRoot"] = systemRoot;
        }

        if (!string.IsNullOrWhiteSpace(temp))
        {
            startInfo.Environment["TEMP"] = temp;
            startInfo.Environment["TMP"] = temp;
        }

        startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
        if (Environment.Is64BitProcess)
        {
            startInfo.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
        }

        startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";
        startInfo.Environment["COMPlus_EnableDiagnostics"] = "0";
        return new Process { StartInfo = startInfo };
    }

    private static string CurrentDotNetRoot()
    {
        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        DirectoryInfo? root = runtimeDirectory.Parent?.Parent?.Parent;
        if (root is null || !root.Exists)
        {
            throw new InvalidOperationException("The current .NET runtime root is unavailable.");
        }

        return root.FullName;
    }

    private async Task RequestProcessTreeTerminationAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between the check and the request, or
            // the OS may have denied termination. No details are exposed.
        }

        try
        {
            using var shutdown = new CancellationTokenSource(options.ShutdownTimeout);
            await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch
        {
            // This bounded wait observes only the root handle. It is cleanup,
            // never proof that the descendant set terminated.
        }
    }

    private void ThrowIfBoundaryClosed(
        DateTimeOffset absoluteDeadlineUtc,
        CancellationToken boundaryToken)
    {
        boundaryToken.ThrowIfCancellationRequested();
        if (timeProvider.GetUtcNow() >= absoluteDeadlineUtc)
        {
            throw new OperationCanceledException(boundaryToken);
        }
    }

    private static async Task DrainStandardErrorAsync(Stream standardError)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (await standardError.ReadAsync(buffer).ConfigureAwait(false) != 0)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
        catch
        {
            // Child diagnostics are deliberately discarded and never logged.
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void Zero(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
