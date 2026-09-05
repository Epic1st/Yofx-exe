[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishScript = Join-Path $workspaceRoot "scripts\Publish-YO4XDesktop.ps1"
$publishDir = Join-Path $workspaceRoot "artifacts\desktop\YO4X.Desktop\win-x64"
$distDir = Join-Path $workspaceRoot "artifacts\distribution"
$zipPath = Join-Path $distDir "YO4X-v1.0.0-Windows-x64.zip"
$desktopEnvironmentFile = Join-Path $publishDir "yo4x.desktop.env"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  Creating Distribution ZIP for YO4X Standalone Desktop" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Always publish so a stale framework-dependent development build can never be distributed.
& powershell -ExecutionPolicy Bypass -File $publishScript
if ($LASTEXITCODE -ne 0) { throw "Desktop publication failed." }

if (-not (Test-Path -LiteralPath $desktopEnvironmentFile)) {
    throw "Desktop runtime configuration is missing: $desktopEnvironmentFile"
}
$desktopEnvironmentText = Get-Content -Raw -LiteralPath $desktopEnvironmentFile
foreach ($requiredSetting in @('YO4X_CONTROL_API_ORIGIN', 'YO4X_DESKTOP_IDENTITY_URL')) {
    if ($desktopEnvironmentText -notmatch "(?m)^$requiredSetting=\S+") {
        throw "Desktop runtime configuration is missing $requiredSetting."
    }
}

# 2. Add Launcher batch file in publishDir
$launcherBat = Join-Path $publishDir "Start-YO4X.bat"
$launcherContent = @"
@echo off
title Starting YO4X Desktop...
cd /d "%~dp0"
start "" "%~dp0YO4X.exe"
exit
"@
Set-Content -LiteralPath $launcherBat -Value $launcherContent -Encoding ASCII
Write-Host "  -> Created Start-YO4X.bat launcher" -ForegroundColor Green

# 3. Create Distribution Directory
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Force -LiteralPath $zipPath
}

# 4. Create ZIP Archive
Write-Host "`nCompressing distribution package to ZIP archive..." -ForegroundColor Yellow
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($null -eq ($archive.Entries | Where-Object FullName -eq 'yo4x.desktop.env')) {
        throw "Distribution archive validation failed: yo4x.desktop.env was not included."
    }
}
finally {
    $archive.Dispose()
}

$zipSize = (Get-Item -LiteralPath $zipPath).Length / (1024 * 1024)
Write-Host "`n=========================================================" -ForegroundColor Green
Write-Host "  DISTRIBUTION PACKAGE CREATED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "  Archive Location: $zipPath" -ForegroundColor White
Write-Host "  Archive Size: $([Math]::Round($zipSize, 2)) MB" -ForegroundColor White
Write-Host "  Ready for user download and offline execution." -ForegroundColor White
Write-Host "=========================================================" -ForegroundColor Green
