# YO4X Desktop

Windows WPF/WebView2 shell for the YO4X operational frontend. The shell owns no
broker credential, trading decision, licence decision, or raw gateway capability.

The default application origin is `http://127.0.0.1:4173/` for local development.
Set `YO4X_DESKTOP_APP_URL` or pass `--app-url https://control.example` for a deployed
HTTPS frontend. Plain HTTP is accepted only for loopback.

The local development identity origin defaults to `https://127.0.0.1:7210/`.
Deployments can set an exact HTTPS origin with `YO4X_DESKTOP_IDENTITY_URL` or
`--identity-url`. It is the only additional origin allowed inside the shell.
For an untrusted loopback-only development certificate, configure its exact
SHA-256 fingerprint with `YO4X_DESKTOP_IDENTITY_CERTIFICATE_SHA256` or
`--development-identity-certificate-sha256`; certificate errors remain blocked
for every other origin and fingerprint.

For local visual testing only, pass `--development-fixture`. The fixture option is
rejected for non-loopback origins and remains clearly labelled inside the UI.
Normal launches hide all development browser chrome and present only the
customer-facing application surface.

## Build and publish

Use the checked-in `win-x64` publish profile so local and CI packaging use the
same self-contained, single-file settings:

```powershell
dotnet publish .\src\Apps\YO4X.Desktop\YO4X.Desktop.csproj `
  -p:PublishProfile=win-x64 `
  --nologo
```

The executable is emitted to
`artifacts/desktop/YO4X.Desktop/win-x64/YO4X.exe`. Microsoft Edge WebView2
Evergreen Runtime is an external browser prerequisite. Production distribution
must Authenticode-sign and timestamp the executable with the organization's
code-signing certificate; local development builds are intentionally unsigned.
