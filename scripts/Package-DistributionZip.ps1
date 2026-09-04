[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishScript = Join-Path $workspaceRoot "scripts\Publish-YO4XDesktop.ps1"
$publishDir = Join-Path $workspaceRoot "artifacts\desktop\YO4X.Desktop\win-x64"
$distDir = Join-Path $workspaceRoot "artifacts\distribution"
$zipPath = Join-Path $distDir "YO4X-v1.0.0-Windows-x64.zip"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  Creating Distribution ZIP for YO4X Standalone Desktop" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Run Publish if not already built
if (-not (Test-Path (Join-Path $publishDir "YO4X.exe"))) {
    & powershell -ExecutionPolicy Bypass -File $publishScript
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

$zipSize = (Get-Item -LiteralPath $zipPath).Length / (1024 * 1024)
Write-Host "`n=========================================================" -ForegroundColor Green
Write-Host "  DISTRIBUTION PACKAGE CREATED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "  Archive Location: $zipPath" -ForegroundColor White
Write-Host "  Archive Size: $([Math]::Round($zipSize, 2)) MB" -ForegroundColor White
Write-Host "  Ready for user download and offline execution." -ForegroundColor White
Write-Host "=========================================================" -ForegroundColor Green
