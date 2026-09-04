---
agent_id: I10
lane: operational-scripts
scope:
  - scripts/Get-BacktestDetail.ps1
  - scripts/Get-BrokerSymbols.ps1
  - scripts/Get-StrategyInputs.ps1
  - scripts/Import-Mql5Corpus.ps1
  - scripts/New-BrokerWorkerLaunchManifest.ps1
  - scripts/Show-ProjectionPayloads.ps1
  - scripts/Start-YO4XDevelopment.ps1
  - scripts/Stop-YO4XDevelopment.ps1
  - scripts/Test-BrokerAccountLink.ps1
  - scripts/Test-FrontendProjectionEndpoints.ps1
  - scripts/Test-Mt5ToolchainIsolation.ps1
  - scripts/Test-PostgresIntegration.ps1
  - scripts/Test-YO4XDevelopmentAuth.ps1
  - scripts/build-catalog-sql.mjs
  - scripts/debug-import-session.ps1
  - scripts/project-corpus-to-catalog.sql
  - src/Frontend/YO4X.Web/scripts/design-capture.mjs
  - src/Frontend/YO4X.Web/scripts/dom-probe.mjs
  - src/Frontend/YO4X.Web/scripts/interaction-check.mjs
  - src/Frontend/YO4X.Web/scripts/live-capture.mjs
  - src/Frontend/YO4X.Web/scripts/live-detail.mjs
  - src/Frontend/YO4X.Web/scripts/stub-api.mjs
  - src/Frontend/YO4X.Web/scripts/visual-qa.mjs
status: COMPLETE
generated: 2026-08-29T11:32:00Z
counts: { P0: 0, P1: 1, P2: 3, P3: 3 }
---

# I10 — Operational Scripts

## Scope audited
- `scripts/Get-BacktestDetail.ps1` (92 lines)
- `scripts/Get-BrokerSymbols.ps1` (92 lines)
- `scripts/Get-StrategyInputs.ps1` (92 lines)
- `scripts/Import-Mql5Corpus.ps1` (185 lines)
- `scripts/New-BrokerWorkerLaunchManifest.ps1` (72 lines)
- `scripts/Show-ProjectionPayloads.ps1` (84 lines)
- `scripts/Start-YO4XDevelopment.ps1` (483 lines)
- `scripts/Stop-YO4XDevelopment.ps1` (66 lines)
- `scripts/Test-BrokerAccountLink.ps1` (156 lines)
- `scripts/Test-FrontendProjectionEndpoints.ps1` (155 lines)
- `scripts/Test-Mt5ToolchainIsolation.ps1` (495 lines)
- `scripts/Test-PostgresIntegration.ps1` (336 lines)
- `scripts/Test-YO4XDevelopmentAuth.ps1` (136 lines)
- `scripts/build-catalog-sql.mjs` (140 lines)
- `scripts/debug-import-session.ps1` (29 lines)
- `scripts/project-corpus-to-catalog.sql` (127 lines)
- `src/Frontend/YO4X.Web/scripts/design-capture.mjs` (311 lines)
- `src/Frontend/YO4X.Web/scripts/dom-probe.mjs` (49 lines)
- `src/Frontend/YO4X.Web/scripts/interaction-check.mjs` (102 lines)
- `src/Frontend/YO4X.Web/scripts/live-capture.mjs` (86 lines)
- `src/Frontend/YO4X.Web/scripts/live-detail.mjs` (50 lines)
- `src/Frontend/YO4X.Web/scripts/stub-api.mjs` (303 lines)
- `src/Frontend/YO4X.Web/scripts/visual-qa.mjs` (198 lines)

## Verdict
The core launcher, teardown, and verification scripts are well structured: `$ErrorActionPreference = 'Stop'` and `Set-StrictMode -Version Latest` are universally configured, ephemeral credentials use cryptographically secure random generators with DPAPI / Clixml storage, and file deletions generally enforce path containment guards. However, one projection SQL script performs an unconstrained table-wide `DELETE` that destroys cross-tenant performance metrics, a test script lacks assertions and silently exits with success on HTTP failure, and several utility scripts contain hardcoded author paths, missing `finally` cleanup guards, or copy-paste parameter/docstring defects.

## Findings

