param(
    [string]$Directory = "Testing\Mq5"
)

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$targetDir = [IO.Path]::GetFullPath((Join-Path $workspaceRoot $Directory))

if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

Write-Host "Auto-Watcher started for strategy directory: $targetDir" -ForegroundColor Cyan

$fsw = New-Object IO.FileSystemWatcher $targetDir, "*.*"
$fsw.IncludeSubdirectories = $true
$fsw.EnableRaisingEvents = $true

$lastSync = [DateTime]::MinValue
$action = {
    $now = [DateTime]::UtcNow
    if (($now - $lastSync).TotalSeconds -lt 2) { return }
    $lastSync = $now
    Write-Host "[Auto-Watch] Change detected in $targetDir, auto-syncing catalog..." -ForegroundColor Yellow
    & (Join-Path $workspaceRoot "scripts\Sync-Catalog.ps1")
}

Register-ObjectEvent $fsw "Created" -Action $action | Out-Null
Register-ObjectEvent $fsw "Changed" -Action $action | Out-Null
Register-ObjectEvent $fsw "Renamed" -Action $action | Out-Null

while ($true) {
    Start-Sleep -Seconds 60
}
