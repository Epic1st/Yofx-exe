[CmdletBinding()]
param(
    [switch] $SkipFrontendBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$frontendRoot = Join-Path $workspaceRoot "src\Frontend\YO4X.Web"
$desktopProject = Join-Path $workspaceRoot "src\Apps\YO4X.Desktop\YO4X.Desktop.csproj"
$desktopWwwRoot = Join-Path $workspaceRoot "src\Apps\YO4X.Desktop\wwwroot"
$distDir = Join-Path $frontendRoot "dist"
$outputDir = Join-Path $workspaceRoot "artifacts\desktop\YO4X.Desktop\win-x64"
$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$nodeDir = Join-Path $workspaceRoot ".tools\node-v24.19.0-win-x64"
$npm = Join-Path $nodeDir "npm.cmd"
$mt5Bridge = Join-Path $workspaceRoot "mt5-net-api-full-binaries-main\mt5api.dll"
$mq5Dir = Join-Path $workspaceRoot "Testing\Mq5"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  YO4X Standalone Desktop Application Packaging (win-x64)" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Build React/Vite Frontend
if (-not $SkipFrontendBuild) {
    Write-Host "`n[1/4] Building React 18 production frontend..." -ForegroundColor Yellow
    $env:PATH = "$nodeDir;" + $env:PATH
    Push-Location $frontendRoot
    try {
        & $npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }
    }
    finally {
        Pop-Location
    }
}

# 2. Copy Frontend Assets to Desktop wwwroot
Write-Host "`n[2/4] Synchronizing static assets to Desktop host wwwroot..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $desktopWwwRoot) {
    Remove-Item -Recurse -Force -LiteralPath $desktopWwwRoot
}
Copy-Item -Recurse -LiteralPath $distDir -Destination $desktopWwwRoot
Write-Host "  -> Synced wwwroot assets successfully." -ForegroundColor Green

# 3. Compile and Publish Standalone Desktop Executable
Write-Host "`n[3/4] Publishing self-contained Win-x64 application..." -ForegroundColor Yellow
& $dotnet publish $desktopProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $outputDir `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Desktop publish compilation failed." }

# 4. Copy MT5 Bridge DLL & Encrypted .yo4x Strategies
Write-Host "`n[4/4] Bundling MT5 socket bridge and proprietary .yo4x strategy containers..." -ForegroundColor Yellow

# Copy MT5 Socket Bridge DLL
if (Test-Path -LiteralPath $mt5Bridge) {
    Copy-Item -Force -LiteralPath $mt5Bridge -Destination $outputDir
    Write-Host "  -> Bundled mt5api.dll" -ForegroundColor Green
}

# Copy .yo4x strategy packages
$destStrategies = Join-Path $outputDir "strategies"
if (-not (Test-Path -LiteralPath $destStrategies)) {
    New-Item -ItemType Directory -Path $destStrategies | Out-Null
}

$yo4xPackages = Get-ChildItem -Path $mq5Dir -Filter "*.yo4x" -Recurse
foreach ($pkg in $yo4xPackages) {
    Copy-Item -Force -LiteralPath $pkg.FullName -Destination (Join-Path $destStrategies $pkg.Name)
    Write-Host "  -> Bundled strategy package: $($pkg.Name) ($($pkg.Length) bytes)" -ForegroundColor Green
}

$exePath = Join-Path $outputDir "YO4X.exe"
if (Test-Path -LiteralPath $exePath) {
    $size = (Get-Item -LiteralPath $exePath).Length / (1024 * 1024)
    Write-Host "`n=========================================================" -ForegroundColor Green
    Write-Host "  BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "  Executable Location: $exePath" -ForegroundColor White
    Write-Host "  Directory: $outputDir" -ForegroundColor White
    Write-Host "  File Size (YO4X.exe): $([Math]::Round($size, 2)) MB" -ForegroundColor White
    Write-Host "  Mode: 100% Standalone Local Execution (Zero Cloud Required)" -ForegroundColor White
    Write-Host "=========================================================" -ForegroundColor Green
}
else {
    throw "Published executable not found at $exePath."
}
