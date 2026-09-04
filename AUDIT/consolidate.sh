#!/usr/bin/env bash
# Builds AUDIT/REPORT.md - the consolidated, triage-ordered view of every lane report.
# Derived purely by scanning findings/, so it is safe to re-run at any time.
cd "$(dirname "$0")/.."
OUT=AUDIT/REPORT.md

total_lanes=$(grep -c '^| `[A-Z][0-9]' AUDIT/ASSIGNMENTS.md 2>/dev/null || echo 0)
reported=$(ls AUDIT/findings/*.md 2>/dev/null | wc -l | tr -d ' ')

# Pull every finding of a given severity: "LANE | title | where"
extract() {
  local sev="$1"
  for f in $(ls AUDIT/findings/*.md 2>/dev/null | sort); do
    local lane
    lane=$(basename "$f" .md)
    awk -v sev="$sev" -v lane="$lane" '
      /^### \[/ {
        inblk = ($0 ~ "^### \\[" sev "\\]")
        if (inblk) {
          title = $0
          sub(/^### \[[^]]*\][ ]*/, "", title)
          where = ""
        }
        next
      }
      inblk && /^- \*\*Where:\*\*/ && where == "" {
        where = $0
        sub(/^- \*\*Where:\*\*[ ]*/, "", where)
        gsub(/`/, "", where)
        printf "| %s | %s | `%s` |\n", lane, title, where
        inblk = 0
      }
    ' "$f"
  done
}

{
  echo "# YO4X Fleet Audit — Consolidated Report"
  echo
  echo "_Generated: $(date -u '+%Y-%m-%dT%H:%M:%SZ') · ${reported}/${total_lanes} lanes reported_"
  echo
  echo "Produced by a fleet of Gemini 3.7 agents, one per lane, under \`AUDIT/CHARTER.md\`."
  echo "Full per-lane detail with code quotes lives in \`AUDIT/findings/\`."
  echo
  echo "> **These are machine-generated findings.** Every P0 and P1 needs a human to confirm"
  echo "> the failure scenario before code changes rest on it. This is a prioritised list of"
  echo "> places to look, not a verdict."
  echo

  echo "## Totals"
  echo
  echo "| Severity | Count | Meaning |"
  echo "|---|---|---|"
  for s in P0 P1 P2 P3; do
    n=$(grep -h "^### \[$s\]" AUDIT/findings/*.md 2>/dev/null | wc -l | tr -d ' ')
    case $s in
      P0) m="Exploitable, or loses money / data / positions" ;;
      P1) m="Wrong behaviour under reachable conditions" ;;
      P2) m="Robustness: unhandled failure, leak, missing validation" ;;
      P3) m="Quality that will cause a future defect" ;;
    esac
    echo "| **$s** | $n | $m |"
  done
  echo

  for s in P0 P1; do
    n=$(grep -h "^### \[$s\]" AUDIT/findings/*.md 2>/dev/null | wc -l | tr -d ' ')
    echo "## $s findings ($n)"
    echo
    if [ "$n" -eq 0 ]; then
      echo "_None._"
    else
      echo "| Lane | Finding | Location |"
      echo "|---|---|---|"
      extract "$s"
    fi
    echo
  done

  echo "## Per-lane summary"
  echo
  echo "| Lane | P0 | P1 | P2 | P3 |"
  echo "|---|---|---|---|---|"
  for f in $(ls AUDIT/findings/*.md 2>/dev/null | sort); do
    lane=$(basename "$f" .md)
    p0=$(grep -c '^### \[P0\]' "$f"); p1=$(grep -c '^### \[P1\]' "$f")
    p2=$(grep -c '^### \[P2\]' "$f"); p3=$(grep -c '^### \[P3\]' "$f")
    [ $((p0+p1+p2+p3)) -eq 0 ] && continue
    echo "| [$lane](findings/$lane.md) | $p0 | $p1 | $p2 | $p3 |"
  done
  echo

  echo "## Lanes reporting no findings"
  echo
  clean=""
  for f in $(ls AUDIT/findings/*.md 2>/dev/null | sort); do
    lane=$(basename "$f" .md)
    n=$(grep -c '^### \[P' "$f")
    [ "$n" -eq 0 ] && clean="$clean $lane"
  done
  if [ -n "$clean" ]; then
    echo "These areas were audited and reported clean. That is a result, not a gap —"
    echo "but a clean report on a high-risk area is worth spot-checking."
    echo
    for c in $clean; do echo "- $c"; done
  else
    echo "_None._"
  fi
  echo

  echo "## Lanes not yet reported"
  echo
  missing=""
  for id in $(grep -o '^| `[A-Z][0-9]*`' AUDIT/ASSIGNMENTS.md 2>/dev/null | tr -d '|` '); do
    ls AUDIT/findings/${id}-*.md >/dev/null 2>&1 || missing="$missing $id"
  done
  if [ -n "$missing" ]; then
    echo "Re-run with: \`pwsh AUDIT/run-fleet.ps1\` (it resumes, skipping completed lanes)."
    echo
    for m in $missing; do echo "- $m"; done
  else
    echo "_All lanes reported._"
  fi
} > "$OUT"

echo "wrote $OUT (${reported}/${total_lanes} lanes)"
