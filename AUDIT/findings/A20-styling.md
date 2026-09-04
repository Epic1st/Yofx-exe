---
agent_id: A20
lane: styling
scope:
  - src/Frontend/YO4X.Web/src/app/styles/global.css
  - src/Frontend/YO4X.Web/src/app/styles/tokens.css
  - src/Frontend/YO4X.Web/src/auth/auth.css
  - src/Frontend/YO4X.Web/src/features/backtests/backtests.css
  - src/Frontend/YO4X.Web/src/features/bots/bots.css
  - src/Frontend/YO4X.Web/src/features/cloud/cloud.css
  - src/Frontend/YO4X.Web/src/features/compiler/compiler.css
  - src/Frontend/YO4X.Web/src/features/dashboard/dashboard.css
  - src/Frontend/YO4X.Web/src/features/journal/journal.css
  - src/Frontend/YO4X.Web/src/features/overlays/overlays.css
  - src/Frontend/YO4X.Web/src/features/settings/settings.css
  - src/Frontend/YO4X.Web/src/features/strategies/catalog.css
  - src/Frontend/YO4X.Web/src/features/strategies/detail.css
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 2, P2: 4, P3: 1 }
---

# A20 — styling

## Scope audited
Audited all 13 CSS stylesheets defining the frontend styling layer (5,090 total lines):
- `src/Frontend/YO4X.Web/src/app/styles/tokens.css` (111 lines)
- `src/Frontend/YO4X.Web/src/app/styles/global.css` (1,189 lines)
- `src/Frontend/YO4X.Web/src/auth/auth.css` (223 lines)
- `src/Frontend/YO4X.Web/src/features/backtests/backtests.css` (534 lines)
- `src/Frontend/YO4X.Web/src/features/bots/bots.css` (363 lines)
- `src/Frontend/YO4X.Web/src/features/cloud/cloud.css` (269 lines)
- `src/Frontend/YO4X.Web/src/features/compiler/compiler.css` (137 lines)
- `src/Frontend/YO4X.Web/src/features/dashboard/dashboard.css` (234 lines)
- `src/Frontend/YO4X.Web/src/features/journal/journal.css` (79 lines)
- `src/Frontend/YO4X.Web/src/features/overlays/overlays.css` (1,039 lines)
- `src/Frontend/YO4X.Web/src/features/settings/settings.css` (190 lines)
- `src/Frontend/YO4X.Web/src/features/strategies/catalog.css` (242 lines)
- `src/Frontend/YO4X.Web/src/features/strategies/detail.css` (580 lines)

## Verdict
The design system exhibits strong syntactic discipline overall with nearly universal token usage, semantic class naming, and clean separation between layout and primitives. However, there are critical accessibility and functional defects: `.backtests-profit` statically binds profit-green color to the entire net profit column so that backtest losses render in green; several core muted/faint text tokens fail WCAG AA contrast (dropping as low as 2.27:1 on white and raised surfaces); key strategy catalog and dashboard grids lack responsive media queries, squishing cards below 1200px; and profit/loss signals on equity curves and uptime bars rely entirely on color without secondary cues.

## Findings

### [P1] `.backtests-profit` statically hardcodes profit green, displaying net backtest losses in green
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtests.css:61-65`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
  .backtests-profit {
    color: var(--color-positive-text);
    font-size: 12px;
    font-weight: 500;
  }
  ```
- **Failure:** In `BacktestsPage.tsx:168-172`, every backtest row applies `className="backtests-profit mono"` to the net profit cell. Because `.backtests-profit` sets `color: var(--color-positive-text)` statically without modifier classes (unlike `.bots-pl` in `bots.css` or `.dashboard-row__pl` in `dashboard.css`), any backtest run that produced a net loss (e.g. `-$2,450.00`) is rendered in positive green (`#1f7a45`). Traders reviewing backtest history are presented with green loss figures, obscuring unprofitable strategy outcomes.
- **Fix:** Remove the unconditional `color` declaration from `.backtests-profit`, define `.backtests-profit--positive` (`color: var(--color-positive-text);`) and `.backtests-profit--negative` (`color: var(--color-negative);`) modifiers, and apply them dynamically in `BacktestsPage.tsx`.

