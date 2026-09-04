# YO4X Standalone Desktop OTA (Over-The-Air) Update Guide

## 1. Architecture Confirmation: Frontend + Backend Ek Sath Hai

**Haan, bilkul!** YO4X Desktop ek **100% Standalone Single-Package Application** hai:

```text
YO4X Desktop Installation Folder / Package
│
├── YO4X.exe                  <-- Native Windows Host + Embedded Backend (ASP.NET Core / Kestrel)
├── mt5api.dll                <-- MetaTrader 5 Bridge DLL
├── wwwroot/                  <-- Pura React 18 Production Frontend (HTML/JS/CSS/Assets)
│   ├── index.html
│   └── assets/
└── *.dll                     <-- .NET 10 Runtimes, Npgsql, WebView2 Libraries
```

* **Frontend:** React 18 SPA code (`src/Frontend/YO4X.Web`) build hokar `wwwroot/` folder me chala jata hai.
* **Backend:** ASP.NET Core (`LocalServerHost.cs`) aur `LocalTradingEngine.cs` directly `YO4X.exe` ke andar in-process chalte hain loopback port par.
* **Jab aap build banate ho**, to **Frontend + Backend dono ek sath ek hi folder / zip package me bundle hote hain.**

Isliye jab client ko update bhejte hain, to **pura software package ek sath update hota hai**—alag se kuch nahi bhejna padta!

---

## 2. Pura Package OTA Kaise Kaam Karta Hai? (Workflow)

```
[1. Developer / Admin]
       │
       │ 1. Naya code likha (Frontend ya Backend me)
       │ 2. Run: .\scripts\Publish-OtaRelease.ps1 -Version "1.1.0"
       │ 3. Script banayega:
       │    - YO4X-v1.1.0-Windows-x64.zip  (Pura standalone package)
       │    - version.json                 (Version & Download URL info)
       │ 4. Upload to Server / GitHub Releases / AWS S3 / VPS
       v
[2. Update Server / Cloud]
       │ Hosting: https://your-server.com/updates/
       │   ├── version.json
       │   └── YO4X-v1.1.0-Windows-x64.zip
       v
[3. Client Machine par YO4X.exe]
       │
       │ 1. App start hote hi ya har 2-4 ghante me version.json check karta hai.
       │ 2. Agar Server Version (1.1.0) > Client Version (1.0.0):
       │ 3. Background me YO4X-v1.1.0-Windows-x64.zip download karta hai.
       │ 4. User ko UI me popup / banner dikhta hai: "Update Ready! [Restart Now]"
       │ 5. User jab click karta hai:
       │    - YO4X.exe background me updater script chalata hai
       │    - YO4X.exe band (exit) ho jata hai
       │    - Updater script naye files purane folder me overwrite/replace kar deta hai
       │    - Naya YO4X.exe auto restart ho jata hai!
       v
[Client Updated to v1.1.0 with New Frontend & Backend!]
```

---

## 3. Server Setup: Files Kaha Host Karni Hai?

Client machines tak update pahunchane ke liye aapko sirf **2 files** kisi bhi static web server, GitHub Releases, AWS S3, ya VPS par rakhni hoti hain:

### URL Structure:
```text
https://your-domain.com/updates/
├── version.json
└── YO4X-v1.1.0-Windows-x64.zip
```

### `version.json` Format:
```json
{
  "version": "1.1.0",
  "minRequiredVersion": "1.0.0",
  "releaseDate": "2026-09-03",
  "title": "YO4X Update v1.1.0",
  "changelog": "Performance improvements, new UI fixes, and MT5 connection stability.",
  "downloadUrl": "https://your-domain.com/updates/YO4X-v1.1.0-Windows-x64.zip",
  "sha256": "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92",
  "forceUpdate": false
}
```

