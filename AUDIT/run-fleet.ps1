<#
.SYNOPSIS
  Runs the remaining YO4X audit lanes through the agy (Antigravity CLI) Gemini bridge.
.DESCRIPTION
  One Gemini agent per lane, throttled. Each lane writes exactly one report to
  AUDIT/findings/<ID>-<slug>.md. Lanes whose report already exists are skipped, so the
  script is resumable: run it again and it only picks up what is missing.
.EXAMPLE
  pwsh AUDIT/run-fleet.ps1 -Throttle 10
  pwsh AUDIT/run-fleet.ps1 -Only F01,F03 -Force
#>
[CmdletBinding()]
param(
  [int]$Throttle = 10,
  [string[]]$Only = @(),
  [switch]$Force,
  [int]$MinBytes = 900
)

$ErrorActionPreference = 'Stop'
$auditDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $auditDir
$findings = Join-Path $auditDir 'findings'
$agy      = Join-Path $env:USERPROFILE '.claude\plugins\cache\agy-local\agy\1.0.1\scripts\agy-run.ps1'

if (-not (Test-Path $agy))      { throw "agy bridge not found at $agy" }
if (-not (Test-Path $findings)) { New-Item -ItemType Directory -Path $findings | Out-Null }

$lanes = Get-Content (Join-Path $auditDir 'lanes.json') -Raw -Encoding UTF8 | ConvertFrom-Json

# ---- build the work queue, skipping lanes that already reported ----------------
$queue = @()
foreach ($l in $lanes) {
  if ($Only.Count -gt 0 -and $Only -notcontains $l.id) { continue }
  $out = Join-Path $findings ("{0}-{1}.md" -f $l.id, $l.slug)
  if ((-not $Force) -and (Test-Path $out) -and ((Get-Item $out).Length -ge $MinBytes)) {
    # A quota refusal can still be large: the bridge echoes the whole prompt back with
    # the error. Size alone would treat that as a finished report and skip the lane
    # forever, so the first line decides.
    $body = Get-Content $out -Raw -ErrorAction SilentlyContinue
    if ($body -notmatch 'AGY_ERROR' -and $body -match 'agent_id:') {
      Write-Host ("skip  {0} (already reported)" -f $l.id) -ForegroundColor DarkGray
      continue
    }
  }
  $queue += [pscustomobject]@{ Lane = $l; Out = $out }
}

Write-Host ""
Write-Host ("Queued {0} lane(s), throttle {1}." -f $queue.Count, $Throttle) -ForegroundColor Cyan
Write-Host ""
if ($queue.Count -eq 0) { return }

# ---- the prompt every lane gets, varying only in id / lane / scope / focus ------
function New-LanePrompt {
  param($Id, $Lane, $Scope, $Focus)
@"
You are agent $Id in a 156-agent audit of YO4X, a live MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine).

FIRST read these two files at the repo root and obey them exactly - they are binding:
  AUDIT/CHARTER.md
  AUDIT/TEMPLATE.md

LANE: $Id - $Lane

SCOPE (file findings ONLY against these; read anything else for context only):
$Scope

FOCUS: $Focus

Read every file in your scope completely, then produce your report.

OUTPUT: emit ONLY the finished report, in the exact structure of AUDIT/TEMPLATE.md, starting with the '---' YAML front matter and ending with the Coverage gaps section. No preamble, no wrapping code fence, no closing commentary. Use real line numbers and exact code quotes. Every finding needs a concrete failure scenario - specific input or state in, specific wrong behaviour out. Delete any finding you cannot substantiate; an honest short report beats a padded one, and inventing findings to look thorough is the worst possible outcome. If the area is genuinely clean, say so plainly - that is a valid and useful result.
"@
}

