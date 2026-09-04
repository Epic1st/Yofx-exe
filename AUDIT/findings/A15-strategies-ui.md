---
agent_id: A15
lane: strategies-ui
scope:
  - src/Frontend/YO4X.Web/src/features/strategies/CatalogPage.tsx
  - src/Frontend/YO4X.Web/src/features/strategies/DetailPage.tsx
  - src/Frontend/YO4X.Web/src/features/strategies/StrategyCard.tsx
status: COMPLETE
generated: 2026-08-29T08:50:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 0 }
---

# A15 — strategies-ui

## Scope audited
- `src/Frontend/YO4X.Web/src/features/strategies/CatalogPage.tsx` (264 lines)
- `src/Frontend/YO4X.Web/src/features/strategies/DetailPage.tsx` (604 lines)
- `src/Frontend/YO4X.Web/src/features/strategies/StrategyCard.tsx` (105 lines)

## Verdict
The strategies UI is generally sound in its core rendering, navigation, and security architecture. Untrusted metadata (strategy names, descriptions, author names, review bodies) is rendered exclusively through standard React JSX text bindings with no `dangerouslySetInnerHTML`, HTML injection vectors, or raw source code exposure. Two robustness issues exist in error and loading state presentation: the strategy detail left rail displays permanent loading skeletons on error, and catalog facet chips unmount during requests and stay unmounted on fetch failure.

## Findings

### [P2] DetailPage left rail renders permanent skeleton loading state on fetch error
- **Where:** `src/Frontend/YO4X.Web/src/features/strategies/DetailPage.tsx:253`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
        {value === null ? (
          <>
            <SkeletonBlock className="detail-skeleton--thumb" />
            <SkeletonBlock className="detail-skeleton--price" />
            <SkeletonBlock className="detail-skeleton--facts" />
          </>
        ) : (
  ```
- **Failure:** When `client.getStrategyDetail` fails (e.g. 404 Not Found, network failure, or 500 error), `detail.state.status` transitions to `'error'` or `'unauthorized'` and `value` is `null`. While the main body (`detail__body`) displays an error state with a retry button or unauthorized notice, the left rail (`detail__rail`) checks only `value === null` and unconditionally renders three animated `SkeletonBlock` placeholders indefinitely, creating a broken and confusing loading/error split UI.
- **Fix:** Check `detail.state.status === 'loading'` when rendering rail skeletons, and render an empty or muted state in the rail when `detail.state.status === 'error'` or `'unauthorized'`.

### [P2] CatalogPage facet filter chips unmount during in-flight queries and disappear on error
- **Where:** `src/Frontend/YO4X.Web/src/features/strategies/CatalogPage.tsx:139`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
        {(value?.categories ?? []).map((name) => (
          <button
            key={name}
            type="button"
            className={category === name ? 'chip chip--active' : 'chip'}
            aria-pressed={category === name}
            onClick={() => selectCategory(category === name ? null : name)}
          >
            {name}
          </button>
        ))}
  ```
- **Failure:** When a user selects a category or symbol filter, `useResource` immediately resets `state` to `{ status: 'loading' }`, making `value` `null`. Because category and symbol filter chips are mapped directly from `value?.categories` and `value?.symbols`, all chips except the fallback "All" and "ALL" buttons unmount from the DOM while the request is in flight. If the network request fails (`state.status === 'error'`), `value` remains `null`, leaving the filter chips permanently unmounted and preventing the user from seeing their selected filter or selecting other facets to recover.
- **Fix:** Retain the last non-null `categories` and `symbols` arrays (e.g. via local state or a ref) so chips remain mounted and clickable during loading and error states.

## Referrals
None.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/strategies/DetailPage.tsx:253` — Error and unauthorized states for the detail page sidebar rail are untested; tests only mock successful detail views.
- `src/Frontend/YO4X.Web/src/features/strategies/CatalogPage.tsx:139,160` — Behavior of category and symbol chips during transient network failure or error status is untested.
- `src/Frontend/YO4X.Web/src/features/strategies/DetailPage.tsx:103` — `buildCurve` edge case where `points.length < 2` returning `null` to render the empty curve placeholder is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 108.2s | 224601 tok | id=fe4468be-6361-4fa8-a01d-1f0ce0ebece4
