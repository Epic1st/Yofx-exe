---
agent_id: A09
lane: dashboard
scope:
  - src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx
status: COMPLETE
generated: 2026-08-29T08:51:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 1 }
---

# A09 — dashboard

## Scope audited
- `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx` (410 lines) — primary audit scope.
- `src/Frontend/YO4X.Web/src/features/dashboard/dashboard.css` (234 lines) — layout and theme tokens context.
- `src/Frontend/YO4X.Web/src/features/strategies/StrategyCard.tsx` (105 lines) — strategy preview card context.
- `src/Frontend/YO4X.Web/src/app/useResource.ts` (74 lines) — data loader lifecycle context.
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines) — dashboard summary and bot view contracts.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts` (770 lines) — API client query interface.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines) — backend summary projection context.

## Verdict
The `DashboardPage.tsx` component is cleanly structured and handles resource loading/error boundaries well for primary layout elements. However, three defects were identified: (1) a P1 currency formatting bug in the fallback path of `formatMoney` where zero profit/loss is rendered with a positive `+` sign (`+0.00`) for non-ISO currencies such as USDT or BTC; (2) a P2 UI state glitch where category filter buttons unmount and disappear entirely during catalog refetches and remain missing on network errors; and (3) a P3 semantic inconsistency where the "Running now" table column header is labeled "Strategy" while displaying only the custom bot instance name `bot.name` instead of `bot.strategyName`.

## Findings

### [P1] `formatMoney` fallback prepends positive `+` sign to zero P/L (`+0.00`) for non-ISO currencies
- **Where:** `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:90`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  function formatMoney(amount: number, currency: string): string {
    try {
      return new Intl.NumberFormat('en-GB', {
        style: 'currency',
        currency,
        signDisplay: 'exceptZero',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      }).format(amount);
    } catch {
      return `${amount >= 0 ? '+' : ''}${amount.toFixed(2)} ${currency}`;
    }
  }
  ```
- **Failure:** When a bot trades on an account denominated in a cryptocurrency or non-ISO currency code (such as `USDT`, `BTC`, or `ETH`), `new Intl.NumberFormat` throws a `RangeError: Invalid currency code` and enters the `catch` block. For zero profit/loss (`amount = 0` or `-0`), `amount >= 0` evaluates to `true`, returning `+0.00 USDT`. For standard ISO currencies, `signDisplay: 'exceptZero'` correctly formats zero as `US$0.00` without a sign. In the fallback path, displaying `+0.00` falsely indicates positive profit to the trader for a flat/zero P/L bot.
- **Fix:** In `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:90`, replace `amount >= 0 ? '+' : ''` with `amount > 0 ? '+' : ''`.

### [P2] Category filter chips unmount from DOM during catalog queries and are permanently lost on errors
- **Where:** `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:178, 345-356`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const catalogValue = catalog.state.status === 'ready' ? catalog.state.value : null;
  ```
  and
  ```typescript
  {(catalogValue?.categories ?? []).map((name) => (
    <button
      key={name}
      type="button"
      className={category === name ? 'chip chip--active' : 'chip'}
      aria-pressed={category === name}
      onClick={() => setCategory(category === name ? null : name)}
    >
      {name}
    </button>
  ))}
  ```
- **Failure:** When the user clicks a category chip (e.g. "Scalping") on the dashboard, `setCategory` changes `category`, causing `useResource` to transition `catalog.state.status` to `'loading'`. `catalogValue` immediately becomes `null`, which causes `(catalogValue?.categories ?? [])` to evaluate to `[]`. All category filter chips (except "All") are unmounted from the DOM while the request is in flight. If the network request fails, `catalog.state.status` transitions to `'error'`, leaving `catalogValue` as `null` and trapping the user in the selected category filter with no chips available to switch to another category.
- **Fix:** Retain the previously loaded `categories` array across loading and error states so filter chips remain mounted and operable.

### [P3] "Running now" table column header "Strategy" displays `bot.name` instead of `bot.strategyName`
- **Where:** `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:127, 295`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  <div className="table__head" style={{ gridTemplateColumns: runningColumns }}>
    <div>Strategy</div>
    <div>Symbol</div>
  ```
  and
  ```typescript
  <div className="dashboard-row__name">
    <span
      className={dotClassName(bot.status)}
      role="img"
      aria-label={statusLabels[bot.status]}
    />
    <span className="dashboard-row__name-text">{bot.name}</span>
  </div>
  ```
- **Failure:** When a user launches a bot named "Aggressive Scalper 1" running strategy "MACD Divergence", the dashboard table header labels the first column as "Strategy", but the row renders only `bot.name` ("Aggressive Scalper 1"). The underlying strategy name is omitted. Furthermore, the "Inspect" button calls `onInspect(bot.strategyId)` to open the strategy's catalog detail page, which is confusing when the row only displays the custom bot name.
- **Fix:** Display both `bot.name` and `bot.strategyName` in `RunningRow` or rename the column header to "Bot" to match `BotsPage.tsx`.

## Referrals
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:1965, 1986` — `live-bots` and `cloud-runners` stat cards emit `direction: Up` whenever `count > 0`, causing static total labels like `"1 configured"` and `"1 provisioned"` to be rendered in positive green (`--color-positive-text`) rather than neutral/flat. (Referral to `D07` / `B16`).
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx:57` — `formatSignedAmount` fallback path omits the `+` sign for positive amounts on non-ISO currencies (`${amount.toFixed(2)} ${currency}`), inconsistent with `signDisplay: 'exceptZero'`. (Referral to `A12`).

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:90` — The fallback branch of `formatMoney` when `currency` is not recognized by `Intl.NumberFormat` has zero test coverage in `src/Frontend/YO4X.Web`.
- `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:345-356` — Category filtering and chip unmounting behavior during loading and error states has no unit or integration tests.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 136.1s | 307256 tok | id=396e4e52-de16-4089-a1f1-9a54e1cc0002