# ---- the per-lane job: run agy, retry once on error, write the report -----------
$work = {
  param($AgyPath, $RepoRoot, $OutPath, $BriefRelPath)
  # agy emits UTF-8; a job runspace defaults to the ANSI codepage and would mangle
  # every em-dash and quote in the report. Force UTF-8 on the way in and out.
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  $OutputEncoding           = [System.Text.Encoding]::UTF8
  # The brief goes by PATH, never as argument text. Windows PowerShell 5.1 escapes native
  # arguments badly: a prompt carrying a backtick, quote, angle bracket or paren gets
  # word-split and agy rejects it with "unexpected argument".
  # Pointing at a brief invites agy to treat the lane as a multi-step task and answer with
  # a plan awaiting approval. Saying plainly that the response IS the deliverable keeps it
  # doing the work instead of proposing it.
  $prompt = "Read $BriefRelPath and carry it out now. Do not write a plan, do not create any file, and do not ask for approval. Your entire response must BE the finished report the brief specifies, starting with the --- front matter."
  $text = ''
  for ($attempt = 1; $attempt -le 2; $attempt++) {
    try {
      $raw  = & $AgyPath -Prompt $prompt -Dir $RepoRoot -Timeout '9m'
      $text = ($raw | Out-String)
    } catch {
      $text = "AGY_ERROR: $($_.Exception.Message)"
    }
    # A real report carries the template's front matter. A plan, an approval request or a
    # refusal does not - and size alone cannot tell them apart.
    if ($text -notmatch 'AGY_ERROR' -and $text -match 'agent_id:') { break }
    if ($attempt -lt 2) { Start-Sleep -Seconds 20 }
  }
  if ($text -notmatch 'agent_id:' -and $text -notmatch 'AGY_ERROR') {
    $text = "AGY_ERROR: no report front matter returned (agy answered with a plan or refusal).`n`n" + $text
  }
  $text.Trim() | Out-File -FilePath $OutPath -Encoding utf8
}

# ---- dispatch with throttling ---------------------------------------------------
$briefDir = Join-Path $auditDir 'lanebriefs'
if (-not (Test-Path $briefDir)) { New-Item -ItemType Directory -Path $briefDir | Out-Null }

$started = 0
$jobs    = @()
foreach ($q in $queue) {
  while (@(Get-Job -State Running).Count -ge $Throttle) { Start-Sleep -Seconds 4 }

  $briefPath = Join-Path $briefDir ("{0}.md" -f $q.Lane.id)
  New-LanePrompt -Id $q.Lane.id -Lane $q.Lane.lane -Scope $q.Lane.scope -Focus $q.Lane.focus |
    Out-File -FilePath $briefPath -Encoding utf8
  $briefRel = "AUDIT/lanebriefs/$($q.Lane.id).md"

  $job = Start-Job -Name $q.Lane.id -ScriptBlock $work `
                   -ArgumentList $agy, $repoRoot, $q.Out, $briefRel
  $jobs += $job
  $started++
  Write-Host ("start {0,-4} {1,-28} [{2}/{3}]" -f $q.Lane.id, $q.Lane.slug, $started, $queue.Count)
}

Write-Host ""
Write-Host "All lanes dispatched. Waiting for completion..." -ForegroundColor Cyan
$null = Wait-Job -Job $jobs
$null = Receive-Job -Job $jobs -ErrorAction SilentlyContinue
Remove-Job -Job $jobs -Force -ErrorAction SilentlyContinue

# ---- report what landed ----------------------------------------------------------
Write-Host ""
$ok = 0; $bad = @()
foreach ($q in $queue) {
  if ((Test-Path $q.Out) -and ((Get-Item $q.Out).Length -ge $MinBytes)) {
    $body = Get-Content $q.Out -Raw -ErrorAction SilentlyContinue
    if ($body -match 'AGY_ERROR' -or $body -notmatch 'agent_id:') { $bad += $q.Lane.id } else { $ok++ }
  } else { $bad += $q.Lane.id }
}
Write-Host ("DONE. {0} report(s) written, {1} failed." -f $ok, $bad.Count) -ForegroundColor Green
if ($bad.Count -gt 0) {
  Write-Host ("Failed lanes: {0}" -f ($bad -join ' ')) -ForegroundColor Yellow
  Write-Host "Re-run to retry them: pwsh AUDIT/run-fleet.ps1"
}
