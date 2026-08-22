# YO4X Frontend Design System

Status: dashboard slice v1, implemented 2026-08-22.

Accepted concept: `docs/frontend/design/yo4x-dashboard-concept-v1.png` at 1536 × 1024. The original user reference established the white/cobalt visual language; the accepted concept adapts it from commerce into a read-only strategy-control surface.

## Product boundary

This frontend presents ControlPlane evidence. It must not invent operational success, profit, balances, live-account capability, compatibility, or runtime state. It does not contain cart, marketplace, deposit, profit, arbitrary order, or direct broker controls. The browser never performs a trade and never calls GatewayHost directly.

Production data comes through typed clients in `src/Frontend/YO4X.Web/src/api`. Representative visual data is isolated in `src/test-fixtures`, selected only by `?fixture=dashboard` in Vite development/test mode, and excluded from production behavior.

## Exact tokens

| Role | Token | Value |
|---|---|---|
| Canvas | `--color-canvas` | `#ffffff` (true white) |
| Surface | `--color-surface` | `#ffffff` |
| Subtle surface | `--color-surface-subtle` | `#f7f9fd` |
| Selected surface | `--color-surface-selected` | `#eef4ff` |
| Primary text | `--color-text` | `#0b1b43` |
| Secondary text | `--color-text-secondary` | `#50638a` |
| Muted text | `--color-text-muted` | `#7383a2` |
| Border | `--color-border` | `#dfe6f1` |
| Strong border | `--color-border-strong` | `#ccd7e7` |
| Cobalt | `--color-cobalt` | `#075cf0` |
| Cobalt hover | `--color-cobalt-hover` | `#004bce` |
| Success | `--color-success` | `#079447` |
| Warning | `--color-warning` | `#e78a00` |
| Danger | `--color-danger` | `#e31b31` |
| Panel radius | `--radius-panel` | `6px` |
| Control radius | `--radius-control` | `5px` |
| Panel shadow | `--shadow-panel` | `0 2px 10px rgb(28 57 105 / 5%)` |
| Sidebar | `--sidebar-width` | `270px` |
| Desktop top bar | `--topbar-height` | `76px` |
| Mobile top bar | `--topbar-height` | `68px` |
| Fast motion | `--motion-fast` | `150ms` |
| Standard motion | `--motion-standard` | `220ms` |

No cream, warm-gray, decorative gradient, glow, glass card, or unapproved color overlay is allowed. The loading shimmer is a neutral state affordance, not part of the loaded product surface.

## Typography

The UI family is `Inter, "Segoe UI", Roboto, Helvetica, Arial, sans-serif`. A font download is deliberately not required. Primary panel titles are 15–16px at weight 690; metric values are 14px at weight 700; navigation and controls are 12–13px at 500–650; dense table text is 9.5–10.5px. Controls always use the UI family and explicit sizing.

## Container and spacing model

- Fixed 270px desktop sidebar, fixed visual rail, true-white background.
- 76px sticky desktop top bar.
- 22px horizontal content gutter and 14px vertical panel rhythm.
- Five equal summary tiles at the 1536px reference viewport.
- Deployment readiness is a single bordered panel with a split evidence/context layout, not nested cards.
- Strategy compatibility and operational evidence remain tables.
- Recent activity and runtime readiness form one two-column band.
- Desktop panel dimensions are locked to the accepted composition: summary 87px, deployment readiness 349px, strategy compatibility 174px, and lower evidence band 226px.

At 1120px the sidebar becomes an inert, focus-trapped off-canvas navigation. At 940px evidence context and the lower band stack. At 720px summary tiles become two columns and tables remain horizontally scrollable rather than being converted into misleading cards. At 470px summary tiles become a single column. The page itself has no horizontal overflow at 390px.

## Component families

| Family | Responsibility |
|---|---|
| `AppShell` | Sidebar, top bar, footer, help modal, mobile navigation state |
| `Sidebar` | Grouped navigation, selected state, keyboard-contained mobile drawer |
| `TopBar` | Search, environment, service notices, user context |
| `SummaryTiles` | Five high-level facts with semantic tone and a consistent icon frame |
| `DeploymentReadiness` | Ordered proofs, explicit proven/pending/blocked/unavailable states, evidence dialogs |
| `StrategyCompatibility` | Searchable table and non-authorizing report summary |
| `RecentActivity` | Time-bound ControlPlane audit/activity projection |
| `RuntimeReadiness` | Component projection with honest not-configured/unavailable states |
| `Panel`, `Status`, `Modal`, `EmptyState` | Shared structural and state primitives |
| `Icon`, `BrandMark` | One code-native SVG family with 1.8px round strokes |

Icons are purpose-built SVG components using `currentColor`; no icon package or barrel import is used. The metaphors are home, book, star, cloud upload, bank, rocket, list, user, shield, help, bell, file, folder, database, globe, status circles, and chevrons. Filled semantic dots are reserved for current states.

## Allowed visible copy

Above the fold may contain only the accepted operational vocabulary:

- `Yo4x`, `Trading Strategy Control`
- `Search strategies, accounts, deployments...`
- `Demo environment`
- `Dashboard`, `Strategy Library`, `My Strategies`, `Import & Analysis`, `Broker Accounts`, `Deployments`, `Activity`, `Sessions`, `Security`, `Help Centre`
- `Broker account`, `Strategies analyzed`, `Deployment`, `Safety policy`, `Gateway`
- `Deployment readiness`, `Every authority must be proven before execution`
- `Account binding`, `Strategy package`, `Risk policy`, `Gateway evidence`, `Reconciliation`
- `Proven`, `Pending`, `Blocked`, `Unavailable`, `View evidence`, `Review deployment`

Downstream panels may use `Strategy compatibility`, `Recent activity`, `Runtime readiness`, `Open report`, `View all activity`, and `View all components`. Error, empty, loading, and unauthorized states may use the concise recovery copy implemented with those components.

The fixture strategy and event names are representative test data only. Production never substitutes them when a projection is missing.

## API and trust rules

The current client reads `/v1/me`, optional selected broker-account and credential-state endpoints, optional selected deployment and activity endpoints, and `/health/ready`. Compatibility and runtime aggregation paths are disabled until explicitly configured. Independent reads start together. A failed optional section degrades only that section; any 401 or 403 enters the unauthorized state.

Responses are decoded at runtime. Unknown enums, malformed dates, invalid shapes, non-JSON success bodies, and RFC 7807 errors are handled explicitly. Bearer tokens may come only from the in-memory `window.__YO4X_AUTH__` bridge. No token or broker credential may enter Vite configuration, web storage, source, query strings, screenshots, logs, or fixture data.

This slice has no mutation form. Future backend actions must use typed reason-code selects and must never accept secret-capable free text. Current allowlists are:

- Connection test: `user_connection_test`
- Disable cloud use: `user_disabled_cloud_use`, `security_concern`, `account_retired`
- Credential deletion: `user_requested_credential_deletion`, `security_concern`, `account_retired`
- Start: `user_started_deployment`, `validation_complete`
- Close-only: `user_requested_close_only`, `risk_reduction`, `security_concern`
- Stop-after-flat: `user_requested_stop_after_flat`, `maintenance`, `strategy_retired`

## Accessibility and interaction

- Semantic landmarks, headings, tables, row/column headers, labels, and `aria-live` refresh text.
- Visible cobalt focus treatment on keyboard-focusable controls.
- Search uses a native labeled search input and deferred filtering.
- Evidence and reports use focus-contained modal dialogs, Escape-to-close, focus return, and backdrop close.
- Mobile navigation is inert when off canvas, moves focus inside when opened, traps Tab/Shift+Tab, closes with Escape, and restores focus.
- Status never relies on color alone; every state includes text and/or an icon.
- Horizontal tables expose a labeled keyboard-focusable scroll region.
- Motion honors `prefers-reduced-motion`.

## Fidelity inventory and verification ledger

Browser plugin tooling was not available, so regular Playwright Core 1.62.1 drove the installed Microsoft Edge browser. The dev fixture was rendered at the accepted concept’s native 1536 × 1024 viewport and at 390 × 844. Both screenshots were inspected with the accepted concept using original image detail.

| Comparison point | Concept evidence | Render evidence and disposition |
|---|---|---|
| Palette | True-white canvas, cobalt controls, navy text, green/amber/red states | Exact token family implemented; no warm background or decorative overlay |
| Skeleton | Sidebar, top bar, five tiles, large readiness panel, compatibility table, two lower tables | Same section order and table-driven container model; 270px sidebar follows the explicit implementation requirement |
| Vertical rhythm | Primary content fills one 1536 × 1024 viewport | Browser bounds: summary y=91/h=87, readiness y=192/h=349, compatibility y=555/h=174, lower band y=743/h=226, footer y=984; no viewport overflow |
| Copy | Operational control copy rather than marketplace/commercial claims | Above-the-fold copy matches the allowed inventory; commerce, balances, profits, and order controls are absent |
| Icons | Consistent thin cobalt/navy line family | One custom SVG family with round 1.8px strokes; no mixed icon library |
| State treatment | Proven, pending, blocked and runtime states are legible | Text + icon/dot + semantic color; production missing projections remain unavailable/not configured |
| Responsive behavior | Compact continuation expected from the desktop system | 390px page has no horizontal page overflow; drawer, stacked panels, scrollable tables, focus handling, and close behavior verified |
| Core interaction | Evidence review and compatibility report access | Search filtering, evidence/report dialogs, Escape close, notification/profile menus, and mobile navigation work |
| Production data boundary | Reference data must never become an operational claim | Production preview rejects `?fixture=dashboard`, renders `Dashboard unavailable` when `/v1/me` and `/health/ready` are deliberately absent, and remains fail-closed after retry at both 1536 × 1024 and 390 × 844 |

Material fixes made during visual QA: desktop-only menu controls were properly hidden; metric wrapping was corrected; readiness state columns were aligned; compatibility headings were consolidated into the concept’s compact header band; support-card placement and height were matched; favicon console noise was removed; and the page was brought within the native viewport without horizontal or vertical overflow.

Intentional deviations: the accepted concept’s exact font file was unavailable, so the documented system stack is used; the logo/horse is a clean code-native vector rather than a raster extraction; and the required 270px sidebar is slightly wider than the concept image’s measured rail. No other material mismatch remains.
