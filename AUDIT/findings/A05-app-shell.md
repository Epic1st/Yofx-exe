---
agent_id: A05
lane: app-shell
scope:
  - src/Frontend/YO4X.Web/src/app/shell/AppShell.tsx
  - src/Frontend/YO4X.Web/src/app/shell/Sidebar.tsx
  - src/Frontend/YO4X.Web/src/app/shell/TopBar.tsx
  - src/Frontend/YO4X.Web/src/app/shell/TitleBar.tsx
status: COMPLETE
generated: 2026-08-29T08:50:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 1 }
---

# A05 — app-shell

## Scope audited
- `src/Frontend/YO4X.Web/src/app/shell/AppShell.tsx` (84 lines) — reviewed layout grid, overlay container clipping, and landmark hierarchy.
- `src/Frontend/YO4X.Web/src/app/shell/Sidebar.tsx` (65 lines) — reviewed primary navigation rail, exact route matching via `sidebarViewFor`, badge formatting, and ARIA current page markup.
- `src/Frontend/YO4X.Web/src/app/shell/TopBar.tsx` (102 lines) — reviewed catalog search field, account status pill, user profile button, and assistive technology labels.
- `src/Frontend/YO4X.Web/src/app/shell/TitleBar.tsx` (94 lines) — reviewed desktop window control commands, bridge latency indicator, and connection status readout.

## Verdict
The desktop application shell layout and navigation infrastructure is lean, clean, and structurally sound. Route matching in `Sidebar` strictly relies on exact equality with detail routes collapsed cleanly via `sidebarViewFor`, avoiding prefix mismatching bugs. Navigation components are pure and stateless, preventing stale state across route changes. One P2 robustness issue was found in `TitleBar` where the latency accessibility label conceals disconnected bridge states, and one P3 accessibility issue was found in `TopBar` regarding missing button action labeling.

## Findings

### [P2] TitleBar latency status label announces active round trip when bridge is disconnected
- **Where:** `src/Frontend/YO4X.Web/src/app/shell/TitleBar.tsx:44-50`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const measured = latencyMs !== null;
  const latencyLabel = measured ? `${latencyMs} ms` : '—';
  const dotClass = measured && connected ? 'dot dot--live' : 'dot dot--idle';
  const statusLabel = measured
    ? `Bridge round trip ${latencyMs} milliseconds`
    : 'Bridge round trip not measured';
  ```
- **Failure:** When the bridge disconnects (`connected: false`) but retains a previously measured latency value (e.g. `latencyMs: 45`), `dotClass` switches to `'dot dot--idle'`, but `statusLabel` is computed purely from `measured`. Both the visual tooltip (`title={statusLabel}`) and the screen reader text (`<span className="sr-only">{statusLabel}</span>`) output `"Bridge round trip 45 milliseconds"`. Screen reader users and operators inspecting tooltip status are falsely informed that the bridge is actively communicating with a 45 ms round trip when it is offline.
- **Fix:** Update `statusLabel` computation to check `connected` first, emitting `'Bridge disconnected'` (or `'Bridge disconnected (last round trip ${latencyMs} ms)'`) when `connected` is `false`.

### [P3] TopBar user settings button lacks descriptive accessible name
- **Where:** `src/Frontend/YO4X.Web/src/app/shell/TopBar.tsx:94-97`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  <button type="button" className="topbar__user" onClick={onOpenSettings}>
    <span className="topbar__avatar" aria-hidden="true">{user.initials}</span>
    <span className="topbar__user-name">{user.displayName}</span>
  </button>
  ```
- **Failure:** The avatar container is marked `aria-hidden="true"`, leaving the computed accessible name of the button as only `user.displayName` (e.g. `"operator@example.com"`). Screen reader users navigating interactive landmarks hear only the user's email address with no indication that activating this button opens the application Settings view.
- **Fix:** Add `aria-label={`Open settings (${user.displayName})`}` or `title="Settings"` to the `<button>` element.

## Referrals
- `src/Frontend/YO4X.Web/src/app/App.tsx:293-310` — `searchTerm` is captured and updated in `WorkspaceShell` state, but omitted from `renderPage` arguments and never passed to `CatalogPage` for strategy search filtering.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/app/shell/TitleBar.tsx:44-50` — Status label generation and dot class styling when `latencyMs !== null` and `connected === false` lacks unit test coverage verifying disconnected state messaging.
- `src/Frontend/YO4X.Web/src/app/shell/Sidebar.tsx:27-48` — Navigation rail rendering lacks unit test coverage verifying badge suppression on `undefined` count vs rendering numeric badge on `0` count, as well as `aria-current="page"` presence across all `AppView` variants.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 86.7s | 188540 tok | id=6e30815b-8f7d-4873-ac9d-339fc9dd0ce1
