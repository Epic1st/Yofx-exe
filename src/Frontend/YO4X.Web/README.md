# YO4X Control Plane Web

React + Vite frontend for the tenant-scoped YO4X ControlPlane dashboard. Most surfaces are read-only. Broker Accounts exposes one narrow mutation for a pre-provisioned demo account: it can request a backend cloud connection test and poll its durable operation status. It does not expose trading controls.

## Run locally

Use Node 22.22.2 or later.

```powershell
npm ci
npm run typecheck
npm run test:run
npm run dev
```

The production build is created with `npm run build`.

## Runtime configuration

Copy `.env.example` to an untracked `.env.local` and configure only the IDs and projections that exist. Missing optional projections render explicit unavailable or empty states.

- `VITE_YO4X_CONTROL_API_ORIGIN`: canonical HTTPS ControlPlane origin with no path, query, fragment, or user information; empty means same-origin.
- `VITE_YO4X_BROKER_ACCOUNT_ID`: optional selected demo account UUID.
- `VITE_YO4X_DEPLOYMENT_ID`: optional selected deployment UUID.
- `VITE_YO4X_STRATEGY_CORPUS_ID`: optional tenant-owned source corpus UUID. When set, the frontend reads only `/v1/strategy-source-corpora/{corpusId}/compatibility`; the API binds the corpus to the authenticated tenant and user.
- `VITE_YO4X_RUNTIME_READINESS_PATH`: optional same-origin ControlPlane read projection.
- `VITE_YO4X_SIGN_IN_URL`: same-origin identity-provider entry point.
- `VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED`: exactly `true` enables the fixed local OIDC client, and only in Vite development at `http://127.0.0.1:4173`. It cannot override the registered authority, client, or callback.

Never put access tokens, broker credentials, login IDs, passwords, or server secrets in Vite environment variables. Values prefixed with `VITE_` are compiled for the browser.

## Broker Accounts safety boundary

Broker Accounts is limited to the account selected by `VITE_YO4X_BROKER_ACCOUNT_ID`. The browser sends a fixed `user_connection_test` audit reason with a high-entropy idempotency key and the current account version. A `202 Accepted` response is displayed as pending; only a bound terminal `succeeded` operation is displayed as successful.

The frontend has no broker-password field and no credential upload path. Credential enrollment and rotation remain blocked until a shared, versioned MT5 credential envelope is defined across the browser, secret-ingestion service, vault, and GatewayHost. Existing server-side credentials are represented only by masked state returned from the ControlPlane.

Compatibility data is a source-free static-inventory projection. It contains file identifiers, display names, source type, static disposition, and feature counts; it does not publish MQL source bodies, findings, verification documents, conversion evidence documents, or report artifacts. A compatibility result is not compile, semantic-conversion, parity, runtime, or trading permission evidence.

## Authentication boundary

Every API request uses `credentials: "include"`. When the hosting identity shell uses bearer authentication, it provides an ephemeral token through an in-memory bridge:

```ts
window.__YO4X_AUTH__ = {
  getAccessToken: async () => identitySession.getAccessToken(),
  beginLogin: () => identitySession.beginLogin(),
};
```

For local account testing, explicitly set `VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED=true`. The fixed public client uses authorization code with PKCE against `https://127.0.0.1:7210`, client `yo4x-web-development`, and callback `http://127.0.0.1:4173/auth/callback`. Create account opens the identity provider's real `/account/register` form and then resumes the stored authorization request. The React application never renders a password field.

Access and ID tokens live only in the OIDC client's in-memory user store. `sessionStorage` holds only short-lived authorization transaction state (PKCE verifier, state, and nonce) needed to survive the redirect; no token is written to local storage, session storage, source, URLs, or logs. A production build, a different frontend origin, or an authentication callback without explicit development enablement fails closed. Production should serve the frontend and API from one trusted origin when practical.

## Visual QA fixture

Representative design data is isolated in `src/test-fixtures`. It is reachable only in Vite development/test mode and only with the explicit query `?fixture=dashboard`. Outside those modes the query flag is ignored, a production build tree-shakes the fixture branch, and the application never falls back to fixture data after an API error. With a valid HTTPS origin, production follows the normal typed API path and issues ordinary API requests.

With the dev server running, `npm run qa:browser` uses an installed Edge/Chromium executable to capture desktop and mobile screenshots, exercise search, evidence/report dialogs and responsive navigation, reject browser console errors, and reject page-level horizontal overflow. Override its inputs with `YO4X_QA_URL`, `YO4X_QA_OUTPUT`, and `YO4X_BROWSER_EXECUTABLE`.

Set `YO4X_QA_EXPECTATION=fail-closed` against a production preview with no API to prove that the fixture flag is ignored and the explicit unavailable/authentication/configuration state remains closed after normal API attempts and retry. The default expectation is `fixture` and is valid only against the explicit development fixture URL.
