#!/usr/bin/env bash
# Extracts every finding from the real lane reports and groups them by TARGET FILE,
# emitting AUDIT/fixes.json. One entry per file = one agy write-call, which keeps the
# number of quota-consuming calls proportional to files touched, not findings found.
cd "$(dirname "$0")/.."

TMP=$(mktemp)
for f in AUDIT/findings/*.md; do
  head -1 "$f" | grep -q 'AGY_ERROR' && continue
  lane=$(basename "$f" .md)
  awk -v lane="$lane" '
    /^### \[P[0-3]\]/ {
      sev = $0; sub(/^### \[/, "", sev); sub(/\].*/, "", sev)
      title = $0; sub(/^### \[[^]]*\][ ]*/, "", title)
      where = ""; fix = ""; failure = ""; conf = ""
      inblk = 1; next
    }
    inblk && /^- \*\*Where:\*\*/ && where == "" {
      where = $0; sub(/^- \*\*Where:\*\*[ ]*/, "", where); gsub(/`/, "", where); next
    }
    inblk && /^- \*\*Confidence:\*\*/ && conf == "" {
      conf = $0; sub(/^- \*\*Confidence:\*\*[ ]*/, "", conf); next
    }
    inblk && /^- \*\*Failure:\*\*/ && failure == "" {
      failure = $0; sub(/^- \*\*Failure:\*\*[ ]*/, "", failure); next
    }
    inblk && /^- \*\*Fix:\*\*/ && fix == "" {
      fix = $0; sub(/^- \*\*Fix:\*\*[ ]*/, "", fix)
      # Agents render Where: in three shapes: a bare "path:line", a markdown link
      # "[file.cs:12](file:///C:/.../repo/path/file.cs#L12)", or the link text alone.
      # Prefer the URL when present - it is the only form carrying the full path.
      path = where
      if (match(path, /file:\/\/\/[^) ]+/)) {
        url = substr(path, RSTART, RLENGTH)
        sub(/#.*$/, "", url)
        sub(/^.*\/yo4x\//, "", url)          # make it repo-relative
        path = url
      } else {
        sub(/^\[/, "", path)                  # bare link text, no URL
        sub(/\].*$/, "", path)
      }
      sub(/:[0-9].*$/, "", path)              # drop any trailing :line
      gsub(/^[ \t]+|[ \t]+$/, "", path)
      gsub(/\t/, " ", title); gsub(/\t/, " ", fix); gsub(/\t/, " ", failure)
      printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\n", path, sev, lane, title, where, failure, fix
      inblk = 0; next
    }
    /^## / { inblk = 0 }
  ' "$f"
done | sort > "$TMP"

# group by path -> JSON
awk -F'\t' '
  function esc(s) { gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s); return s }
  BEGIN { print "["; first_file = 1 }
  {
    if ($1 != cur) {
      if (cur != "") { print "\n  ]\n }," }
      cur = $1
      printf "%s{\n  \"file\": \"%s\",\n  \"findings\": [", (first_file ? " " : " "), esc(cur)
      first_file = 0; firstf = 1
    }
    if (!firstf) printf ","
    printf "\n    {\"sev\":\"%s\",\"lane\":\"%s\",\"title\":\"%s\",\"where\":\"%s\",\"failure\":\"%s\",\"fix\":\"%s\"}", \
      esc($2), esc($3), esc($4), esc($5), esc($6), esc($7)
    firstf = 0
  }
  END { if (cur != "") print "\n  ]\n }"; print "]" }
' "$TMP" > AUDIT/fixes.json

rm -f "$TMP"
echo "wrote AUDIT/fixes.json"
