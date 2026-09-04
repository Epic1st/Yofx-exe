<#
.SYNOPSIS
  Drives the fix fleet to completion across Gemini quota windows.
.DESCRIPTION
  Same shape as orchestrate.ps1, for the fix phase. run-fixes.ps1 already skips a file
  whose log shows a clean prior run and retries one whose log holds an AGY_ERROR, so
  completion is a matter of running it repeatedly and waiting out each quota refusal.
.EXAMPLE
  pwsh AUDIT/orchestrate-fixes.ps1 -MaxPasses 6 -Throttle 20
#>
[CmdletBinding()]
param(
  [int]$MaxPasses = 6,
  [int]$Throttle = 20,
  [int]$QuotaWaitMinutes = 40
)

$ErrorActionPreference = 'Stop'
$auditDir = $PSScriptRoot
$logDir   = Join-Path $auditDir 'fixlogs'

function Get-OutstandingFixes {
  # Computed from the same inputs run-fixes.ps1 uses, NOT by parsing its console output:
  # that script reports its queue with Write-Host, which bypasses stdout entirely, so a
  # pipeline reading it sees nothing and concludes there is no work.
  $repoRoot = Split-Path -Parent $auditDir
  $excludedPrefixes = @('Testing/Mq5', 'docs/', 'mt5-net-api-full-binaries-main', 'tests/')
  $excluded = @(
    'src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql',
    'src/BuildingBlocks/YO4X.Persistence.Postgres/Security/README.md',
    'src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql'
  )
  $groups = Get-Content (Join-Path $auditDir 'fixes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $out = @()
  foreach ($g in $groups) {
    $rel = $g.file -replace '\\','/'
    if ($excluded -contains $rel) { continue }
    if ($excludedPrefixes | Where-Object { $rel.StartsWith($_) }) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $rel))) { continue }
    $log = Join-Path $logDir (($rel -replace '[\\/]','__') + '.log')
    if (Test-Path $log) {
      $head = Get-Content $log -TotalCount 1 -ErrorAction SilentlyContinue
      if ($head -notmatch 'AGY_ERROR') { continue }
    }
    $out += $rel
  }
  return @($out)
}

$start = (Get-OutstandingFixes).Count
Write-Host ""
Write-Host ("FIX ORCHESTRATOR: {0} file(s) outstanding, up to {1} pass(es), throttle {2}." -f `
  $start, $MaxPasses, $Throttle) -ForegroundColor Cyan

for ($pass = 1; $pass -le $MaxPasses; $pass++) {
  $before = (Get-OutstandingFixes).Count
  if ($before -eq 0) { Write-Host "FIX ORCHESTRATOR: nothing outstanding." -ForegroundColor Green; break }

  Write-Host ("=== FIX PASS {0}/{1} - {2} file(s) outstanding ===" -f $pass, $MaxPasses, $before) -ForegroundColor Cyan
  & (Join-Path $auditDir 'run-fixes.ps1') -Throttle $Throttle | Out-Null

  $after = (Get-OutstandingFixes).Count
  $done  = $before - $after
  Write-Host ("FIX PASS {0}: {1} file(s) handled, {2} still outstanding." -f $pass, $done, $after)

  if ($after -eq 0) { Write-Host "FIX ORCHESTRATOR: nothing outstanding." -ForegroundColor Green; break }

  if ($done -eq 0) {
    Write-Host ("FIX PASS {0}: no progress - quota is spent. Sleeping {1} minute(s)." -f `
      $pass, $QuotaWaitMinutes) -ForegroundColor Yellow
    Start-Sleep -Seconds ($QuotaWaitMinutes * 60)
  } else {
    Write-Host ("FIX PASS {0}: partial progress. Pausing 5 minutes." -f $pass)
    Start-Sleep -Seconds 300
  }
}

$remaining = Get-OutstandingFixes
Write-Host ""
Write-Host ("FIX ORCHESTRATOR DONE. {0} of {1} file(s) handled." -f ($start - $remaining.Count), $start) -ForegroundColor Green
if ($remaining.Count -gt 0) {
  Write-Host ("Still outstanding: {0}" -f $remaining.Count) -ForegroundColor Yellow
  Write-Host "Re-run this script to continue."
}
