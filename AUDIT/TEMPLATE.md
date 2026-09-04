---
agent_id: <ID>
lane: <short lane name>
scope:
  - <exact path 1>
  - <exact path 2>
status: COMPLETE
generated: <UTC ISO8601>
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# <ID> — <lane name>

## Scope audited
List every file you actually opened and reviewed, with line counts. If you could not
reach a file in your scope, say so here and why — do not silently skip it.

## Verdict
Two to four sentences. What is the actual state of this area? Is it sound, shaky, or
broken? An area that is genuinely clean should be stated as clean.

## Findings

### [P0] <one-line title>
- **Where:** `path/to/file.cs:123`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  <exact quote, 1-8 lines>
  ```
- **Failure:** <concrete input/state -> concrete wrong outcome>
- **Fix:** <the specific change>

### [P1] <one-line title>
...

(Repeat per finding, ordered P0 first. No findings? Write `None.` and say why the area
holds up — that is a real result.)

## Referrals
Real suspicions outside your scope. One line each: `path/to/file — what looks wrong`.
Do not investigate these. `None.` if empty.

## Coverage gaps
Specific untested branches in your scope where a bug could hide. Name the branch, not
"needs tests". `None.` if empty.
