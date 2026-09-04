---
agent_id: A21
lane: fe-build
scope:
  - src/Frontend/YO4X.Web/vite.config.ts
  - src/Frontend/YO4X.Web/package.json
  - src/Frontend/YO4X.Web/index.html
  - src/Frontend/YO4X.Web/.env.example
status: COMPLETE
generated: 2026-08-29T08:52:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 0 }
---

# A21 — fe-build

## Scope audited
- `src/Frontend/YO4X.Web/vite.config.ts` (47 lines) — primary audit scope (Vite build, dev server proxy, preview, and vitest config).
- `src/Frontend/YO4X.Web/package.json` (36 lines) — primary audit scope (dependency pinning, scripts, engine constraints).
- `src/Frontend/YO4X.Web/index.html` (16 lines) — primary audit scope (HTML entry shell, meta tags, script loading).
- `src/Frontend/YO4X.Web/.env.example` (19 lines) — primary audit scope (client environment variable definitions and exposure).
- `src/Frontend/YO4X.Web/package-lock.json` (2565 lines) — context review for resolved dependency tree.
- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.ts` (115 lines) — context review for runtime environment consumption.
- `src/Frontend/YO4X.Web/src/main.tsx` (42 lines) — context review for application bootstrap.
- `src/Frontend/YO4X.Web/src/app/styles/tokens.css` (111 lines) — context review for local styling tokens.
- `src/Frontend/YO4X.Web/src/app/styles/global.css` (1189 lines) — context review for external font imports.

## Verdict
The build and delivery configuration is clean and strictly controlled in terms of dependency management and environment variable exposure. Dependencies are pinned to exact versions without floating ranges, and environment variables expose only intentional public context IDs and endpoints without backend secret leakage. However, production builds unconditionally emit full source maps exposing internal TypeScript source code and comments, and the application entry point lacks a Content-Security-Policy (CSP) to restrict script execution and network exfiltration in the browser.

## Findings

### [P2] Production build unconditionally emits full TypeScript source maps
- **Where:** `src/Frontend/YO4X.Web/vite.config.ts:28-31`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
    build: {
      sourcemap: true,
      reportCompressedSize: true,
    },
  ```
- **Failure:** When running `npm run build` (`tsc -b && vite build`), Vite generates `.js.map` files (such as `dist/assets/index-DjR2POmv.js.map`) into the production `dist/` directory. When served by a static web server or CDN, external users and adversaries can retrieve these source maps to reconstruct the complete original TypeScript source code, comments, internal file structure, and private algorithmic interfaces.
- **Fix:** Set `sourcemap: false` or conditionally toggle sourcemaps only for non-production modes (e.g., `sourcemap: mode !== 'production'`).

### [P2] Missing Content-Security-Policy (CSP) in HTML entry point
- **Where:** `src/Frontend/YO4X.Web/index.html:3-10`
- **Confidence:** CONFIRMED
- **Code:**
  ```html
    <head>
      <meta charset="UTF-8" />
      <meta name="viewport" content="width=device-width, initial-scale=1.0" />
      <meta name="theme-color" content="#ffffff" />
      <meta name="description" content="YO4X trading strategy control plane" />
      <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
      <title>YO4X Control</title>
    </head>
  ```
- **Failure:** `index.html` defines no `<meta http-equiv="Content-Security-Policy">` tag, and Vite does not inject CSP headers. If an XSS vulnerability occurs elsewhere or malicious script is injected, the browser lacks policy enforcement to prevent inline script execution, unauthorized script evaluation, or unauthorized network exfiltration of session credentials and trade commands.
- **Fix:** Add a strict Content-Security-Policy meta tag or configure deployment server headers enforcing `default-src 'self'`, `connect-src 'self'`, `script-src 'self'`, `style-src 'self' 'unsafe-inline' https://fonts.googleapis.com`, `font-src 'self' https://fonts.gstatic.com`, `img-src 'self' data:`, `frame-ancestors 'none'`, and `object-src 'none'`.

## Referrals
- `src/Frontend/YO4X.Web/src/app/styles/global.css:1 — imports Google Fonts over HTTP/CDN (@import url("https://fonts.googleapis.com/...")) without Subresource Integrity or local font bundling, creating an unpinned external network dependency.`

## Coverage gaps
- `src/Frontend/YO4X.Web/vite.config.ts:28` — No automated build-verification test validates that production distribution artifacts in `dist/` omit `.js.map` files and include appropriate security headers.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 75.8s | 157381 tok | id=a094c15d-11ca-4b10-8974-0c5bd59572d9