> **Option A (GitHub Releases - 100% Free & Fast):**
> Aap GitHub Repository ke Releases tab me `YO4X-v1.1.0-Windows-x64.zip` aur `version.json` upload kar sakte hain. Direct download link mil jata hai.
>
> **Option B (Apna Server / VPS / Cloudflare R2 / S3):**
> Kisi bhi Nginx/Apache ya S3 bucket me daal do aur URL `version.json` me set kar do.

---

## 4. Release Banane Ka Script (1-Click Command)

Jab bhi naya update nikalna ho, sirf yeh command run karni hai:

```powershell
.\scripts\Publish-OtaRelease.ps1 -Version "1.1.0"
```

Yeh script automatically:
1. React frontend (`npm run build`) banayega.
2. Assets ko desktop ke `wwwroot/` me copy karega.
3. C# Backend + WPF host (`dotnet publish -r win-x64 --self-contained`) compile karega.
4. `mt5api.dll` bundle karega.
5. Pura package zip banayega: `artifacts/ota/release-stable/YO4X-v1.1.0-Windows-x64.zip`.
6. `version.json` generate karega jisme SHA-256 hash aur download URL hoga.

---

## 5. Client-Side Code: `YO4X.exe` Me OTA Setup Kaise Hoga?

Windows par jab koi `.exe` chal raha hota hai, to OS us file ko lock kar deta hai (`ERROR_SHARING_VIOLATION`). Isliye update apply karne ke liye ek simple 3-step atomic process use hota hai:

### Step 1: C# Updater Service (`OtaService.cs`)
Is file ko `src/Apps/YO4X.Desktop/OtaService.cs` me add karein:

```csharp
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace YO4X.Desktop;

public sealed class OtaService
{
    private static readonly HttpClient Http = new();
    
    // Aapka update server URL (Change this to your actual server or GitHub link)
    public const string UpdateManifestUrl = "https://your-domain.com/updates/version.json";

    public sealed record VersionInfo(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("changelog")] string Changelog,
        [property: JsonPropertyName("forceUpdate")] bool ForceUpdate
    );

    public string CurrentVersion => 
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public VersionInfo? AvailableUpdate { get; private set; }
    public string? DownloadedZipPath { get; private set; }

    /// <summary>
    /// Check if a newer version exists on the server
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            var info = await Http.GetFromJsonAsync<VersionInfo>(UpdateManifestUrl);
            if (info == null || string.IsNullOrWhiteSpace(info.Version)) return false;

            var serverVer = new Version(info.Version);
            var localVer = new Version(CurrentVersion);

            if (serverVer > localVer)
            {
                AvailableUpdate = info;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OTA] Check failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Download the full update package zip in the background
    /// </summary>
    public async Task<bool> DownloadUpdateAsync(Action<int>? onProgress = null)
    {
        if (AvailableUpdate == null) return false;

        string updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YO4X", "Updates");
        Directory.CreateDirectory(updatesDir);

        string zipPath = Path.Combine(updatesDir, $"YO4X-v{AvailableUpdate.Version}.zip");

        using (var response = await Http.GetAsync(AvailableUpdate.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var sourceStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                if (totalBytes > 0 && onProgress != null)
                {
                    int percent = (int)((totalRead * 100) / totalBytes);
                    onProgress(percent);
                }
            }
        }

        // Verify SHA256 Checksum
        if (!string.IsNullOrWhiteSpace(AvailableUpdate.Sha256))
        {
            using var sha256 = SHA256.Create();
            await using var fs = File.OpenRead(zipPath);
            byte[] hash = await sha256.ComputeHashAsync(fs);
            string calculated = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(calculated, AvailableUpdate.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                throw new Exception("Downloaded update file hash verification failed!");
            }
        }

        DownloadedZipPath = zipPath;
        return true;
    }

    /// <summary>
    /// Closes YO4X.exe, replaces all files from the zip, and restarts new version
    /// </summary>
    public void ApplyUpdateAndRestart()
    {
        if (string.IsNullOrEmpty(DownloadedZipPath) || !File.Exists(DownloadedZipPath))
            throw new FileNotFoundException("Update package not found.");

        string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        string tempExtractDir = Path.Combine(Path.GetDirectoryName(DownloadedZipPath)!, "extracted");

        if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
        ZipFile.ExtractToDirectory(DownloadedZipPath, tempExtractDir);

        int currentPid = Environment.ProcessId;
        string updaterBat = Path.Combine(Path.GetDirectoryName(DownloadedZipPath)!, "apply_update.bat");

        // Bat script waits for YO4X.exe to exit, copies all files, and restarts it
        string batScript = $@"@echo off
timeout /t 2 /nobreak > nul
:WAIT_LOOP
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if %ERRORLEVEL%==0 (
    timeout /t 1 /nobreak > nul
    goto WAIT_LOOP
)

xcopy /s /e /y ""{tempExtractDir}\*"" ""{appDir}\"" > nul

start """" ""{Path.Combine(appDir, "YO4X.exe")}""
exit
";
        File.WriteAllText(updaterBat, batScript);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{updaterBat}\"",
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);
        System.Windows.Application.Current.Shutdown(0);
    }
}
```