### [P1] WCAG AA contrast failure (2.37:1) on `--color-text-faint` across footnotes, disclosures, and pricing terms
- **Where:** `src/Frontend/YO4X.Web/src/app/styles/tokens.css:38`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
    --color-text-muted: #8b9199;
    --color-text-faint: #a3a8b2;

    /* Accent */
  ```
- **Failure:** `--color-text-faint` (`#a3a8b2`) has relative luminance $L = 0.3935$. Against `--color-surface` (`#ffffff`), its contrast ratio is **2.37:1**; against `--color-surface-raised` (`#fafbfc`), it is **2.27:1**. WCAG 2.1 AA (SC 1.4.3) mandates a minimum contrast ratio of 4.5:1 for text. UI text styled with this token—including legal footnotes in `auth/auth.css:148` (`.auth-entry__card small`), strategy pricing terms in `features/strategies/catalog.css:95` (`.strategy-card__price-note`), brand descriptors in `auth/auth.css:207` (`.brand-mark__descriptor`), and empty backtest cells in `features/backtests/backtests.css:77` (`.backtests-absent`)—fails contrast thresholds and is unreadable for users with low vision or on low-contrast displays.
- **Fix:** Darken `--color-text-faint` to `#656c78` ($L \le 0.155$, contrast $\ge 4.5:1$) or replace text usages with `--color-text-tertiary`.

### [P2] WCAG AA contrast failure (3.02:1 - 3.84:1) for table headers, subtitles, and form hints
- **Where:** `src/Frontend/YO4X.Web/src/app/styles/tokens.css:36-37`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
    --color-text-quaternary: #7b8290;
    --color-text-muted: #8b9199;
    --color-text-faint: #a3a8b2;
  ```
- **Failure:** `--color-text-muted` (`#8b9199`, $L = 0.2834$) produces a **3.15:1** contrast ratio against `#ffffff` and **3.02:1** on `--color-surface-raised` (`#fafbfc`). `--color-text-quaternary` (`#7b8290`, $L = 0.2232$) yields a **3.84:1** contrast ratio. Both fall below the WCAG AA 4.5:1 requirement for standard text. Consequently, table column headers across the entire platform (`.table__head` in `global.css:358`), page subtitles (`.page-subtitle` in `global.css:235`), empty state details (`.empty-state__detail` in `global.css:720`), bot account numbers (`.bots-bot__account` in `bots.css:19`), and form input hints (`.nb-hint` in `backtests.css:213`) fail accessibility compliance.
- **Fix:** Adjust `--color-text-quaternary` to `#5e6573` (4.6:1) and `--color-text-muted` to `#606775` (4.5:1) to ensure all descriptive labels satisfy WCAG AA on light surfaces.

### [P2] Missing responsive grid rules in catalog, dashboard, and strategy detail squish content on screens < 1200px
- **Where:** `src/Frontend/YO4X.Web/src/features/strategies/catalog.css:182-185`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
  .catalog-grid {
    display: grid;
    grid-template-columns: repeat(6, 1fr);
    gap: 12px;
  }
  ```
- **Failure:** Unlike `compiler.css:132` (`@media (max-width: 900px)`) and `bots.css:137`, stylesheets `catalog.css:182` (`.catalog-grid`), `dashboard.css:230` (`.dashboard-cards`), `dashboard.css:16` (`.dashboard-stats`), and `detail.css:378` (`.detail-figures`) hardcode 6-column or 4-column grid templates without `@media` queries. On viewport widths between 768px and 1200px (e.g. tablet or split-screen desktop with a 236px sidebar), strategy cards are compressed to <110px width and detail metric tiles to <50px width, causing star ratings, prices, titles, and numeric values to clip and overflow horizontally.
- **Fix:** Add responsive breakpoint queries (`@media (max-width: 1200px)`, `@media (max-width: 900px)`, `@media (max-width: 640px)`) to `catalog.css`, `dashboard.css`, and `detail.css` to scale column counts from 6 to 4, 3, 2, and 1.

### [P2] `.detail-meta__verified` uses graphical `--color-positive` token instead of typography token
- **Where:** `src/Frontend/YO4X.Web/src/features/strategies/detail.css:305-307`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
  .detail-meta__verified {
    color: var(--color-positive);
  }
  ```
