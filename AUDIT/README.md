# YO4X Fleet Audit

A 156-lane defect audit of the YO4X platform, executed by a fleet of Gemini 3.7 agents
driven through the Antigravity CLI bridge. Read-only: no agent modifies source.

## The files

| File | What it is | Who writes it |
|---|---|---|
| `CHARTER.md` | Binding rules every agent obeys. Evidence standard, severity scale, what is forbidden. | Human. Agents read it first. |
| `ASSIGNMENTS.md` | The 156 lanes. Each has an ID, an exact file scope, and a focus. Scopes are disjoint. | Human. |
| `TEMPLATE.md` | The report shape every agent emits. | Human. |
| `findings/<ID>-<slug>.md` | One report per lane. | The lane agent, exactly one file each. |
| `PROGRESS.md` | Live status board: who reported, severity counts, every P0/P1, who is missing. | `progress.sh`, regenerated. |
| `progress.sh` | Rebuilds `PROGRESS.md` by scanning `findings/`. | Human. Run it any time. |

## Why it is built this way

**Disjoint scopes stop duplicate findings.** A file belongs to exactly one lane. Without
this, 156 agents independently rediscover the same handful of obvious issues and the
signal drowns. When an agent spots something outside its lane it writes one line under
`## Referrals` instead of investigating — the owning agent gets there properly.

**The charter is read by the agent, not just by us.** Every agent's first instruction is
to read `CHARTER.md`. That is the mechanism that keeps 156 independent agents producing
comparable output instead of 156 different essay formats.

**Findings go to disk, digests come back.** Each agent writes its full report to its own
file and returns only a ≤10-line digest. Reports never flow back through the orchestrator,
so the audit can be arbitrarily large without exhausting context.

**Evidence is mandatory.** `file:line`, an exact code quote, and a concrete failure
scenario — inputs in, wrong behaviour out. A finding that cannot state how it breaks is
deleted rather than softened. Padding is explicitly called out in the charter as the worst
possible outcome, because a fleet this size can generate plausible-sounding noise faster
than anyone can triage it.

**One writer per file.** Agents never touch `PROGRESS.md`; it is derived by scanning the
findings directory. Nothing races.

## Severity

- **P0** — exploitable, or loses money / data / positions.
- **P1** — wrong behaviour under reachable conditions.
- **P2** — robustness: unhandled failure, leak, missing validation.
- **P3** — quality that will cause a future defect.

## Running it

```bash
bash AUDIT/progress.sh     # rebuild the status board
cat AUDIT/PROGRESS.md      # who reported, what they found, who is missing
```

To re-run a single lane, look up its row in `ASSIGNMENTS.md` and dispatch one agent with
that scope and focus; it overwrites its own file and nothing else.

## Caveat on the output

These are machine-generated findings. Every P0 and P1 needs a human to confirm the
failure scenario before anyone changes code on the strength of it. The audit is a
prioritised list of places to look, not a verdict.
