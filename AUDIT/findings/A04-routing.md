---
agent_id: A04
lane: Route table and navigation model
scope:
  - src/Frontend/YO4X.Web/src/app/App.tsx
  - src/Frontend/YO4X.Web/src/app/navigation.ts
status: COMPLETE
generated: 2026-08-29T08:48:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 0 }
---

# A04 — Route table and navigation model

## Scope audited
- `src/Frontend/YO4X.Web/src/app/App.tsx` (445 lines) — reviewed all 445 lines covering authentication gating, workspace routing, overlay orchestration, and browser history synchronization.
- `src/Frontend/YO4X.Web/src/app/navigation.ts` (112 lines) — reviewed all 112 lines covering route definitions, UUID parameter validation, fallback parsing, and canonical hash formatting.

## Verdict
The route table and navigation model is well-structured, secure, and resilient. Authentication resolution strictly precedes workspace mounting (preventing protected content flashing), unknown/malformed routes cleanly fall back to the dashboard rather than rendering blank screens, and deep-link hashes are preserved across initial auth. No destructive actions are reachable via route transitions without modal confirmation. One P2 robustness issue was identified in async launch overlay initialization.

## Findings

### [P2] Unhandled promise rejection in launch overlay trigger
- **Where:** `src/Frontend/YO4X.Web/src/app/App.tsx:281-291`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const startLaunch = useCallback(
    (host: 'LOCAL' | 'CLOUD') => async (strategyId: string) => {
      const detail = await client.getStrategyDetail(strategyId);
      setOverlay({
        kind: 'launch',
        host,
        strategy: { id: detail.item.id, name: detail.item.name, symbol: detail.item.symbol },
      });
    },
    [client, setOverlay],
  );
  ```
- **Failure:** When a user triggers "Run locally now" or "Start a cloud runner" from `DetailPage`, `startLaunch` executes asynchronously via `void startLaunch(...)(strategyId)`. If `client.getStrategyDetail` fails (e.g., transient network disconnection, request timeout, or HTTP 500 from the control plane), the rejected promise is unhandled. The launch wizard fails to open and no error notification or recovery prompt is surfaced to the operator.
- **Fix:** Wrap the `client.getStrategyDetail` call in a `try/catch` block within `startLaunch` to catch errors and surface an error message or toast rather than letting the promise reject unhandled.

## Referrals
- `src/Frontend/YO4X.Web/src/app/shell/TopBar.tsx:59` — TopBar captures `searchTerm` in local workspace state, but `searchTerm` is never passed to `renderPage` or `CatalogPage` for search filtering.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/app/navigation.ts:75-100` — `locationFromHash` lacks dedicated unit tests covering edge cases: trailing slashes (`#strategies/`), malformed UUID versions/variants, uppercase UUIDs, extra path segments (`#strategies/id/extra`), and query string fragments.
- `src/Frontend/YO4X.Web/src/app/App.tsx:172-183` — `Workspace` hash and history popstate synchronization lacks browser event integration tests validating back/forward state preservation across page switches.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 81.9s | 221717 tok | id=99446601-a226-42c5-9038-3b9c8b66b7fe
