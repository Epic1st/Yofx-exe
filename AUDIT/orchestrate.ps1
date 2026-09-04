<#
.SYNOPSIS
  Drives the audit fleet to completion across Gemini quota windows.
.DESCRIPTION
  105 lanes need more Gemini tokens than one quota window holds. run-fleet.ps1 is
  resumable - it re-runs any lane whose report is missing or still holds an AGY_ERROR -
  so completion is a matter of running it repeatedly and waiting out each refusal.

  Each pass: dispatch what remains, count what landed, and decide. Progress means go
  again immediately. No progress means the quota is spent, so sleep until it resets.
.EXAMPLE
  pwsh AUDIT/orchestrate.ps1 -MaxPasses 8 -Throttle 6
#>
[CmdletBinding()]
param(
  [int]$MaxPasses = 8,
  [int]$Throttle = 6,
  [int]$QuotaWaitMinutes = 40
)

$ErrorActionPreference = 'Stop'
$auditDir = $PSScriptRoot
$findings = Join-Path $auditDir 'findings'

function Get-OutstandingLanes {
  $lanes = Get-Content (Join-Path $auditDir 'lanes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $missing = @()
  foreach ($l in $lanes) {
    $out = Join-Path $findings ("{0}-{1}.md" -f $l.id, $l.slug)
    if (-not (Test-Path $out)) { $missing += $l.id; continue }
    # Outstanding means "no usable report": absent, an agy refusal, or a plan/approval
    # request rather than the report itself. Only the template front matter proves the latter.
    $body = Get-Content $out -Raw -ErrorAction SilentlyContinue
    if ($body -match 'AGY_ERROR' -or $body -notmatch 'agent_id:') { $missing += $l.id }
  }
  return $missing
}

$startCount = (Get-OutstandingLanes).Count
Write-Host ""
Write-Host ("ORCHESTRATOR: {0} lane(s) outstanding, up to {1} pass(es), throttle {2}." -f `
  $startCount, $MaxPasses, $Throttle) -ForegroundColor Cyan
Write-Host ""

for ($pass = 1; $pass -le $MaxPasses; $pass++) {
  $before = (Get-OutstandingLanes).Count
  if ($before -eq 0) {
    Write-Host "ORCHESTRATOR: every lane has reported." -ForegroundColor Green
    break
  }

  Write-Host ("=== PASS {0}/{1} - {2} lane(s) outstanding ===" -f $pass, $MaxPasses, $before) -ForegroundColor Cyan
  & (Join-Path $auditDir 'run-fleet.ps1') -Throttle $Throttle | Out-Null

  $after = (Get-OutstandingLanes).Count
  $done  = $before - $after
  Write-Host ("PASS {0}: {1} lane(s) completed, {2} still outstanding." -f $pass, $done, $after)

  if ($after -eq 0) {
    Write-Host "ORCHESTRATOR: every lane has reported." -ForegroundColor Green
    break
  }

  if ($done -eq 0) {
    # Nothing landed at all, so the refusal is the quota rather than any one lane.
    # Waiting is the only thing that changes the outcome.
    Write-Host ("PASS {0}: no progress - quota is spent. Sleeping {1} minute(s)." -f `
      $pass, $QuotaWaitMinutes) -ForegroundColor Yellow
    Start-Sleep -Seconds ($QuotaWaitMinutes * 60)
  } else {
    # Partial progress means the window ran out mid-pass. A short pause lets it refill
    # a little before the next attempt rather than hammering a spent quota.
    Write-Host ("PASS {0}: partial progress. Pausing 5 minutes." -f $pass)
    Start-Sleep -Seconds 300
  }
}

$remaining = Get-OutstandingLanes
Write-Host ""
Write-Host ("ORCHESTRATOR DONE. {0} of {1} outstanding lane(s) completed." -f `
  ($startCount - $remaining.Count), $startCount) -ForegroundColor Green
if ($remaining.Count -gt 0) {
  Write-Host ("Still outstanding ({0}): {1}" -f $remaining.Count, ($remaining -join ' ')) -ForegroundColor Yellow
  Write-Host "Re-run this script to continue."
}
