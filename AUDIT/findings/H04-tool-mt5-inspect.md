---
agent_id: H04
lane: MT5 Demo and Diagnostic Tools
scope:
  - src/Tools/YO4X.Mt5.AccountInspector/**
  - src/Tools/YO4X.Mt5.BrokerCatalogueImport/**
  - src/Tools/YO4X.Mt5.DemoCanary/**
  - src/Tools/YO4X.Mt5.DemoExecutionTest/**
status: COMPLETE
generated: 2026-08-29T11:32:00Z
counts: { P0: 1, P1: 1, P2: 1, P3: 1 }
---

# H04 — MT5 Demo and Diagnostic Tools

## Scope audited
- `src/Tools/YO4X.Mt5.AccountInspector/BrokerEndpointDirectory.cs` (176 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/ConnectivitySweep.cs` (472 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/PinnedVendorAssembly.cs` (72 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/Program.cs` (185 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/VendorMt5Session.cs` (229 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/YO4X.Mt5.AccountInspector.csproj` (19 lines)
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs` (650 lines)
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/YO4X.Mt5.BrokerCatalogueImport.csproj` (15 lines)
- `src/Tools/YO4X.Mt5.DemoCanary/DemoCanaryOptions.cs` (155 lines)
- `src/Tools/YO4X.Mt5.DemoCanary/Program.cs` (41 lines)
- `src/Tools/YO4X.Mt5.DemoCanary/YO4X.Mt5.DemoCanary.csproj` (19 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/LatencyBenchmark.cs` (109 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs` (149 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/YO4X.Mt5.DemoExecutionTest.csproj` (16 lines)

## Verdict
The diagnostic tools (`AccountInspector`, `DemoCanary`, and `BrokerCatalogueImport`) are read-only and carefully avoid exposing or persisting sensitive secrets. However, `DemoExecutionTest` contains a critical P0 flaw: despite being a test tool intended for demo accounts, it accepts `--environment live`, bypassing demo safety checks and executing live market orders and pending orders against real funded accounts. Furthermore, `DemoExecutionTest` lacks any teardown or failure recovery logic, leaving open positions and pending orders orphaned on the broker when tests encounter errors.

## Findings

### [P0] DemoExecutionTest accepts `--environment live` and executes real trades on live funded accounts
- **Where:** `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:41-43`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          Mt5TradingEnvironment environment = Optional(arguments, "--environment") is "live"
              ? Mt5TradingEnvironment.Live
              : Mt5TradingEnvironment.Demo;
  ```
- **Failure:** An operator or automation script runs `YO4X.Mt5.DemoExecutionTest --environment live --credential-key <live_key> --symbol EURUSD ...`. Because `Mt5NetApiDemoTradeClient.RequireDeclaredEnvironment` verifies that the declared environment matches what the broker reports, declaring `live` for a live account satisfies the environment check. `DemoExecutionTest` then places real market BUY orders (`0.01` lots) and pending stop orders (`0.01` lots) or benchmark cycles against a live funded account, risking real capital from a diagnostic tool.
- **Fix:** Remove the `--environment live` option from `YO4X.Mt5.DemoExecutionTest/Program.cs` and hardcode `Mt5TradingEnvironment.Demo` so the tool is strictly prohibited from executing against live accounts.

### [P1] Unhandled failure during position lifecycle leaves test positions open and pending orders active on broker
- **Where:** `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:83-91`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          Console.WriteLine("[2] modify stop and target");
          double stop = Math.Round(opened.Price * 0.995, 5);
          double target = Math.Round(opened.Price * 1.005, 5);
          latencies.Add(await client.ModifyAsync(opened, stop, target).ConfigureAwait(false));

          Console.WriteLine();
          Console.WriteLine("[3] close position");
          Mt5DemoOrderReceipt closed = await client.CloseAsync(opened).ConfigureAwait(false);
  ```
- **Failure:** In `DemoExecutionTest`, step [1] opens a market position ticket. If step [2] `ModifyAsync` fails (e.g. broker rejects stops due to minimum stop-distance rules) or step [3] `CloseAsync` fails, an exception is thrown to `Main` and the process exits. The open position is never closed. Similarly, if step [4] places a pending stop order and step [5] `CancelAsync` throws, the pending order remains active on the broker and can trigger into an unmonitored market position.
- **Fix:** Wrap the test sequence in a `try/finally` block that attempts an emergency close on `opened` and cancel on `placed` if any intermediate step fails.

### [P2] Fixed string slicing on vault credential key crashes connectivity sweep on short filenames
- **Where:** `src/Tools/YO4X.Mt5.AccountInspector/ConnectivitySweep.cs:188-190`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          LocalMt5CredentialDescriptor descriptor = credential.Describe();
          Console.WriteLine(
              $"[{credentialKey[..12]}…] {descriptor.Server} login {descriptor.MaskedLogin}");
  ```
- **Failure:** When `ConnectivitySweep.RunAsync` enumerates `*.yo4xcred` in a vault containing a non-standard or test key file whose filename without extension is shorter than 12 characters (e.g. `mock.yo4xcred`), evaluating `credentialKey[..12]` throws an unhandled `ArgumentOutOfRangeException`, crashing the sweep before remaining accounts can be inspected.
- **Fix:** Use `credentialKey[..Math.Min(credentialKey.Length, 12)]` (and `Math.Min(..., 10)` in `Program.cs:139` and `RenderTable:452`) instead of unconditional range slicing.

### [P3] Overwriting server access endpoints during directory merge drops endpoints discovered across sweep terms
- **Where:** `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs:225-231`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              if (server.ValueKind == JsonValueKind.Object
                  && server.TryGetProperty("name", out JsonElement serverName)
                  && serverName.ValueKind == JsonValueKind.String
                  && TryNormalizeName(serverName.GetString(), 500, out string normalizedServer))
              {
                  knownServers[normalizedServer] = ReadAccessEndpoints(server, "access");
              }
  ```
- **Failure:** During `fetch`, multiple 2-letter search terms return results for the same broker company and server. Direct dictionary assignment overwrites previously discovered access endpoints for that server rather than unioning them, causing alternative network access nodes discovered on earlier pages to be lost.
- **Fix:** Merge newly parsed endpoints with existing entries in `knownServers[normalizedServer]` using a sorted set union up to `CatalogueSource.MaximumAccessEndpointCount`.

## Referrals
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` — Class is named `Mt5NetApiDemoTradeClient` and documented as demo-only, but accepts `Mt5TradingEnvironment.Live` and trades against live accounts if supplied.

## Coverage gaps
- `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:86-90`: Untested branch where `ModifyAsync` or `CloseAsync` throws after `SendAsync` succeeds, hiding orphaned open market positions.
- `src/Tools/YO4X.Mt5.AccountInspector/ConnectivitySweep.cs:189`: Untested branch where credential keys have length < 12, hiding `ArgumentOutOfRangeException`.
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs:230`: Untested branch where the same server name appears across multiple search pages with differing access endpoints.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 158.9s | 243118 tok | id=e0a562ce-3699-4ed1-8568-6a99531adab2
