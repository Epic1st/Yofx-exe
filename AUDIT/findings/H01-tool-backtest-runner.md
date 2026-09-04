---
agent_id: H01
lane: Backtest Runner and Strategy Input Projection CLI Tools
scope:
  - src/Tools/YO4X.Backtest.Runner/YO4X.Backtest.Runner.csproj
  - src/Tools/YO4X.Backtest.Runner/Program.cs
  - src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs
  - src/Tools/YO4X.StrategyInputProjection/YO4X.StrategyInputProjection.csproj
  - src/Tools/YO4X.StrategyInputProjection/Program.cs
  - src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs
  - src/Tools/YO4X.StrategyInputProjection/StrategyProjectionIdentity.cs
  - src/Tools/YO4X.StrategyInputProjection/Mql5InputProjection.cs
status: COMPLETE
generated: 2026-08-29T11:31:00Z
counts: { P0: 0, P1: 2, P2: 3, P3: 2 }
---

# H01 — Backtest Runner and Strategy Input Projection CLI Tools

## Scope audited
- [src/Tools/YO4X.Backtest.Runner/YO4X.Backtest.Runner.csproj](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/YO4X.Backtest.Runner.csproj) (21 lines)
- [src/Tools/YO4X.Backtest.Runner/Program.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs) (680 lines)
- [src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs) (126 lines)
- [src/Tools/YO4X.StrategyInputProjection/YO4X.StrategyInputProjection.csproj](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/YO4X.StrategyInputProjection.csproj) (18 lines)
- [src/Tools/YO4X.StrategyInputProjection/Program.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/Program.cs) (5 lines)
- [src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs) (549 lines)
- [src/Tools/YO4X.StrategyInputProjection/StrategyProjectionIdentity.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyProjectionIdentity.cs) (83 lines)
- [src/Tools/YO4X.StrategyInputProjection/Mql5InputProjection.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/Mql5InputProjection.cs) (390 lines)

## Verdict
The core database projection logic and cryptographic identity derivation across both CLI tools are strictly structured and deterministic. However, the command-line argument parsers fail to reject invalid or unrecognized parameters, silently defaulting critical options such as queue limits and tenant IDs. Additionally, the backtest runner contains unguarded double-to-decimal casts that crash the process on non-finite trade metrics, and a data-quality calculation that reports 0% coverage on valid backtests spanning weekend boundaries.

## Findings

### [P1] Unguarded double-to-decimal cast throws unhandled `OverflowException`, stranding claimed backtest in RUNNING status
- **Where:** [src/Tools/YO4X.Backtest.Runner/Program.cs:257-266](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L257-L266)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        Mql5RunReport report = result.Report;
        return new BacktestOutcome(
            (decimal)(report.FinalBalance - report.InitialDeposit),
            ClampDrawdown((decimal)report.MaxDrawdownPercent),
            ClampProfitFactor(report.ProfitFactor),
            report.TotalTrades,
            coverage,
            Truncate(DescribeFidelity(csv, request.Timeframe, window.Count), 200),
            null,
            BuildEquityCurve(report));
  ```
- **Failure:** When a backtest produces non-finite or extreme values (e.g. `report.FinalBalance` or `report.MaxDrawdownPercent` is `double.NaN`, `double.PositiveInfinity`, or exceeds `decimal.MaxValue` due to division-by-zero or runaway compounding in strategy simulation), casting directly to `(decimal)` throws `System.OverflowException`. Because `Main` only catches `ArgumentException`, `IOException`, `InvalidDataException`, and `NpgsqlException`, this unhandled exception crashes the process, leaving the claimed database row permanently stuck in `RUNNING` status with no completion timestamp or failure reason, and aborting all subsequent queued jobs.
- **Fix:** Validate that `report.FinalBalance` and `report.MaxDrawdownPercent` are finite and within storable bounds before casting, modifying `ClampDrawdown` to accept `double` and returning `BacktestOutcome.Refused` if values cannot be represented as `decimal`.

### [P1] Weekend day deduction in data quality coverage underflows to 0% for partial-span backtests
- **Where:** [src/Tools/YO4X.Backtest.Runner/Program.cs:367-379](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L367-L379)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int weekendDays = 0;
        for (DateTime day = window[0].Time.Date; day <= window[^1].Time.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekendDays++;
            }
        }

        double expected = (spanMinutes - (weekendDays * 1440.0)) / periodMinutes;
        if (expected <= 0)
        {
            return 0m;
        }
  ```
- **Failure:** `MeasureCoverage` iterates through calendar dates from `window[0].Time.Date` to `window[^1].Time.Date` and subtracts 1,440 minutes for every Saturday and Sunday touched. For a backtest spanning across a weekend with partial boundaries (for example, Friday 22:00 to Monday 02:00 = 1,680 minutes total span across 4 calendar dates), `weekendDays` equals 2 and `weekendDays * 1440.0` equals 2,880 minutes. `spanMinutes - 2880.0` evaluates to `-1200.0`, triggering `if (expected <= 0) return 0m;`. The runner writes `data_quality_percent = 0.00%` into `simulation.backtests` despite 100% of trading hours bars being present.
- **Fix:** Calculate actual weekend minutes that fall strictly within the `[window[0].Time, window[^1].Time]` timestamp range rather than subtracting full 24-hour days for each touched calendar date.

