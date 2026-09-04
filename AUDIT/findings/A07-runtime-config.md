---
agent_id: A07
lane: runtime-config
scope:
  - src/Frontend/YO4X.Web/src/app/config/runtimeConfig.ts
status: COMPLETE
generated: 2026-08-29T08:50:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# A07 — runtime-config

## Scope audited
- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.ts` (115 lines) — primary audit scope (runtime configuration parsing, environment variable ingestion, UUID and URL path validation, development identity guards).
- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.test.ts` (135 lines) — unit test suite for runtime configuration parsing and rejection boundaries.
- `src/Frontend/YO4X.Web/src/api/safeUrl.ts` (99 lines) — context review for origin parsing, loopback transport safety checks, and canonical path resolution.
- `src/Frontend/YO4X.Web/src/auth/developmentOidc.ts` (184 lines) — context review for development OIDC configuration consumption.
- `src/Frontend/YO4X.Web/src/app/App.tsx` (445 lines) — context review for runtime configuration loading and error boundary presentation.
- `src/Frontend/YO4X.Web/src/main.tsx` (42 lines) — context review for bootstrap entry point configuration ingestion.

## Verdict
The runtime configuration parsing implementation in `runtimeConfig.ts` is robust, secure, and fails closed under all invalid or hostile environment configurations. No sensitive credentials or secrets are baked into the frontend bundle. Optional environment variables safely resolve to `null` or explicit defaults (`/auth/sign-in`) without falling back to insecure development origins. Dev-only features (such as local development OIDC) enforce multi-layer guards requiring both `import.meta.env.DEV === true` and an exact loopback origin match (`http://127.0.0.1:4173`), throwing synchronously if configured in a production build. Boolean environment variable parsing avoids `Boolean('false')` coercion bugs by enforcing strict exact-string equivalence (`=== 'true'`).

## Findings

None. The audited module exhibits no security vulnerabilities or behavioral defects:
- API origins are strictly parsed and validated using WHATWG URL parsing; HTTP loopback transport is strictly forbidden in production builds (`!import.meta.env.DEV`), preventing production traffic from being pointed at unencrypted or spoofed endpoints.
- Path configurations (`VITE_YO4X_SIGN_IN_URL`, `VITE_YO4X_RUNTIME_READINESS_PATH`) enforce canonical same-origin absolute paths via `resolveSameOriginApiPath`, preventing open redirects or scheme manipulation.
- Identifier variables (`brokerAccountId`, `deploymentId`, `strategyCorpusId`) enforce RFC 4122/RFC 9562 UUID syntax (v1–v8, including backend v7 UUIDs) and lowercase normalization.
- Development OIDC parameters are hardcoded contract constants; no user-controllable authority or redirect URLs can be injected via environment variables.

## Referrals

None.

## Coverage gaps

- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.ts:39` — `developmentOidc` branch `window.location.origin !== 'http://127.0.0.1:4173'` under `DEV === true` lacks an explicit unit test verifying that non-standard dev origins (such as `http://localhost:5173`) are rejected when `VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED='true'`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 60.6s | 162604 tok | id=ff92c8f6-39c9-4c0b-9015-ecb97cd817e1
