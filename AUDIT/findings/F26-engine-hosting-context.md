---
agent_id: F26
lane: Strategy Host and Market Context
scope:
  - src/Runtime/YO4X.Mql5.Engine/Hosting/IMql5Strategy.cs
  - src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunOptions.cs
  - src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunReport.cs
  - src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs
  - src/Runtime/YO4X.Mql5.Engine/Context/IMql5MarketContext.cs
  - src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 1, P2: 2, P3: 0 }
---

# F26 — Strategy Host and Market Context

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Hosting/IMql5Strategy.cs` (23 lines)
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunOptions.cs` (67 lines)
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunReport.cs` (84 lines)
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs` (265 lines)
- `src/Runtime/YO4X.Mql5.Engine/Context/IMql5MarketContext.cs` (79 lines)
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs` (320 lines)

## Verdict
The core simulation pipeline is structurally sound: tick lifecycle sequencing (`OnInit` -> `AppendBar` -> `ApplyBar` -> `BeginTick` -> `OnTick` -> `OnDeinit`) strictly guarantees that intrabar stops and resting orders fire before strategy invocation, eliminating look-ahead bias and off-by-one errors. Failed initializations correctly abort the run before any ticks are delivered. However, `Mql5StrategyHost.Summarize` contains a P1 defect in relative drawdown calculation where larger currency drawdowns overwrite earlier, more severe percentage drawdowns, and two P2 issues involving early loop termination truncating `BarsSeen` accounting and missing journal audit events when `OnDeinit` throws.

## Findings

### [P1] MaxDrawdownPercent is overwritten by absolute drawdown instead of tracking peak relative decline
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:217-226`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        foreach (double equity in equityCurve)
        {
            peak = Math.Max(peak, equity);
            double drawdown = peak - equity;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
                maxDrawdownPercent = peak > 0.0 ? drawdown / peak * 100.0 : 0.0;
            }
        }
  ```
- **Failure:** A strategy starts with an initial deposit of $1,000 and immediately incurs a catastrophic 90% drawdown to $100 (drawdown = $900, `maxDrawdownPercent` = 90.0%). The strategy then recovers and scales equity to $100,000 before experiencing a minor dip to $99,000 (drawdown = $1,000). Because $1,000 > $900, the condition `drawdown > maxDrawdown` evaluates to true, overwriting `maxDrawdownPercent` with `1,000 / 100,000 * 100 = 1.0%`. `Mql5RunReport.MaxDrawdownPercent` reports 1.0% despite the contract documenting it as "the largest peak-to-trough equity decline as a percentage of the peak", severely masking ruin risk.
- **Fix:** Track relative drawdown independently on each sample point via `double pct = peak > 0.0 ? drawdown / peak * 100.0 : 0.0; if (pct > maxDrawdownPercent) maxDrawdownPercent = pct;` rather than gating percentage updates on currency drawdown increases.

### [P2] Feed enumeration breaks early on tick cap, violating BarsSeen report contract
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:72-80`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        foreach (Mql5Bar bar in feed.ReadBars())
        {
            barsSeen++;

            if (ticks >= options.MaxTicks)
            {
                tickCapTriggered = true;
                break;
            }
  ```
- **Failure:** A market feed contains 10,000 bars and `options.MaxTicks` is configured to 10. When `ticks` reaches 10 on the 11th bar, the loop immediately executes `break;`. `Mql5RunReport.BarsSeen` is emitted as 11 rather than 10,000, violating its documented contract ("Gets the number of bars the feed produced, including any skipped by the tick cap").
- **Fix:** Either drain the remaining bars in the feed or query the feed count when available to accurately populate `BarsSeen` when `tickCapTriggered` is true, or update the report contract to specify bars consumed prior to loop termination.

### [P3] Strategy exception thrown in OnDeinit is not recorded in report Events journal
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:144-149`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string deinitFault = string.Empty;
        SafeDeinit(strategy, context, deinitReason, ref deinitFault);
        if (fault.Length == 0 && deinitFault.Length > 0)
        {
            fault = deinitFault;
        }
  ```
- **Failure:** A strategy executes all ticks cleanly (`fault` is initially empty), but throws an unhandled exception inside `OnDeinit`. The `StrategyFault` order event was already evaluated at lines 118-128 prior to calling `SafeDeinit`. Consequently, while `report.StrategyFault` receives the exception text and `report.CompletedCleanly` returns `false`, `report.Events` contains no `Mql5OrderEventKind.StrategyFault` entry, creating an incomplete audit trail.
- **Fix:** If `fault` is updated from `deinitFault` following `SafeDeinit`, append a corresponding `Mql5OrderEvent` with `Kind = Mql5OrderEventKind.StrategyFault` to `events` before calling `Summarize`.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:153` — Swap accrual in `ApplyBar` checks calendar date change rather than day-of-week triple rollover rules.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:146` — No unit test asserts `report.Events` contents when `OnDeinit` throws an exception after a clean tick loop.
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:221` — No unit test validates `MaxDrawdownPercent` when a large initial percentage drawdown is followed by a higher nominal dollar drawdown at larger equity scales.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 156.2s | 240184 tok | id=217fe1ae-cc48-4856-a6e7-b47e84ae2a7c
