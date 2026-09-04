param(
  [int]$Port = 5184
)

$nodeExe = Join-Path $PSScriptRoot "..\.tools\node-v24.19.0-win-x64\node.exe"
if (-not (Test-Path $nodeExe)) {
  $nodeExe = "node"
}

$serverScript = Join-Path $PSScriptRoot "..\src\Apps\YO4X.Admin.Portal\server.mjs"

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  Starting YO4X Dedicated Admin Portal on Port 5184    " -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  URL:         http://localhost:5184" -ForegroundColor Yellow
Write-Host "  Admin Email: admin@yo4x.com" -ForegroundColor Yellow
Write-Host "  Password:    Password123!" -ForegroundColor Yellow
Write-Host "=======================================================" -ForegroundColor Cyan

& $nodeExe $serverScript