---

### Step 2: LocalServerHost Me API Expose Karna
`LocalServerHost.cs` me sirf 3 simple routes add karne hain taaki frontend se update manage ho sake:

```csharp
// --- OTA Endpoints ---
var ota = new OtaService();

// 1. Current status & check update
app.MapGet("/v1/ota/status", async () =>
{
    bool hasUpdate = await ota.CheckForUpdateAsync();
    return Results.Ok(new
    {
        currentVersion = ota.CurrentVersion,
        updateAvailable = hasUpdate,
        latestVersion = ota.AvailableUpdate?.Version,
        changelog = ota.AvailableUpdate?.Changelog,
        downloadReady = ota.DownloadedZipPath != null
    });
});

// 2. Download package
app.MapPost("/v1/ota/download", async () =>
{
    bool success = await ota.DownloadUpdateAsync();
    return Results.Ok(new { success });
});

// 3. Apply update and restart
app.MapPost("/v1/ota/apply", () =>
{
    ota.ApplyUpdateAndRestart();
    return Results.Ok(new { status = "restarting" });
});
```

---

### Step 3: Frontend (React) Me Banner / Button
Frontend ke header ya dashboard me chhota sa popup ya banner dikha sakte hain:

```tsx
// Jab user software khole, backend check karega:
const checkUpdate = async () => {
  const res = await fetch('/v1/ota/status');
  const data = await res.json();
  if (data.updateAvailable) {
    // Show banner: "New update v{data.latestVersion} available!"
  }
};

// "Update Now" click karne par:
const handleUpdate = async () => {
  setLoading("Downloading update...");
  await fetch('/v1/ota/download', { method: 'POST' });
  
  // Download hone ke baad:
  setLoading("Restarting to apply update...");
  await fetch('/v1/ota/apply', { method: 'POST' });
};
```

---

## 6. Real Client Deployment Runbook (Step-by-Step)

Jab aap software clients ko bhejte ho, to future updates ka pura process yeh hoga:

### First Time (v1.0.0 Release):
1. Package build karein:
   ```powershell
   .\scripts\Package-DistributionZip.ps1
   ```
2. Client ko `YO4X-v1.0.0-Windows-x64.zip` de do (wo unzip karke `YO4X.exe` chalayega).

### Future Update (e.g. v1.0.1 Release):
1. Code me changes karein (frontend UI fix kiya ya backend trading logic modify kiya).
2. Version update karein aur release build run karein:
   ```powershell
   .\scripts\Publish-OtaRelease.ps1 -Version "1.0.1"
   ```
3. Jo output folder me `YO4X-v1.0.1-Windows-x64.zip` aur `releases.json` (rename to `version.json`) bane hain, unko apne server par upload kar do.
4. **Bas!** Client jab bhi apna YO4X software kholega:
   - App automatically server se naya `version.json` read karega.
   - Pura naya zip download karega.
   - User jaise hi "Restart & Update" dabayega, software auto update hokar naye version me khul jayega!