### [P2] Invalid or malformed `--limit` arguments silently default to running the entire queue
- **Where:** [src/Tools/YO4X.Backtest.Runner/Program.cs:79-83](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L79-L83)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int limit = int.TryParse(
            Option(arguments, "--limit"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed) ? parsed : int.MaxValue;
  ```
- **Failure:** If an operator supplies a non-numeric or negative limit (e.g. `--limit abc`, `--limit -5`, or `--limit` with no value), `int.TryParse` fails due to `NumberStyles.None`. Instead of raising an error or reporting invalid arguments, `limit` silently defaults to `int.MaxValue`, causing the runner to process all queued backtests unbounded.
- **Fix:** Check if `--limit` was passed in `arguments`; if present, require `int.TryParse` to succeed with a positive value (`parsed > 0`), throwing `ArgumentException` on invalid values.

### [P2] `YO4X.StrategyInputProjection` ignores `--help` and executes full projection
- **Where:** [src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs:468-476](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs#L468-L476)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string sourceRoot = Path.GetFullPath(
            GetOption(arguments, "--source-root") ?? Path.Combine("Testing", "Mq5"));
        string manifestPath = Path.GetFullPath(
            GetOption(arguments, "--manifest") ?? DefaultManifestPath);
        string outputPath = Path.GetFullPath(
            GetOption(arguments, "--output")
            ?? Path.Combine(".local", "development", "strategy-input-projection.sql"));
        string tenant = GetOption(arguments, "--tenant-id") ?? DefaultTenantId;
  ```
- **Failure:** Passing `--help` or `-h` to `YO4X.StrategyInputProjection` is not intercepted by `ParseOptions`. When default paths exist on disk, the tool ignores `--help`, runs the complete compilation and code generation pipeline, overwrites `.local/development/strategy-input-projection.sql`, and exits with code 0 without ever displaying usage information.
- **Fix:** Add a check for `--help` or `-h` at the start of `RunAsync` or `ParseOptions` that invokes `WriteUsage()` and exits cleanly with return code 0.

### [P2] Unrecognized CLI arguments and typos in `YO4X.StrategyInputProjection` are silently ignored
- **Where:** [src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs:499-529](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs#L499-L529)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static string? GetOption(IReadOnlyList<string> arguments, string option)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (!arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new ArgumentException("Option '" + option + "' can be specified only once.");
            }

            index = candidate;
        }
  ```
- **Failure:** `ParseOptions` only searches for four specific flags (`--source-root`, `--manifest`, `--output`, `--tenant-id`). Any unrecognized option or typo (such as `--tenant <guid>` instead of `--tenant-id`, or `--output-path <path>` instead of `--output`) is skipped without validation. The command silently falls back to the default hardcoded tenant (`019c8d27-763d-7000-8000-000000000001`) and default output file, generating SQL against the wrong tenant.
- **Fix:** Validate all tokens in `arguments` against the set of accepted option flags, throwing `ArgumentException` when unknown parameters are supplied.

### [P3] Generated diagnostic query counts rows globally across all tenants
- **Where:** [src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs:373-376](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs#L373-L376)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        builder.Append(
            """
            select
                (select pg_catalog.count(*) from catalog.strategy_inputs) as input_rows,
                (select pg_catalog.count(*) from catalog.strategy_enum_members) as enum_member_rows;

            """);
  ```
- **Failure:** The concluding SQL query in the generated migration script performs an unfiltered `count(*)` on `catalog.strategy_inputs` and `catalog.strategy_enum_members`. In a database with multiple tenants, running this script reports the total row count across all tenants rather than confirming the rows projected for the specific target tenant.
- **Fix:** Add `where tenant_id = <tenant>::uuid` to both subqueries in the diagnostic output block.

### [P3] StrategySourceResolver does not validate manifest relative paths against path traversal
- **Where:** [src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs:81-86](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs#L81-L86)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        file = found;
        string path = Path.Combine(corpusRoot, found.RelativePath);
        if (!File.Exists(path))
        {
            refusal = "The corpus file named by the manifest is not on disk: " + found.RelativePath;
            return false;
        }
  ```
- **Failure:** Unlike `StrategyInputProjectionCommand.ReadManifestAsync` (which checks for `..` and rooted paths), `StrategySourceResolver.Load` does not check `relativePath`. If a manifest contains relative paths with directory traversal (e.g. `../../secret.mq5`), `Path.Combine(corpusRoot, found.RelativePath)` navigates outside the intended `corpusRoot` directory.
- **Fix:** Reject entries in `StrategySourceResolver.Load` where `relativePath` contains `..` or is rooted.

## Referrals
- [src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunReport.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunReport.cs) — `FinalBalance` and `MaxDrawdownPercent` allow unconstrained `double` values (including `NaN` and `Infinity`), which propagate to consumers expecting storable decimal quantities.
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IrV2.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IrV2.cs) — `Mql5IrEnumMember` does not define a `Label` property, causing reflection probes in `Mql5InputProjection` to always yield `null` for enum member descriptions.

## Coverage gaps
- [src/Tools/YO4X.Backtest.Runner/Program.cs:49-56](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L49-L56) — Catch block in `Main` only filters four specific exception types, leaving other runtime exceptions (`OverflowException`, `KeyNotFoundException`) untested and leading to unhandled process crashes.
- [src/Tools/YO4X.Backtest.Runner/Program.cs:351-383](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L351-L383) — `MeasureCoverage` lacks tests for spans crossing weekend boundaries where calendar weekend days exceed span minutes, hiding the underflow to 0% data quality.
- [src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs:499-529](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs#L499-L529) — `GetOption` lacks unit tests asserting rejection of unrecognized CLI flags, masking silent fallback to hardcoded default values.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 164.8s | 217341 tok | id=1381d147-ad9d-4f58-8e8f-5305535504d6
