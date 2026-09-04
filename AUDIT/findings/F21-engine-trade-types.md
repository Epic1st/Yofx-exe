---
agent_id: F21
lane: engine-trade-types
scope:
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5ClosedTrade.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Constants.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Enums.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5OrderEvent.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5PendingOrder.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Position.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5TradeRequest.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5TradeResult.cs
status: COMPLETE
generated: 2026-08-29T11:24:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 2 }
---

# F21 — engine-trade-types

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5ClosedTrade.cs` (51 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Constants.cs` (242 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Enums.cs` (169 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5OrderEvent.cs` (49 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5PendingOrder.cs` (41 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Position.cs` (53 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs` (106 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5TradeRequest.cs` (66 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5TradeResult.cs` (36 lines)

## Verdict
The core trading domain models, specifications, and property constant dictionaries in this lane are largely solid and well-structured. Property identifiers in `Mql5Constants.cs` align with compiler-measured MQL5 property values, and the P&L arithmetic in `Mql5SymbolSpec.ProfitOf` and margin sizing in `MarginOf` correctly account for tick size, tick value, contract size, and quote-to-deposit exchange conversion. However, one enum ordinal divergence exists where `Mql5MarginMode.Hedging` assigns `1` instead of `2` (the MQL5 value for `ACCOUNT_MARGIN_MODE_RETAIL_HEDGING`), causing `AccountInfoInteger(ACCOUNT_MARGIN_MODE)` queries to misreport hedging accounts as exchange netting. Additionally, `NormalizeVolume` floors small negative volume residues into negative lots, `NetProfit` on closed trades computes unrounded floating-point additions, and `Mql5Position` lacks explicit closed-state invalidation.

## Findings

### [P1] `Mql5MarginMode.Hedging` assigns ordinal 1 instead of 2, misidentifying hedging accounts as exchange netting
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Enums.cs:55-62`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public enum Mql5MarginMode
  {
      /// <summary>One position per symbol; opposite deals net against it.</summary>
      Netting = 0,

      /// <summary>Independent positions per deal; opposing positions coexist.</summary>
      Hedging = 1,
  }
  ```
- **Failure:** In standard MQL5 (`ENUM_ACCOUNT_MARGIN_MODE`), `ACCOUNT_MARGIN_MODE_RETAIL_NETTING = 0`, `ACCOUNT_MARGIN_MODE_EXCHANGE = 1`, and `ACCOUNT_MARGIN_MODE_RETAIL_HEDGING = 2`. When a backtest or live execution runs with `Mql5MarginMode.Hedging`, querying `AccountInfoInteger(ACCOUNT_MARGIN_MODE)` via `Mql5MarketContext.cs:159` evaluates `(long)options.MarginMode` and returns `1L`. Any EA or standard library class checking `AccountInfoInteger(ACCOUNT_MARGIN_MODE) == ACCOUNT_MARGIN_MODE_RETAIL_HEDGING` (which checks for `2L`) evaluates to `false` and believes the account is running in exchange netting mode (`ACCOUNT_MARGIN_MODE_EXCHANGE`), causing hedging logic to fail to activate.
- **Fix:** Explicitly assign `Hedging = 2` (and optionally `Exchange = 1`) in `Mql5MarginMode` to maintain exact ordinal parity with MQL5's `ENUM_ACCOUNT_MARGIN_MODE`.

### [P2] `Mql5SymbolSpec.NormalizeVolume` floors negative sub-step residues to negative full volume steps
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:75-84`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public double NormalizeVolume(double volume)
      {
          if (VolumeStep <= 0.0)
          {
              return volume;
          }

          double steps = Math.Floor((volume / VolumeStep) + 1e-9);
          return Math.Round(steps * VolumeStep, 8, MidpointRounding.AwayFromZero);
      }
  ```
- **Failure:** When `NormalizeVolume` is called with a small negative volume residue (e.g. `volume = -0.001` with `VolumeStep = 0.01`, as can arise during floating-point volume arithmetic in order netting or position sizing), `volume / VolumeStep + 1e-9` computes `-0.099999999`. Calling `Math.Floor` rounds down to `-1.0`, returning `-0.01` instead of `0.0`. Passing negative or sub-zero residual amounts to `NormalizeVolume` produces negative lot sizes instead of clamping to zero.
- **Fix:** Guard against non-positive inputs by returning `0.0` when `volume <= 0.0` or clamping `steps` to non-negative values.

### [P3] `Mql5ClosedTrade.NetProfit` does not round floating-point sum, risking epsilon drift in trade outcomes
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5ClosedTrade.cs:49`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      /// <summary>Gets the profit after commission and swap. This is what hits the balance.</summary>
      public double NetProfit => GrossProfit + Commission + Swap;
  ```
- **Failure:** While `GrossProfit`, `Commission`, and `Swap` are individually rounded to 2 decimal places and the broker balance is updated via `Round2(balance + gross + commission + swap)`, `Mql5ClosedTrade.NetProfit` adds the three IEEE 754 `double` values directly without rounding. For a break-even trade with `GrossProfit = 10.10`, `Commission = -10.10`, and `Swap = 0.0`, `NetProfit` can produce a non-zero epsilon (such as `1e-16`). When downstream reporting logic in `Mql5StrategyHost.cs:201-208` tests `if (net > 0.0) { wins++; }`, a flat break-even trade can be incorrectly categorized as a winning trade.
- **Fix:** Round `NetProfit` to 2 decimal places using `Math.Round(GrossProfit + Commission + Swap, 2, MidpointRounding.AwayFromZero)`.

### [P3] `Mql5Position` is an unencapsulated mutable DTO lacking lifecycle state invariants
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Position.cs:4-16`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed class Mql5Position
  {
      /// <summary>Gets the position ticket.</summary>
      public long Ticket { get; internal set; }

      /// <summary>Gets the symbol.</summary>
      public string Symbol { get; internal set; } = string.Empty;

      /// <summary>Gets the direction.</summary>
      public Mql5PositionType Type { get; internal set; }

      /// <summary>Gets the open volume in lots.</summary>
      public double Volume { get; internal set; }
  ```
- **Failure:** `Mql5Position` exposes purely mutable properties without lifecycle invariants. When a position is closed in `ClosePortion` (`Mql5SimulatedBroker.cs:1100-1105`), its `Volume` and `Margin` are set to `0.0` and it is removed from the active positions collection. However, existing references (such as `Mql5MarketContext.selected`) remain pointed to the instance where `Volume == 0.0` but `Ticket`, `PriceOpen`, `StopLoss`, and `TakeProfit` retain non-zero values, without an `IsOpen` property or state flag to signal that the position is closed.
- **Fix:** Add an `IsOpen` boolean property to `Mql5Position` set to `false` on position closure, or clear ticket and pricing fields when volume drops to zero.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs:159` — `AccountInfoInteger(ACCOUNT_MARGIN_MODE)` casts `options.MarginMode` directly to `long` without mapping to MQL5 `ENUM_ACCOUNT_MARGIN_MODE`.
- `tests/YO4X.Mql5.Engine.Tests/PropertyIdentifierFidelityTests.cs:51` — `PropertyIdentifierFidelityTests` tests integer property IDs but lacks assertions verifying enum value parity for `Mql5MarginMode`, `Mql5OrderType`, and `Mql5AppliedPrice` against `Mql5BuiltinConstants`.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:61-72` (`NormalizePrice`): Untested behavior for symbols with non-decimal tick steps (e.g. `TickSize = 0.25` on index futures), verifying price quantization when `TickSize > Point`.
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:96-104` (`MarginOf`): Untested margin calculations for cross-currency instruments where `QuoteToDepositRate != 1.0` (e.g. EURGBP on USD accounts).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 129.8s | 325641 tok | id=4fa8dfd0-3a8e-4621-9bd3-c149c0539a1b
