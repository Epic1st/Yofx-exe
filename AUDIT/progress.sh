#!/usr/bin/env bash
# Regenerates AUDIT/PROGRESS.md by scanning findings/. Race-free: agents never touch it.
cd "$(dirname "$0")/.."
OUT=AUDIT/PROGRESS.md
total=$(grep -c '^| `[A-Z][0-9]' AUDIT/ASSIGNMENTS.md 2>/dev/null || echo 0)
done_n=$(ls AUDIT/findings/*.md 2>/dev/null | wc -l | tr -d ' ')
{
  echo "# YO4X Fleet Audit — Progress"
  echo
  echo "_Regenerated: $(date -u '+%Y-%m-%dT%H:%M:%SZ')_"
  echo
  echo "**Lanes reported: ${done_n} / ${total}**"
  echo
  echo "| ID | Lane | P0 | P1 | P2 | P3 | Status |"
  echo "|---|---|---|---|---|---|---|"
  for f in $(ls AUDIT/findings/*.md 2>/dev/null | sort); do
    id=$(sed -n 's/^agent_id: *//p' "$f" | head -1)
    lane=$(sed -n 's/^lane: *//p' "$f" | head -1)
    st=$(sed -n 's/^status: *//p' "$f" | head -1)
    p0=$(grep -c '^### \[P0\]' "$f"); p1=$(grep -c '^### \[P1\]' "$f")
    p2=$(grep -c '^### \[P2\]' "$f"); p3=$(grep -c '^### \[P3\]' "$f")
    echo "| ${id:-?} | ${lane:-?} | $p0 | $p1 | $p2 | $p3 | ${st:-?} |"
  done
  echo
  echo "## Totals"
  echo
  for s in P0 P1 P2 P3; do
    n=$(grep -h "^### \[$s\]" AUDIT/findings/*.md 2>/dev/null | wc -l | tr -d ' ')
    echo "- **$s**: $n"
  done
  echo
  echo "## All P0 / P1 findings"
  echo
  grep -H '^### \[P0\]\|^### \[P1\]' AUDIT/findings/*.md 2>/dev/null \
    | sed 's|AUDIT/findings/||; s|\.md:### | — |' | sed 's/^/- /' || echo "_none yet_"
  echo
  echo "## Not yet reported"
  echo
  for id in $(grep -o '^| `[A-Z][0-9]*`' AUDIT/ASSIGNMENTS.md 2>/dev/null | tr -d '|` '); do
    ls AUDIT/findings/${id}-*.md >/dev/null 2>&1 || echo "- $id"
  done
} > "$OUT"
echo "wrote $OUT (${done_n}/${total} lanes)"
