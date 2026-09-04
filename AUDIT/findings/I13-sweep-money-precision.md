---
agent_id: I13
lane: sweep-money-precision
scope:
  - src/Runtime/YO4X.Mql5.Runtime
  - src/Runtime/YO4X.Mql5.Engine
  - src/Runtime/YO4X.Mql5.CodeGen
  - src/Runtime/YO4X.Trading.Mt5
  - src/Modules/Risk/YO4X.Risk
  - src/Infrastructure/YO4X.ControlPlane.Postgres
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations
  - src/Tools/YO4X.Backtest.Runner
  - src/Frontend/YO4X.Web
status: COMPLETE
generated: 2026-08-29T08:31:00Z
counts: { P0: 3, P1: 5, P2: 4, P3: 2 }
---

# I13 — sweep-money-precision

## Scope audited

The following files were reviewed across the tree for numeric precision, rounding direction, money calculations, lot sizing, and database schema types:

- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs` (228 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs` (514 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs` (584 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs` (485 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs` (463 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs` (642 lines)
- `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs` (484 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` (1079 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs` (89 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Position.cs` (53 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5ClosedTrade.cs` (51 lines)
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs` (265 lines)
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunOptions.cs` (67 lines)
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs` (320 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs` (112 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/MovingAverageCalculator.cs` (96 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs` (78 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs` (85 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs` (47 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5CciIndicator.cs` (45 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5DeMarkerIndicator.cs` (68 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs` (107 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StdDevIndicator.cs` (64 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1817 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs` (143 lines)
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs` (132 lines)
- `src/Modules/Risk/YO4X.Risk/NumericRiskPolicy.cs` (488 lines)
- `src/Modules/Risk/YO4X.Risk/NumericRiskEvaluation.cs` (713 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql` (147 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql` (159 lines)
- `src/Tools/YO4X.Backtest.Runner/Program.cs` (680 lines)
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines)
- `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts` (275 lines)

## Verdict

The policy risk engine (`YO4X.Risk`) and MT5 mapping layers are disciplined, employing exact `decimal` calculations and explicit bounds checking. However, the trading runtime and backtest simulation paths exhibit critical floating-point truncation defects, unrounded lot step operations, missing margin calculation implementations, and restrictive 2-decimal database schemas that break sizing calculations and reject fractional/crypto instruments.

## Findings

### [P0] Float truncation in `CAccountInfo.MaxLotCheck` causes 33% sizing loss and lot step divergence
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:137-141`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          double step = runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolVolumeStep);
          if (step > 0.0)
          {
              volume = step * Math.Floor(volume / step);
          }
  ```
- **Failure:** When calculating maximum affordable volume for `volume = 0.3` lots with `step = 0.1`, IEEE-754 floating point division computes `0.3 / 0.1` as `2.9999999999999996`. `Math.Floor(2.9999999999999996)` returns `2.0`, resulting in `volume = 0.2` lots instead of `0.3` lots (an immediate 33.3% sizing loss). Similarly, `0.29 / 0.01` yields `28.999999999999996`, flooring to `28.0` (`0.28` lots) and generating floating-point residue (`0.28000000000000003`) that fails simulated broker step validation (`Math.Abs(steps - Math.Round(steps)) > 1e-6`).
- **Fix:** Add an epsilon tolerance before taking the floor (`Math.Floor((volume / step) + 1e-9)`) and re-normalize the product to symbol volume digits.

### [P0] Unimplemented `OrderCalcMargin` in backtesting context breaks standard library position sizing
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:257-261`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      bool OrderCalcMargin(int orderType, string symbol, double volume, double price, out double margin)
      {
          margin = 0;
          return false;
      }
  ```
- **Failure:** `Mql5MarketContext` in `YO4X.Mql5.Engine` does not override `OrderCalcMargin` or `OrderCalcProfit`, inheriting default interface methods that unconditionally return `false` with `margin = 0`. In `Mql5AccountInfo.cs:124`, `if (!runtime.OrderCalcMargin(...))` always evaluates to `true`, prints `"CAccountInfo::MaxLotCheck margin calculation failed"`, and returns `0.0`. Every strategy using `CAccountInfo.MaxLotCheck` or `MarginCheck` in backtests fails to open any position.
- **Fix:** Implement `OrderCalcMargin` and `OrderCalcProfit` in `Mql5MarketContext` by delegating to `Mql5SymbolSpec.MarginOf` and `Mql5SymbolSpec.ProfitOf`.

### [P0] `NormalizeVolume` rounds half away from zero instead of rounding down, violating margin limits
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:65-66`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          double steps = Math.Round(volume / VolumeStep, MidpointRounding.AwayFromZero);
          return Math.Round(steps * VolumeStep, 8, MidpointRounding.AwayFromZero);
  ```
- **Failure:** When sizing a trade to match available free margin, an unrounded lot calculation of `1.005` lots on a symbol with `VolumeStep = 0.01` is rounded up to `1.01` lots by `MidpointRounding.AwayFromZero`. The required margin for `1.01` lots exceeds account free margin, causing `Mql5SimulatedBroker.HasMarginFor` to fail and rejecting the order.
- **Fix:** Change `NormalizeVolume` to round down (floor to nearest volume step) rather than rounding half away from zero.

### [P1] Hardcoded 2-decimal rounding in `CAccountInfo.MaxLotCheck` breaks fractional and crypto lots
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:135`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          double volume = Math.Round(FreeMargin() * percent / 100.0 / margin, 2, MidpointRounding.AwayFromZero);
  ```
- **Failure:** For crypto symbols (e.g. BTCUSD where `VolumeMin = 0.001` or `0.0001`), if margin calculations determine that the account can carry `0.004` BTC, `Math.Round(..., 2)` rounds it to `0.00`, causing line 143 (`volume < SymbolVolumeMin`) to abort the trade. Conversely, an affordable size of `0.006` BTC rounds up to `0.01` BTC (a 66.7% risk overshoot).
- **Fix:** Remove hardcoded 2-decimal rounding; normalize directly against `SymbolVolumeStep` and `SymbolVolumeMin`.

### [P1] PostgreSQL `numeric(12,2)` volume columns and projection validator reject fractional lot sizes
- **Where:** `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2351-2355`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          return decimal.Round(value, 2) == value
              ? value
              : throw new DomainException(
                  RequestInvalidCode,
                  "The volume must not carry more than two decimal places.");
  ```
- **Failure:** For broker instruments configured with crypto or fractional volumes (e.g. `volume_step = 0.001` or `volume_min = 0.005`), saving a bot configuration with `volume = 0.025` throws a `422 Unprocessable Entity` domain exception. Furthermore, database columns `bots.bots.volume`, `bots.broker_symbols.volume_min/max/step`, and `journal.trades.volume` are constrained to `numeric(12,2)`, rejecting sub-cent trade sizes.
- **Fix:** Update schema volume columns to `numeric(18,8)` and update `RequireTradableVolume` to allow up to 8 decimal places.

### [P1] `NormalizePrice` ignores symbol `TickSize` and `TickSize` is hardcoded to `Point`
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:19, 55`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public double TickSize => Point;

      public double NormalizePrice(double price) => Math.Round(price, Digits, MidpointRounding.AwayFromZero);
  ```
- **Failure:** On futures and index instruments where `Digits = 2` (`Point = 0.01`) but `TickSize = 0.25` (e.g. S&P 500 / ES or DAX), `NormalizePrice(4500.12)` returns `4500.12`. The price is not normalized to the broker's tick size grid, causing off-tick order rejections when dispatched to live execution gateways.
- **Fix:** Allow independent specification of `TickSize` and round prices to multiples of `TickSize` (`Math.Round(price / TickSize, MidpointRounding.AwayFromZero) * TickSize`).

### [P1] Floating-point cancellation drift in `RollingWindow.Add` causes indicator numerical drift
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:27-38`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (IsFull)
          {
              Sum -= items[head];
          }
          else
          {
              Count++;
          }

          items[head] = value;
          head = (head + 1) % items.Length;
          Sum += value;
  ```
- **Failure:** Successive subtraction and addition of floating-point numbers over 100,000 backtest bars accumulates precision drift in `Sum`. When processing flat price series (e.g. price `1.10000`), `Sum` drifts away from `Count * 1.10000` by `~1e-13`, causing `window.Sum / Count` in `Mql5CciIndicator` and `Mql5RelativeVigorIndexIndicator` to fail zero-range/flatness invariants.
- **Fix:** Recompute `Sum` by directly summing window elements or utilize compensated summation (Kahan).

### [P1] `Mql5RelativeVigorIndexIndicator` produces dimensional unit error on zero denominator
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs:64`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          double main = denominator == 0.0 ? numerator : numerator / denominator;
  ```
- **Failure:** When 4 consecutive bars have zero range (`High == Low`), `denominator` sums to `0.0`. Instead of returning `0.0`, `main` is assigned `numerator` directly (`(Close - Open)` in price currency/points, e.g. `0.0025`). RVI is an oscillator normalized near [-1, 1], and returning an unscaled price delta corrupts strategy signal thresholds. Additionally, exact float equality `denominator == 0.0` fails if `RollingWindow` drift leaves `denominator = 1e-16`.
- **Fix:** Return `0.0` when `Math.Abs(denominator) < 1e-12`.

### [P2] Trade journal database schema `numeric(18,5)` truncates sub-pip and crypto prices
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql:323-324`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
      entry_price numeric(18,5) not null check (entry_price >= 0),
      exit_price numeric(18,5) check (exit_price >= 0),
  ```
- **Failure:** For crypto symbols priced below $1 (e.g. DOGE, SHIB, ADA at `$0.00001234`) or 6-digit fractional pip forex feeds, storing trade records in PostgreSQL truncates prices beyond 5 decimal places to `0.00001`, corrupting recorded execution history and P&L attribution.
- **Fix:** Migrate `entry_price` and `exit_price` in `journal.trades` to `numeric(18,8)`.

### [P2] Unclamped `double.PositiveInfinity` in `Mql5RunReport.ProfitFactor` breaks JSON serialization
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:235`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              profitFactor = grossProfit > 0.0 ? double.PositiveInfinity : 0.0;
  ```
- **Failure:** When a backtest has winning trades and zero losing trades (`grossLoss == 0.0`), `ProfitFactor` is set to `double.PositiveInfinity`. Standard `System.Text.Json` serialization throws `JsonException: Infinity is not a valid JSON numeric literal` when sending `Mql5RunReport` across HTTP or IPC boundaries.
- **Fix:** Clamp `profitFactor` to a defined maximum numeric bound (e.g. `9999.99m`) when `grossLoss == 0.0`.

### [P2] Web frontend restricts EA magic numbers to signed 32-bit `2_147_483_647` instead of `ulong`
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1940`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  export const botMagicNumberBound = 2_147_483_647;
  ```
- **Failure:** In MetaTrader 5, `magic_number` is a 64-bit unsigned integer (`ulong`), frequently constructed from hash codes (e.g. `0x87654321` = `2,271,560,481`). Inputting any magic number greater than `2,147,483,647` into the bot settings form triggers validation failure (`"Enter a whole magic number between 0 and 2147483647"`), preventing users from configuring valid EAs.
- **Fix:** Update `botMagicNumberBound` to `Number.MAX_SAFE_INTEGER` (`9_007_199_254_740_991`) in frontend contracts and form validation.

### [P2] `ValidatePendingPrice` rejects valid limit orders at market price when `StopsLevelPoints == 0`
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:841`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              Mql5OrderType.BuyLimit => ask - price >= minimum && ask - price > tolerance,
  ```
- **Failure:** On ECN accounts where `StopsLevelPoints = 0` (`minimum = 0.0`), `tolerance` is `0.5 * Point`. Placing a `BuyLimit` order at the current Ask price evaluates `ask - price = 0.0`. While `ask - price >= minimum` is satisfied, `ask - price > tolerance` evaluates to `false`, erroneously rejecting the order with `"BuyLimit at ... is on the wrong side of the market"`.
- **Fix:** Remove the strict `> tolerance` requirement when `minimum == 0.0`, testing `ask - price >= minimum - tolerance`.

### [P3] Unrounded `Mql5ClosedTrade.NetProfit` sum causes floating-point drift against broker balance
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5ClosedTrade.cs:49`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public double NetProfit => GrossProfit + Commission + Swap;
  ```
- **Failure:** In `Mql5SimulatedBroker.ClosePortion`, balance updates via `Round2(balance + gross + commission + swap)` on each close. Because `Mql5ClosedTrade.NetProfit` is unrounded, summing `trade.NetProfit` in `Mql5StrategyHost.Summarize` (`grossProfit += net`) across 10,000 backtest trades accumulates floating-point cents drift against `FinalBalance - InitialDeposit`.
- **Fix:** Define `NetProfit => Math.Round(GrossProfit + Commission + Swap, 2, MidpointRounding.AwayFromZero);`.

### [P3] Inconsistent banker's rounding in `TryStorableEquity` vs away-from-zero rounding in broker simulator
- **Where:** `src/Tools/YO4X.Backtest.Runner/Program.cs:342`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          equity = Math.Round((decimal)value, 4, MidpointRounding.ToEven);
  ```
- **Failure:** Throughout the engine, price, volume, and balance rounding use `MidpointRounding.AwayFromZero`. In `TryStorableEquity`, equity curve points use `MidpointRounding.ToEven`. For exact half-way tie values (e.g. `$10000.00005`), the runner rounds to `$10000.0000` while the engine and run report round to `$10000.0001`.
- **Fix:** Use `MidpointRounding.AwayFromZero` in `TryStorableEquity`.

## Referrals

None.

## Coverage gaps

- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:135-141`: Untested for volume steps not equal to `0.01` and lot calculations where `volume / step` produces floating point representation `N.9999999999999996`.
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs:1-320`: Untested branch where strategies invoke `runtime.OrderCalcMargin()` or `runtime.OrderCalcProfit()` directly during backtests.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2351`: Untested with sub-cent lot sizes (`volume = 0.001` or `0.005`) when configuring crypto or micro-futures bots.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 284.3s | 745514 tok | id=4f1f42c1-9278-40d3-a90f-ced14e0ce801
