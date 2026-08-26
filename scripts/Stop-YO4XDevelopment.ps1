[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$developmentRoot = Join-Path $workspaceRoot ".local\development"
$statePath = Join-Path $developmentRoot "processes.json"
$pgCtl = Join-Path $workspaceRoot ".tools\postgresql-local\package-native-18.6-1\pgsql\bin\pg_ctl.exe"
$postgresBin = Split-Path $pgCtl -Parent

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    Write-Host "No workspace-owned YO4X development stack is recorded."
    exit 0
}
$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
if (-not [string]::Equals([IO.Path]::GetFullPath($state.workspace), $workspaceRoot,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "The development state does not belong to this workspace."
}

function Stop-OwnedProcess {
    param([Parameter(Mandatory=$true)] $Record)
    $process = Get-Process -Id ([int]$Record.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    $expected = [IO.Path]::GetFullPath([string]$Record.executable)
    $actual = try { [IO.Path]::GetFullPath($process.Path) } catch { return }
    $start = $process.StartTime.ToUniversalTime().ToString('O')
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($start, [string]$Record.startTimeUtc, [StringComparison]::Ordinal)) {
        Write-Warning "Refusing to stop PID $($Record.pid) because its executable or start time does not match the workspace record."
        return
    }
    Stop-Process -Id $process.Id
    try { Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue } catch {}
}

foreach ($name in @('desktop', 'frontend', 'control-plane', 'identity')) {
    $record = $state.processes | Where-Object name -eq $name | Select-Object -First 1
    if ($null -ne $record) { Stop-OwnedProcess $record }
}
$postgresRecord = $state.processes | Where-Object name -eq 'postgres' | Select-Object -First 1
if ($null -ne $postgresRecord -and (Get-Process -Id ([int]$postgresRecord.pid) -ErrorAction SilentlyContinue)) {
    $postgresData = [IO.Path]::GetFullPath([string]$state.postgresData)
    $developmentPrefix = $developmentRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $postgresData.StartsWith($developmentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to stop PostgreSQL outside the workspace development directory."
    }
    $visualCppDirectory = Get-ChildItem 'C:\Program Files (x86)\Microsoft\Edge\Application' -Directory `
        -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Where-Object {
            Test-Path (Join-Path $_.FullName 'vcruntime140.dll')
        } | Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($visualCppDirectory)) {
        throw "The Microsoft-signed Visual C++ runtime required by PostgreSQL was not found."
    }
    $originalPath = $env:PATH
    try {
        $env:PATH = "$postgresBin;$visualCppDirectory;$originalPath"
        & $pgCtl -D $postgresData -m fast -w -t 30 stop | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "The workspace PostgreSQL process did not stop cleanly." }
    }
    finally { $env:PATH = $originalPath }
}
Remove-Item -LiteralPath $statePath -Force
Write-Host "The workspace-owned YO4X development stack has stopped. Persistent local data was preserved."
