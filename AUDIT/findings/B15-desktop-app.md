---
agent_id: B15
lane: desktop-app
scope:
  - src/Apps/YO4X.Desktop/App.xaml
  - src/Apps/YO4X.Desktop/App.xaml.cs
  - src/Apps/YO4X.Desktop/DesktopLaunchOptions.cs
  - src/Apps/YO4X.Desktop/DesktopNavigationPolicy.cs
  - src/Apps/YO4X.Desktop/MainWindow.xaml
  - src/Apps/YO4X.Desktop/MainWindow.xaml.cs
  - src/Apps/YO4X.Desktop/Properties/AssemblyInfo.cs
  - src/Apps/YO4X.Desktop/Properties/PublishProfiles/win-x64.pubxml
  - src/Apps/YO4X.Desktop/README.md
  - src/Apps/YO4X.Desktop/YO4X.Desktop.csproj
  - src/Apps/YO4X.Desktop/app.manifest
status: COMPLETE
generated: 2026-08-29T11:26:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# B15 — desktop-app

## Scope audited
All 11 files in the assigned scope were fully read and analyzed:
- `src/Apps/YO4X.Desktop/App.xaml` (21 lines)
- `src/Apps/YO4X.Desktop/App.xaml.cs` (30 lines)
- `src/Apps/YO4X.Desktop/DesktopLaunchOptions.cs` (182 lines)
- `src/Apps/YO4X.Desktop/DesktopNavigationPolicy.cs` (31 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml` (48 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml.cs` (279 lines)
- `src/Apps/YO4X.Desktop/Properties/AssemblyInfo.cs` (4 lines)
- `src/Apps/YO4X.Desktop/Properties/PublishProfiles/win-x64.pubxml` (16 lines)
- `src/Apps/YO4X.Desktop/README.md` (39 lines)
- `src/Apps/YO4X.Desktop/YO4X.Desktop.csproj` (27 lines)
- `src/Apps/YO4X.Desktop/app.manifest` (17 lines)

## Verdict
The desktop host boundary is exceptionally sound and implements comprehensive defense-in-depth for embedded browser security. The WebView2 host strictly restricts in-shell navigation to the explicitly configured application and identity origins, disables all host object injection and script IPC bridges, rejects arbitrary URI schemes and script dialogs, denies all browser permissions and basic auth requests, and blocks file downloads. Argument parsing fails closed on unknown or duplicate parameters, non-loopback HTTP is rejected, and custom development certificate pinning is cryptographically verified and constrained strictly to loopback origins.

## Findings
None.

The implementation holds up against all audit criteria:
1. **Allowed Navigation Set & Origin Isolation:**
   - In `DesktopNavigationPolicy.cs:15-21`, `IsAllowedInShell` strictly allows only `about:blank`, the canonical `applicationOrigin`, and the optional `identityProviderOrigin`. Any navigation, subframe (`Core_FrameNavigationStarting`), or popup (`Core_NewWindowRequested`) attempting to load outside these origins is intercepted and canceled (`MainWindow.xaml.cs:96, 115, 122`).
   - Non-matching HTTPS navigation requests trigger `Process.Start(new ProcessStartInfo(requested.AbsoluteUri) { UseShellExecute = true })` only when `DesktopNavigationPolicy.CanOpenExternally` validates that the scheme is strictly `https:` and contains no embedded credentials (`string.IsNullOrEmpty(uri.UserInfo)`), safely delegating external link navigation to the OS default browser.
2. **Host Object & IPC Bridge Surface:**
   - In `MainWindow.xaml.cs:63-74`, browser settings enforce a strict zero-trust boundary:
     - `core.Settings.AreHostObjectsAllowed = false;` (no C#/COM objects exposed to page JavaScript via `AddHostObjectToScript`).
     - `core.Settings.IsWebMessageEnabled = false;` (no postMessage / IPC bridge between page script and the native WPF host).
     - `core.Settings.AreDefaultScriptDialogsEnabled = false;` (suppresses `alert`, `confirm`, `prompt`).
     - In non-debug builds, accelerator keys, default context menus, and DevTools are disabled.
   - Page scripts have zero access to the host file system, native process execution, or trading authority.
3. **Local Content vs Remote Loading & Resource Isolation:**
   - Loaded content is strictly remote HTTP/HTTPS (`http://127.0.0.1:4173/` default development origin or HTTPS production origin). Custom local schemes or `file://` URIs are rejected during startup parsing (`DesktopLaunchOptions.cs:119-124`) and blocked by navigation policies (`DesktopNavigationPolicy.cs:18-20`).
   - Browser profile data is scoped to user local app data at `%LOCALAPPDATA%\YO4X\Desktop\WebView2` (`MainWindow.xaml.cs:39-45`).
   - Downloads are unconditionally canceled (`MainWindow.xaml.cs:147`), browser permissions (camera, microphone, geolocation, notifications) are unconditionally denied (`MainWindow.xaml.cs:154`), and HTTP basic authentication challenges are blocked (`MainWindow.xaml.cs:163`).
4. **TLS Certificate Pinning:**
   - In `MainWindow.xaml.cs:167-212`, TLS certificate errors trigger `CoreWebView2ServerCertificateErrorAction.Cancel` by default. Overriding is permitted exclusively for an untrusted local development identity provider on loopback (`IsPinnedDevelopmentIdentityCertificate`), requiring an exact SHA-256 fingerprint compared using constant-time equality (`CryptographicOperations.FixedTimeEquals`).
5. **Command-Line Argument Parsing & Execution Context:**
   - In `DesktopLaunchOptions.cs:21-81`, arguments are strictly tokenized and validated. Duplicate options and unrecognized parameters throw `ArgumentException`, terminating startup with exit code 2 (`App.xaml.cs:26`).
   - `ParseApplicationUri` verifies that URLs are absolute, query-free, fragment-free, path-normalized to `/`, contain no userinfo, and enforce HTTPS unless pointing to loopback.
   - `app.manifest:7` specifies `asInvoker`, executing without elevated Windows privileges or UAC elevation. No custom protocol handlers or single-instance named pipe IPC channels are registered.

## Referrals
None.

## Coverage gaps
None. (Existing tests in `tests/YO4X.Desktop.Tests/DesktopLaunchOptionsTests.cs` cover default loopback options, CLI override precedence, invalid URI rejection, query/fragment rejection, remote HTTP rejection, development fixture constraints, duplicate/unknown option handling, identity origin validation, loopback certificate pin validation, in-shell navigation policy boundaries, and external navigation HTTPS criteria).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 73.6s | 150399 tok | id=55cab265-c7e0-4fd9-b239-524706bdd3d8
