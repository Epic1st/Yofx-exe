# YO4X Control Plane Web

React + Vite frontend for the tenant-scoped YO4X ControlPlane dashboard. This slice is intentionally read-only: it presents account, deployment, compatibility, activity, and runtime evidence without exposing trading or mutation controls.

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

Never put access tokens, broker credentials, login IDs, passwords, or server secrets in Vite environment variables. Values prefixed with `VITE_` are compiled for the browser.

Compatibility data is a source-free static-inventory projection. It contains file identifiers, display names, source type, static disposition, and feature counts; it does not publish MQL source bodies, findings, verification documents, conversion evidence documents, or report artifacts. A compatibility result is not compile, semantic-conversion, parity, runtime, or trading permission evidence.

## Authentication boundary

Every API request uses `credentials: "include"`. When the hosting identity shell uses bearer authentication, it provides an ephemeral token through an in-memory bridge:

```ts
window.__YO4X_AUTH__ = {
  getAccessToken: async () => identitySession.getAccessToken(),
  beginLogin: () => identitySession.beginLogin(),
};
```

The frontend does not persist tokens in local storage, session storage, source, URLs, or logs. Production should serve the frontend and API from one trusted origin when practical.

## Visual QA fixture

Representative design data is isolated in `src/test-fixtures`. It is reachable only in Vite development/test mode and only with the explicit query `?fixture=dashboard`. A production build tree-shakes this branch and never falls back to the fixture after an API error.

With the dev server running, `npm run qa:browser` uses an installed Edge/Chromium executable to capture desktop and mobile screenshots, exercise search, evidence/report dialogs and responsive navigation, reject browser console errors, and reject page-level horizontal overflow. Override its inputs with `YO4X_QA_URL`, `YO4X_QA_OUTPUT`, and `YO4X_BROWSER_EXECUTABLE`.

Set `YO4X_QA_EXPECTATION=fail-closed` against a production preview with no API to prove that fixture data is rejected and the explicit unavailable/authentication/configuration state remains closed after retry. The default expectation is `fixture` and is valid only against the explicit development fixture URL.
