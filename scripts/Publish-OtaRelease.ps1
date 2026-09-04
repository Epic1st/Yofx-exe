<#
.SYNOPSIS
    Builds, signs, packages, hashes, and publishes an OTA release for YO4X Desktop.
.PARAMETER Version
    The semantic version string to release (e.g. "1.1.0").
.PARAMETER Channel
    The update channel ("stable" or "beta").
.PARAMETER CertThumbprint
    The SHA-1 thumbprint of the Authenticode signing certificate.
.PARAMETER S3Bucket
    The target S3 / Cloudflare R2 bucket name (e.g. "s3://yo4x-updates").
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter()]
    [ValidateSet("stable", "beta")]
    [string] $Channel = "stable",

    [Parameter()]
    [string] $CertThumbprint,

    [Parameter()]
    [string] $S3Bucket = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$desktopProject = Join-Path $workspaceRoot "src\Apps\YO4X.Desktop\YO4X.Desktop.csproj"
$frontendRoot = Join-Path $workspaceRoot "src\Frontend\YO4X.Web"
$desktopWwwRoot = Join-Path $workspaceRoot "src\Apps\YO4X.Desktop\wwwroot"
$distDir = Join-Path $frontendRoot "dist"
$artifactsRoot = Join-Path $workspaceRoot "artifacts\ota"
$stageDir = Join-Path $artifactsRoot "stage-$Version"
$releaseDir = Join-Path $artifactsRoot "release-$Channel"
$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$nodeDir = Join-Path $workspaceRoot ".tools\node-v24.19.0-win-x64"
$npm = if (Test-Path (Join-Path $nodeDir "npm.cmd")) { Join-Path $nodeDir "npm.cmd" } else { "npm" }
$mt5Bridge = Join-Path $workspaceRoot "mt5-net-api-full-binaries-main\mt5api.dll"
$mq5Dir = Join-Path $workspaceRoot "Testing\Mq5"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  YO4X Desktop OTA Release Automation: v$Version ($Channel)" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Clean & Prepare Artifact Folders
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir | Out-Null }
New-Item -ItemType Directory -Path $stageDir | Out-Null

# 2. Build Frontend SPA
Write-Host "`n[1/6] Building Production React Frontend..." -ForegroundColor Yellow
Push-Location $frontendRoot
try {
    if (Test-Path $nodeDir) { $env:PATH = "$nodeDir;" + $env:PATH }
    & $npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed!" }
}
finally {
    Pop-Location
}

# 3. Synchronize wwwroot
Write-Host "`n[2/6] Syncing wwwroot to Desktop..." -ForegroundColor Yellow
if (Test-Path $desktopWwwRoot) { Remove-Item -Recurse -Force $desktopWwwRoot }
Copy-Item -Recurse -LiteralPath $distDir -Destination $desktopWwwRoot

# 4. Compile & Publish Self-Contained Desktop
Write-Host "`n[3/6] Publishing Desktop Binary (win-x64, self-contained)..." -ForegroundColor Yellow
& $dotnet publish $desktopProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -o $stageDir `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Dotnet publish failed!" }

# 5. Bundle Vendor DLLs & Encrypted Strategies
Write-Host "`n[4/6] Bundling mt5api.dll and encrypted .yo4x strategies..." -ForegroundColor Yellow
if (Test-Path $mt5Bridge) {
    Copy-Item -Force $mt5Bridge $stageDir
    Write-Host "  -> Bundled mt5api.dll" -ForegroundColor Green
}

$strategiesDir = Join-Path $stageDir "strategies"
if (-not (Test-Path $strategiesDir)) { New-Item -ItemType Directory -Path $strategiesDir | Out-Null }
$packages = Get-ChildItem -Path $mq5Dir -Filter "*.yo4x" -Recurse -ErrorAction SilentlyContinue
foreach ($pkg in $packages) {
    Copy-Item -Force $pkg.FullName (Join-Path $strategiesDir $pkg.Name)
    Write-Host "  -> Bundled strategy: $($pkg.Name)" -ForegroundColor Green
}

# Add Starter Batch
$batContent = "@echo off`r`nstart """" ""%~dp0YO4X.exe""`r`nexit`r`n"
Set-Content -LiteralPath (Join-Path $stageDir "Start-YO4X.bat") -Value $batContent -Encoding ASCII

# 6. Authenticode Code Signing
if ($CertThumbprint) {
    Write-Host "`n[5/6] Signing Executables with Authenticode..." -ForegroundColor Yellow
    $exePath = Join-Path $stageDir "YO4X.exe"
    $signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
    if (Test-Path $signtool) {
        & $signtool sign /sha1 $CertThumbprint /tr http://timestamp.digicert.com /td sha256 /fd sha256 $exePath
        Write-Host "  -> Authenticode signed: $exePath" -ForegroundColor Green
    } else {
        Write-Warning "signtool.exe not found at default path. Ensure binary is signed prior to production!"
    }
} else {
    Write-Warning "`n[5/6] Skipping Code Signing (No certificate thumbprint provided)."
}

# 7. Create Zip Archive & Calculate SHA-256
Write-Host "`n[6/6] Generating Release Zip & Manifest..." -ForegroundColor Yellow
$zipFileName = "YO4X-v$Version-Windows-x64.zip"
$zipPath = Join-Path $releaseDir $zipFileName
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
$sha256 = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$sizeBytes = (Get-Item $zipPath).Length

# 8. Generate releases.json Manifest
$manifest = [ordered]@{
    '$schema' = "https://updates.yo4x.com/schemas/releases.v1.json"
    channel = $Channel
    latestVersion = $Version
    minSupportedVersion = "1.0.0"
    publishedAt = [DateTime]::UtcNow.ToString("o")
    isCritical = $false
    changelog = [ordered]@{
        title = "YO4X Release v$Version"
        summary = "Automated OTA production release."
        highlights = @(
            "Performance and stability improvements",
            "Upgraded strategy execution pipeline"
        )
    }
    package = [ordered]@{
        version = $Version
        fileName = $zipFileName
        url = "https://updates.yo4x.com/releases/win-x64/$Channel/$zipFileName"
        sha256 = $sha256
        sizeBytes = $sizeBytes
    }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 10
$manifestPath = Join-Path $releaseDir "releases.json"
Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8

Write-Host "`n=========================================================" -ForegroundColor Green
Write-Host "  OTA RELEASE BUILD COMPLETE!" -ForegroundColor Green
Write-Host "  Package:  $zipPath" -ForegroundColor White
Write-Host "  Size:     $([Math]::Round($sizeBytes / 1MB, 2)) MB" -ForegroundColor White
Write-Host "  SHA256:   $sha256" -ForegroundColor White
Write-Host "  Manifest: $manifestPath" -ForegroundColor White
Write-Host "=========================================================" -ForegroundColor Green

# 9. Optional Upload to S3 / Cloudflare R2
if ($S3Bucket) {
    Write-Host "`nDeploying to $S3Bucket..." -ForegroundColor Cyan
    # Upload package with immutable caching
    aws s3 cp $zipPath "$S3Bucket/releases/win-x64/$Channel/$zipFileName" `
        --cache-control "public, max-age=31536000, immutable"

    # Upload manifest with zero caching
    aws s3 cp $manifestPath "$S3Bucket/releases/win-x64/$Channel/releases.json" `
        --cache-control "no-cache, no-store, must-revalidate, max-age=0"
    Write-Host "  -> Successfully deployed to CDN edge." -ForegroundColor Green
}