### [P1] Unscoped `DELETE` in `project-corpus-to-catalog.sql` deletes all tenant performance figures
- **Where:** `scripts/project-corpus-to-catalog.sql:101`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  -- Performance figures the corpus can actually support: byte size, feature count,
  -- include count and entrypoint count. No profit, drawdown or trade statistic is
  -- written, because none has ever been measured for these files.
  delete from catalog.strategy_performance;

  insert into catalog.strategy_performance (id, tenant_id, strategy_id, ordinal, label, value)
  ```
- **Failure:** When `Import-Mql5Corpus.ps1` executes `project-corpus-to-catalog.sql` under the `postgres` superuser role, line 101 runs an unqualified `DELETE FROM catalog.strategy_performance;` with no tenant predicate or strategy filter. In a multi-tenant database or a database containing existing strategies from other sources, this statement deletes all performance metrics across all tenants and all strategies rather than only the strategy performance records associated with the projected corpus source files.
- **Fix:** Replace the unqualified delete with a scoped delete matching the imported corpus strategies: `DELETE FROM catalog.strategy_performance WHERE strategy_id IN (SELECT id FROM governance.strategy_source_files);`.

### [P2] `Test-BrokerAccountLink.ps1` fails to assert HTTP status and returns success on API failure
- **Where:** `scripts/Test-BrokerAccountLink.ps1:142-152`
- **Confidence:** CONFIRMED
- **Code:**
  ```powershell
  $linkOut = (& curl.exe --silent --insecure --request POST `
      --header ("Authorization: Bearer {0}" -f $token.access_token) `
      --header "Content-Type: application/json" `
      --header ("Idempotency-Key: {0}" -f [guid]::NewGuid().ToString("D")) `
      --write-out "`nHTTP_STATUS:%{http_code}" `
      --data-binary "@$requestBody" ("{0}/v1/broker-accounts" -f $ApiOrigin) | Out-String)

  Write-Host ""
  Write-Host "link response:"
  Write-Host $linkOut
  ```
- **Failure:** When the `/v1/broker-accounts` endpoint fails (such as returning HTTP 500 or 400 during credential storage or worker communication), the script prints the error response to standard output without checking the HTTP status code or throwing an exception. Automated test suites or CI runners invoking `Test-BrokerAccountLink.ps1` exit with status 0 (success), falsely reporting that the account link flow succeeded.
- **Fix:** Parse the `HTTP_STATUS` token from the curl output and throw an exception or exit with code 1 if the HTTP status is not `200` or `201`.

### [P2] `Test-PostgresIntegration.ps1` fallback hardcodes local author user profile path
- **Where:** `scripts/Test-PostgresIntegration.ps1:284-291`
- **Confidence:** CONFIRMED
- **Code:**
  ```powershell
  $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
  if ($null -eq $dotnet) {
      $localDotnet = "C:\Users\Dev23\AppData\Local\YO4X\dotnet\dotnet.exe"
      if (-not (Test-Path -LiteralPath $localDotnet -PathType Leaf)) {
          throw "dotnet.exe was not found."
      }
      $dotnetPath = $localDotnet
  }
  ```
- **Failure:** When `dotnet.exe` is not located on `$env:PATH` (e.g. running under a CI service account, container, or another developer machine where the username is not `Dev23`), the fallback path hardcodes `C:\Users\Dev23\AppData\Local\...` rather than resolving the path dynamically via `$env:LOCALAPPDATA` or pointing to the repo-pinned toolchain at `.tools\dotnet-sdk-10.0.400\dotnet.exe`. The script immediately fails with `dotnet.exe was not found.`.
- **Fix:** Replace the hardcoded path with `Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"` or dynamically construct the path using `Join-Path $env:LOCALAPPDATA "YO4X\dotnet\dotnet.exe"`.

### [P2] Plaintext bearer token persisted in `debug-import-session.ps1` without `finally` cleanup guarantee
- **Where:** `scripts/debug-import-session.ps1:19-28`
- **Confidence:** CONFIRMED
- **Code:**
  ```powershell
  header = "Authorization: Bearer $Token"
  data-binary = "@$bodyForward"
  "@
  [IO.File]::WriteAllText($cfg, $configuration, (New-Object Text.UTF8Encoding($false)))
  "idempotency-key length: $($key.Length)  matches pattern: $($key -cmatch '^[A-Za-z0-9_-]{22,200}$')"
  "body: $(Get-Content -Raw $body)"
  "--- response ---"
  & curl.exe --config $cfg
  ""
  Remove-Item $body, $cfg -Force -ErrorAction SilentlyContinue
  ```
- **Failure:** `debug-import-session.ps1` writes the active authorization token `$Token` into `.local\development\dbg.cfg`. Because the invocation of `curl.exe` is not wrapped in a `try ... finally` block, any network failure, user interruption (Ctrl+C), or terminating error prior to line 28 leaves the unencrypted bearer token stored on disk in the workspace directory.
- **Fix:** Wrap the curl execution and file deletion in a `try ... finally` block to guarantee `$body` and `$cfg` are removed upon exit.

### [P3] `Get-BrokerSymbols.ps1` declares unused `$StrategyId` parameter and misleading docstring
- **Where:** `scripts/Get-BrokerSymbols.ps1:1-11,84`
- **Confidence:** CONFIRMED
- **Code:**
  ```powershell
  <#
  .SYNOPSIS
      Dumps the raw /v1/catalog/strategies/{id}/inputs payload for one strategy, so a
      decoder rejection can be compared against what the service actually returned.
  #>
  [CmdletBinding()]
  param(
      [string] $ApiOrigin = "https://127.0.0.1:7209",
      [string] $IdentityOrigin = "https://127.0.0.1:7210",
      [string] $StrategyId = "REPLACE"
  )
  ...
      $payload = (& curl.exe --silent --insecure `
          --header ("Authorization: Bearer {0}" -f $token.access_token) `
          ("{0}/v1/broker-symbols?server=VantageMarkets-Demo&query=XAU" -f $ApiOrigin) | Out-String)
  ```
- **Failure:** The script parameter `$StrategyId` is never referenced in the script body, the query string is hardcoded to `server=VantageMarkets-Demo&query=XAU`, and the synopsis docstring describes dumping strategy inputs. Callers passing custom broker servers, queries, or strategy IDs have their inputs silently ignored.
- **Fix:** Remove `$StrategyId`, declare `[string] $Server = "VantageMarkets-Demo"` and `[string] $Query = "XAU"`, parameterize line 84 with `$Server` and `$Query`, and update the `.SYNOPSIS` comment.

### [P3] `Get-BacktestDetail.ps1` declares `$StrategyId` for backtest route with mismatched docstring
- **Where:** `scripts/Get-BacktestDetail.ps1:1-11,84`
- **Confidence:** CONFIRMED
- **Code:**
  ```powershell
  <#
  .SYNOPSIS
      Dumps the raw /v1/catalog/strategies/{id}/inputs payload for one strategy, so a
      decoder rejection can be compared against what the service actually returned.
  #>
  [CmdletBinding()]
  param(
      [string] $ApiOrigin = "https://127.0.0.1:7209",
      [string] $IdentityOrigin = "https://127.0.0.1:7210",
      [string] $StrategyId = "REPLACE"
  )
  ...
      $payload = (& curl.exe --silent --insecure `
          --header ("Authorization: Bearer {0}" -f $token.access_token) `
          ("{0}/v1/backtests/{1}" -f $ApiOrigin, $StrategyId) | Out-String)
  ```
- **Failure:** The script queries `/v1/backtests/{id}` but names the parameter `$StrategyId` and copy-pastes the synopsis from `Get-StrategyInputs.ps1`. An operator reading the synopsis who supplies a strategy identifier will query the backtest endpoint with an invalid ID, resulting in a 404 response.
- **Fix:** Rename `$StrategyId` to `$BacktestId` and update the synopsis docstring to reflect backtest detail inspection.

### [P3] Duplicate fallback path in browser automation scripts prevents 64-bit Edge detection
- **Where:** `src/Frontend/YO4X.Web/scripts/design-capture.mjs:15-19` (also in `dom-probe.mjs:7-10`, `interaction-check.mjs:7-10`, `live-capture.mjs:15-19`, `live-detail.mjs:6-10`, `visual-qa.mjs:11-16`)
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  const executablePath = [
    process.env.YO4X_BROWSER_EXECUTABLE,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  ].filter(Boolean).find((path) => existsSync(path));
  ```
- **Failure:** Across all 6 frontend Playwright scripts, the fallback executable array contains the 32-bit Program Files (x86) Edge path twice and omits the standard 64-bit `C:\Program Files\Microsoft\Edge\Application\msedge.exe` path. On machines where Edge is installed in 64-bit Program Files and `YO4X_BROWSER_EXECUTABLE` is not explicitly set, the scripts fail to discover the browser and throw `Error: Set YO4X_BROWSER_EXECUTABLE to an installed Chromium-family browser.`.
- **Fix:** Update the second entry in the fallback array to `'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'`.

## Referrals
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` — Superuser role execution in scripts bypasses RLS; verify whether migration scripts require explicit session tenant context when projecting catalog rows.

## Coverage gaps
- `scripts/Import-Mql5Corpus.ps1:128-131` — Error branch when `/v1/strategy-source-import-sessions` returns invalid JSON or an HTTP error payload without `importJobId` is untested during normal automation runs.
- `scripts/Stop-YO4XDevelopment.ps1:29-33` — Process identity mismatch branch where PID matches an unrelated process is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 194.3s | 262795 tok | id=4b5f58c6-8a45-4f57-be62-a50c47e63486