- **Failure:** `--color-positive` (`#1f9d55`, $L = 0.2533$) is designed for icon fills and indicator dots. Applied to 12.5px text in `.detail-meta__verified`, its contrast against `--color-surface` (`#ffffff`) is **3.46:1**, failing WCAG AA (4.5:1). The design system provides `--color-positive-text` (`#1f7a45`, $L = 0.1464$, 5.35:1 contrast) specifically for green typography (as used in `global.css:246`).
- **Fix:** Change `color: var(--color-positive);` to `color: var(--color-positive-text);` in `features/strategies/detail.css:306`.

### [P2] Color is the sole visual signal distinguishing profit from loss on equity curves and bot uptime status
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtests.css:494-500`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
  .bd-chart__line--positive {
    stroke: var(--color-positive);
  }

  .bd-chart__line--negative {
    stroke: var(--color-negative);
  }
  ```
- **Failure:** In `BacktestDetail.tsx:354`, the equity curve polyline switches between `.bd-chart__line--positive` and `.bd-chart__line--negative`. Both classes share identical 2px solid stroke styling with no difference in dash pattern, marker, or baseline shading. Similarly, in `bots.css:97-107`, uptime bars (`.bots-uptime__bar--full`, `--partial`, `--down`) rely solely on green, amber, and red background colors. Under protanopia and deuteranopia (red-green color blindness), the positive and negative strokes are indistinguishable in hue, violating WCAG 2.1 SC 1.4.1 (Use of Color).
- **Fix:** Provide secondary non-color indicators: use distinct stroke-dasharray patterns (e.g. solid for overall gain, dashed for loss), add clear directional glyphs/badges (+▲ / -▼) alongside axis endpoints, and ensure uptime bars include accessible state labels.

### [P3] Hardcoded literal RGB value in `global.css` bypasses design tokens
- **Where:** `src/Frontend/YO4X.Web/src/app/styles/global.css:334`
- **Confidence:** CONFIRMED
- **Code:**
  ```css
  .thumb__label {
    padding: 3px 7px;
    border-radius: var(--radius-tiny);
    background: rgb(255 255 255 / 86%);
    color: var(--color-text-tertiary);
    font-family: var(--font-mono);
    font-size: 10px;
  }
  ```
- **Failure:** Line 6 of `global.css` specifies: "Every value here comes from `tokens.css`. Never introduce a literal colour: if a shade is missing, derive it from an existing token with `color-mix()`." The literal `rgb(255 255 255 / 86%)` bypasses tokens and will not adapt if surface base colors are customized.
- **Fix:** Replace `rgb(255 255 255 / 86%)` with `color-mix(in srgb, var(--color-surface) 86%, transparent)`.

## Referrals
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestsPage.tsx:168` — Unconditionally renders `<span className="backtests-profit mono">` without checking whether `netProfitAmount` is negative.
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestsPage.tsx:197` — Mounts `<NewBacktestModal>` inside the scrolling page element rather than through the top-level `AppShell` overlay portal (`app-frame__overlay`).

## Coverage gaps
- No automated CSS regression or axe-core contrast test suite verifies WCAG AA compliance across text tokens on `--color-surface`, `--color-surface-raised`, and `--color-surface-sunken`.
- No responsive layout tests assert that catalog grids, dashboard stat tiles, or backtest tables remain unclipped on viewport widths between 640px and 1200px.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 196.6s | 376743 tok | id=675f87ea-f81f-4984-b4f8-94761927414e
