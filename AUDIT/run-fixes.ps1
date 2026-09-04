<#
.SYNOPSIS
  Applies audit findings via agy (Gemini) in write mode - one call per target file.
.DESCRIPTION
  Reads AUDIT/fixes.json (findings grouped by file) and dispatches one agy write-agent
  per file. Each agent is scoped to exactly one file so two agents never touch the same
  file, and the diff stays reviewable.

  Deliberately EXCLUDES changes that are architectural or product decisions rather than
  defect fixes - see $Excluded. Those are handled by hand.
.EXAMPLE
  pwsh AUDIT/run-fixes.ps1 -Filter 'src/Runtime' -Throttle 3
  pwsh AUDIT/run-fixes.ps1 -Only 'src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs'
#>
[CmdletBinding()]
param(
  [int]$Throttle = 3,
  [string]$Filter = '',
  [string[]]$Only = @(),
  [string[]]$MinSeverity = @('P0','P1','P2','P3'),
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$auditDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $auditDir
$agy      = Join-Path $env:USERPROFILE '.claude\plugins\cache\agy-local\agy\1.0.1\scripts\agy-run.ps1'
$logDir   = Join-Path $auditDir 'fixlogs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

# Files whose "fix" is an architectural or product decision, not a defect repair.
# Changing these blind would be worse than leaving them: RLS changes the runtime
# contract for every connection, and the README wording is a deliberate statement.
$Excluded = @(
  'src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql',
  'src/BuildingBlocks/YO4X.Persistence.Postgres/Security/README.md',
  'src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql'
)

# Testing/Mq5 is a corpus of third-party vendor EAs, kept as compile fixtures. The K08
# findings against it - embedded credentials, user32.dll imports, Telegram command
# channels - are real, but they describe what those vendors shipped. Editing someone
# else's EA would destroy the fixture and prove nothing; that corpus needs a quarantine
# decision, not a patch.
$ExcludedPrefixes = @('Testing/Mq5', 'docs/', 'mt5-net-api-full-binaries-main', 'tests/')

# tests/ is excluded from the automated pass on purpose. An agent editing a test can turn a
# regression into a green build, and one already added a test that could not run at all.
# Findings against tests are reviewed by hand instead.

$groups = Get-Content (Join-Path $auditDir 'fixes.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$queue = @()
foreach ($g in $groups) {
  $rel = $g.file -replace '\\','/'
  if ($Excluded -contains $rel)                        { continue }
  if ($ExcludedPrefixes | Where-Object { $rel.StartsWith($_) }) { continue }
  if ($Only.Count -gt 0 -and $Only -notcontains $rel)  { continue }
  if ($Filter -and $rel -notlike "*$Filter*")          { continue }
  # A file already carrying a clean fix log was handled in an earlier pass. Re-running it
  # would spend quota re-deciding settled findings, and risks an agent undoing a fix a
  # previous one made correctly.
  $priorLog = Join-Path $logDir (($rel -replace '[\\/]','__') + '.log')
  if ((-not $Force) -and (Test-Path $priorLog)) {
    $priorHead = Get-Content $priorLog -TotalCount 1 -ErrorAction SilentlyContinue
    if ($priorHead -notmatch 'AGY_ERROR') {
      Write-Host ("skip  {0} (fixed in an earlier pass)" -f $rel) -ForegroundColor DarkGray
      continue
    }
  }
  $keep = @($g.findings | Where-Object { $MinSeverity -contains $_.sev })
  if ($keep.Count -eq 0) { continue }
  if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $rel))) {
    Write-Host ("MISSING  {0} (skipping)" -f $rel) -ForegroundColor Yellow
    continue
  }
  $queue += [pscustomobject]@{ File = $rel; Findings = $keep }
}

