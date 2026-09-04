---
agent_id: G08
lane: MT5 Connection Probe & Worker Host
scope:
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5BrokerSymbol.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5ConnectionProbeWorkerComposition.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiAccountReader.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiQuoteHistoryClient.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/PinnedMt5ServersDatEndpointReader.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/VaultBackedBrokerConnectionProbeExecutor.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/YO4X.Mt5.ConnectionProbe.Windows.csproj
  - src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/Program.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.csproj
status: COMPLETE
generated: 2026-08-29T11:27:38Z
counts: { P0: 0, P1: 1, P2: 2, P3: 0 }
---

# G08 — MT5 Connection Probe & Worker Host

## Scope audited
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5BrokerSymbol.cs` (28 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5ConnectionProbeWorkerComposition.cs` (193 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiAccountReader.cs` (388 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs` (361 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiQuoteHistoryClient.cs` (269 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs` (425 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/PinnedMt5ServersDatEndpointReader.cs` (345 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/VaultBackedBrokerConnectionProbeExecutor.cs` (196 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/YO4X.Mt5.ConnectionProbe.Windows.csproj` (19 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/Program.cs` (19 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.csproj` (18 lines)

## Verdict
The connection probe and worker host architecture is generally well-isolated, using strict SHA-256 binary pinning, DPAPI vault evidence binding, and single-use ephemeral process lifetimes to prevent credential leakage. However, `Mt5NetApiConnectionOnlyTransport` hardcodes `BrokerEnvironment.Demo` instead of verifying the broker's actual reported account group, bypassing downstream environment validation. Additionally, synchronous blocking socket operations without timeout configuration can hold credentials in memory on hung network endpoints, and error handling conflates authentication rejections with transport failures.

## Findings

### [P1] Connection probe transport fabricates Demo environment without verifying broker account group
- **Where:** `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:286-294`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                return new Mt5ConnectionOnlyObservation(
                    company,
                    endpoint.ServerName,
                    accountMode,
                    BrokerEnvironment.Demo,
                    BrokerTradingAccess.Unknown,
                    currency,
                    true,
                    timeProvider.GetUtcNow());
  ```
- **Failure:** `VaultBackedBrokerConnectionProbeExecutor` checks `connected.Environment != BrokerEnvironment.Demo` (line 147) to ensure real/funded accounts are not admitted by a demo connection probe. However, `Mt5NetApiConnectionOnlyTransport` hardcodes `BrokerEnvironment.Demo` without reading `Account.Type` from the live session (unlike `Mt5NetApiDemoTradeClient` and `Mt5NetApiAccountReader`). When an operator configures credentials for a live/real account, the probe connects to the live broker server, fabricates a demo environment observation, and succeeds, bypassing downstream environment protection.
- **Fix:** Read the session's account group string from `apiType.GetProperty("Account")`, determine if the server reports a demo account, and populate `Mt5ConnectionOnlyObservation.Environment` with the actual verified environment.

### [P2] Probe transport performs blocking synchronous connect without timeout configuration or in-flight cancellation
- **Where:** `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:252-270`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        return Task.FromResult(credential.UsePassword(ConnectWithPassword));

        Mt5ConnectionOnlyObservation ConnectWithPassword(ReadOnlySpan<byte> passwordUtf8)
        {
            // The vendor constructor requires System.String. This unavoidable vendor-boundary
            // copy is never logged or returned and remains scoped to this single-use worker.
            string password = Encoding.UTF8.GetString(passwordUtf8);
            IMt5NetApiConnectionClient? client = null;
            bool disconnectConfirmed = false;
            try
            {
                client = clientFactory.Create(
                    credential.Login,
                    password,
                    endpoint.Host,
                    endpoint.Port,
                    endpoint.CertificatePfx,
                    endpoint.CertificatePassword);
                client.Connect();
  ```
- **Failure:** `ConnectAndDisconnectAsync` wraps synchronous execution inside `Task.FromResult`. It does not configure the vendor `ConnectTimeout` field on `MT5API` and does not check `cancellationToken` once `Connect()` begins. If the broker host is unresponsive or blackholed, the thread blocks indefinitely in vendor socket connection routines holding the plaintext `password` string in its stack frame and keeping the DPAPI `LocalMt5Credential` open until the external supervisor forcefully terminates the process.
- **Fix:** Expose and configure `SetConnectTimeout` on `IMt5NetApiConnectionClient`, and execute connection attempts with cancellation support that aborts and disposes the client when the request deadline expires.

### [P2] Probe executor conflates invalid credentials with transport failures, creating account lockout risk
- **Where:** `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/VaultBackedBrokerConnectionProbeExecutor.cs:184-188`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failed();
        }
  ```
- **Failure:** When a credential has an invalid password, `client.Connect()` fails with a vendor authentication error. The exception is swallowed and mapped to `BrokerWorkerProtocolContract.ConnectProbeFailedCode` ("connect_probe_failed"), which is the identical code returned for temporary network glitches, host unreachability, or socket timeouts. Automated health checks and connection probe supervisors cannot distinguish bad credentials from transient network drops and will repeatedly retry probing, triggering broker-side MT5 access server rate-limiting and account lockout.
- **Fix:** Distinguish vendor authentication and bad-password exceptions from transport-level network faults and return distinct result status codes.

## Referrals
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerConnectionProbeWorkerServer.cs` — `RunOnceAsync` sets a cancellation timeout via `deadline.CancelAfter(remaining)`, but executor implementations running synchronous blocking calls on the calling thread cannot observe cancellation during vendor socket I/O.

## Coverage gaps
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:340-359`: `MapAccountMode` handling of unknown or non-standard vendor account allocation models (falls back silently to `BrokerAccountMode.Unknown`).
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/PinnedMt5ServersDatEndpointReader.cs:219-234`: Parsing and deduplication behavior in `ReflectionMt5ServersDatLoader` when access records contain a mix of IPv4 and unbracketed IPv6 fallback addresses.
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs:224-231`: Exception handling and slot-cleanup (`TryStop`) branch in `OnBatch` when vendor bar records trigger reflection field type mismatches.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 85.6s | 205954 tok | id=165761f5-fc33-4905-b8b7-bd020fa80813
