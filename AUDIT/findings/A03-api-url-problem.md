---
agent_id: A03
lane: api-url-problem
scope:
  - src/Frontend/YO4X.Web/src/api/safeUrl.ts
  - src/Frontend/YO4X.Web/src/api/problemDetails.ts
status: COMPLETE
generated: 2026-08-29T08:42:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# A03 — api-url-problem

## Scope audited
- `src/Frontend/YO4X.Web/src/api/safeUrl.ts` (99 lines) — primary audit scope (URL parsing, origin canonicalization, path traversal defense, transport safety).
- `src/Frontend/YO4X.Web/src/api/problemDetails.ts` (140 lines) — primary audit scope (RFC 7807 problem details parsing, error boundary mapping, sanitization).
- `src/Frontend/YO4X.Web/src/api/safeUrl.test.ts` (113 lines) — unit test coverage and boundary validation for URL helpers.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts` (770 lines) — integration context review for URL resolution and error propagation.
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines) — context review for contract validation of URL references and problem models.
- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.ts` (115 lines) — context review for runtime environment URL resolution.

## Verdict
The audited URL canonicalization (`safeUrl.ts`) and problem details parsing (`problemDetails.ts`) modules are exceptionally robust and secure. URL origin verification enforces strict parsed origin equality with mandatory HTTPS (loopback HTTP permitted only under development mode), while path resolution guarantees strict verbatim equality against resolved WHATWG `URL` instances to prevent directory traversal (`../`), protocol-relative escapes (`//`), absolute overrides, userinfo injection (`@`), or unencoded character manipulation. Problem details parsing defends against malformed payloads, non-JSON error responses, and resource exhaustion via defensive bounds, while deliberately masking raw server `detail` strings to prevent internal data leakage or markup injection.

## Findings

None. The audited modules are clean and hold up to rigorous security standards. Origin checks rely on parsed WHATWG `origin` equality (`base.origin !== origin` and `resolved.origin !== base.origin`) rather than prefix matching. Path joining verifies `${resolved.pathname}${resolved.search} === path`, neutralizing directory traversal and protocol escapes. Problem details parsing handles missing and malformed fields gracefully, limits array and string lengths to prevent payload-based denial of service, and prevents internal diagnostics leakage by surfacing `title` and `correlationId` rather than raw server `detail` in `userFacingProblem`.

## Referrals

None.

## Coverage gaps

None. Boundary tests in `src/Frontend/YO4X.Web/src/api/safeUrl.test.ts` exercise character length bounds, percent-encoding preservation, interior space rejection, and same-origin reference screening. Downstream consumer test suites in `controlPlaneClient.test.ts` and `BotSettingsModal.test.tsx` thoroughly validate error parsing and field-level validation mapping.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 83.8s | 171731 tok | id=f38ab7c4-5c6a-4102-ab63-b52d53d39565