Write-Host ""
Write-Host ("{0} file(s) queued, {1} finding(s), throttle {2}." -f `
  $queue.Count, ($queue | ForEach-Object { $_.Findings.Count } | Measure-Object -Sum).Sum, $Throttle) -ForegroundColor Cyan
Write-Host ""
foreach ($q in $queue) {
  Write-Host ("  {0,2} [{1}] {2}" -f $q.Findings.Count, (($q.Findings.sev | Sort-Object) -join ','), $q.File)
}
Write-Host ""
if ($DryRun) { Write-Host "dry run - nothing dispatched." -ForegroundColor Yellow; return }
if ($queue.Count -eq 0) { return }

function New-FixPrompt {
  param($File, $Findings)
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine("You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.")
  [void]$sb.AppendLine("")
  [void]$sb.AppendLine("THE ONLY FILE YOU MAY MODIFY:")
  [void]$sb.AppendLine("  $File")
  [void]$sb.AppendLine("")
  [void]$sb.AppendLine("Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.")
  [void]$sb.AppendLine("")
  [void]$sb.AppendLine("FINDINGS TO FIX (" + $Findings.Count + "):")
  $i = 1
  foreach ($f in $Findings) {
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("[$i] [$($f.sev)] $($f.title)")
    [void]$sb.AppendLine("    Where:   $($f.where)")
    if ($f.failure) { [void]$sb.AppendLine("    Failure: $($f.failure)") }
    if ($f.fix)     { [void]$sb.AppendLine("    Suggested fix: $($f.fix)") }
    $i++
  }
  [void]$sb.AppendLine("")
  [void]$sb.AppendLine(@"
HOW TO WORK:

1. Verify each finding against the actual code BEFORE changing anything. Line numbers may
   have shifted. If a finding is WRONG, or was already fixed, or the suggested fix would
   itself introduce a bug - do NOT apply it. Say so in your summary and move on. A refused
   bad fix is a good outcome; applying a wrong fix to a trading system is not.

2. Make the SMALLEST change that actually fixes the defect. Do not refactor, rename,
   reorder, reformat, restyle, or "improve" anything you were not asked about. Do not
   reflow existing lines. The diff must contain only the fix.

3. Match the surrounding code exactly - its naming, its comment density and voice, its
   error-handling idiom, its use of existing helpers. Read enough of the file to know what
   that is. Where the file already has a helper for what you need, use it rather than
   writing a new one.

4. Preserve public API and behaviour that was not identified as defective. If a correct
   fix would require changing a public signature, a database schema, a serialised contract,
   or shared behaviour outside this file, DO NOT do it - report it as needing a wider
   change instead.

5. This code decides real trades. For anything touching money, volume, price, margin, order
   state or time: be conservative, prefer failing closed over guessing, and preserve
   existing rounding/normalisation conventions unless the finding is specifically that the
   convention is wrong.

6. The project builds clean with zero warnings. Keep it that way - no unused variables, no
   unreachable code, no nullable warnings.

AFTER EDITING, output a short plain-text summary (no code fences), one line per finding:
  [n] APPLIED  - <what you changed, in a few words>
  [n] SKIPPED  - <why the finding was wrong or the fix unsafe>
Then a final line: FILES CHANGED: <the one path you edited, or NONE>
"@)
  return $sb.ToString()
}

$work = {
  param($AgyPath, $RepoRoot, $BriefRelPath, $LogPath)
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  $OutputEncoding           = [System.Text.Encoding]::UTF8
  # The brief is passed by PATH, never as argument text. Windows PowerShell 5.1 escapes
  # native arguments badly: a prompt containing a backtick, quote, angle bracket or paren
  # gets word-split and agy rejects it with "unexpected argument". A short, plain-ASCII
  # prompt that points at a file sidesteps that entirely.
  $prompt = "Read the file $BriefRelPath in this repository. It is your complete task brief. Follow it exactly."
  try {
    $raw = & $AgyPath -Prompt $prompt -Dir $RepoRoot -Timeout '9m' -Write
    ($raw | Out-String).Trim() | Out-File -FilePath $LogPath -Encoding utf8
  } catch {
    "AGY_ERROR: $($_.Exception.Message)" | Out-File -FilePath $LogPath -Encoding utf8
  }
}

$briefDir = Join-Path $auditDir 'fixbriefs'
if (-not (Test-Path $briefDir)) { New-Item -ItemType Directory -Path $briefDir | Out-Null }

$jobs = @(); $n = 0
foreach ($q in $queue) {
  while (@(Get-Job -State Running).Count -ge $Throttle) { Start-Sleep -Seconds 5 }
  $slug      = $q.File -replace '[\\/]','__'
  $briefPath = Join-Path $briefDir ($slug + '.md')
  New-FixPrompt -File $q.File -Findings $q.Findings | Out-File -FilePath $briefPath -Encoding utf8
  $briefRel  = "AUDIT/fixbriefs/$slug.md"
  $log       = Join-Path $logDir ($slug + '.log')
  $jobs     += Start-Job -Name $q.File -ScriptBlock $work -ArgumentList $agy, $repoRoot, $briefRel, $log
  $n++
  Write-Host ("dispatch [{0}/{1}] {2}" -f $n, $queue.Count, $q.File)
}

Write-Host ""
Write-Host "Waiting for fix agents..." -ForegroundColor Cyan
$null = Wait-Job -Job $jobs
$null = Receive-Job -Job $jobs -ErrorAction SilentlyContinue
Remove-Job -Job $jobs -Force -ErrorAction SilentlyContinue

Write-Host ""
$quota = 0; $done = 0
foreach ($q in $queue) {
  $log = Join-Path $logDir (($q.File -replace '[\\/]','__') + '.log')
  if (-not (Test-Path $log)) { continue }
  $head = (Get-Content $log -TotalCount 1)
  if ($head -match 'AGY_ERROR|quota') { $quota++ } else { $done++ }
}
Write-Host ("Fix agents finished: {0} ok, {1} errored/quota-blocked." -f $done, $quota) -ForegroundColor Green
Write-Host "Logs: AUDIT/fixlogs/    Review the diff with: git diff --stat"
