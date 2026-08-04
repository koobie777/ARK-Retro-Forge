# Phase 8.5 — Collection Reports

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Scope discipline:** implement Phase 8.5 and stop. No GUI.

---

## No new subsystem

This is the join the pipeline was built to produce:

| Source | Answers |
|---|---|
| DAT catalog (Phase 2) | What exists |
| Scan (Phase 4) | What you have |
| Verification (Phase 5) | Whether what you have is correct |
| Policy (Phase 8) | What you *want* |

Nothing here computes anything new. If a report needs a capability that does not already exist, that is a signal the join is being done wrong.

**Read-only throughout.** Nothing moves, nothing is quarantined. `ark undo` has no role in this phase.

---

## The key inversion: apply the policy to the catalog

Phase 8 runs the policy over **your files** to decide what to remove.
Phase 8.5 runs the same policy over **the catalog** to decide what you should have.

Same grouping, same ranking, same axes, same refusals — different input. That means the Phase 8 engine is reused, not reimplemented. If curation and target-set derivation drift apart, the reports stop describing the collection the user is actually curating toward.

**A user's target set is the policy's output over the DAT.** Nothing else.

---

## Completeness is meaningless without a target

A full No-Intro DAT carries every region, revision, proto, beta, sample, unlicensed and aftermarket release. Compared against a USA retail collection it reports tens of thousands missing — nearly all Japanese releases and prototypes nobody asked for. The number would be true and useless.

**Never compute "missing" against the whole DAT.** The target set comes from the declared policy, and the report states which policy produced it. Changing the policy must change the missing count and nothing else — that is the test that proves the two halves share an engine.

---

## Four states, not three

| State | Meaning | What the user does |
|---|---|---|
| **Present** | In the target set, on disk, Verified | Nothing |
| **Missing** | In the target set, not on disk | Acquire |
| **Damaged** | In the target set, on disk, fails verification | Re-acquire *this specific title* |
| **Unrecognized** | On disk, in no DAT | Investigate — bad dump, hack, homebrew, or a file from elsewhere |

**Damaged is distinct from Missing and the distinction is actionable.** A Mismatched file for a title outside the target set is noise; the same file for a title inside it is a gap you can close. Phase 5 produces the state; this phase supplies the context that makes it matter.

**Upgradable** is reported alongside these: present and Verified, but the target set contains a higher-ranked entry — usually a later revision. It is the sleeper feature. A missing list tells you what to hunt; an upgrade list tells you what you believe is fine and isn't current.

---

## Pivots

By system, by region, or both. Region comes from the parsed tokens of catalog entries, not from filenames.

**Region matching is set intersection, not equality.** A DAT entry tagged `(USA, Europe)` satisfies a USA target. `(World)` satisfies every region target and must never be collapsed into a region list.

---

## Performance

Joining ~1.5M catalog entries against ~10k units, with tokenization on the catalog side.

- **Tokenize the catalog lazily and cache it.** Tokenizing 1.5M names on every run is unacceptable; tokenizing only the DATs in scope is not.
- The join must be indexed, never O(n·m).
- Report progress on anything that takes more than a moment — the `dat import` lesson.

---

## Export is a work queue, not a wall of text

The missing list is what feeds back into acquisition. It must be machine-readable (`--json`) and, in the rendered form, one line per title with enough to search on: canonical name, system, region, revision.

Field parity between human and `--json` output, per the discipline established in Phase 1.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. Missing, damaged, unrecognized, and upgradable are computed against a **policy-declared target set**, never the whole DAT
4. **Changing the policy changes the missing count and nothing else** — the target set derives from the same engine curation uses
5. A Mismatched unit for a title inside the target set reports **Damaged**; the same unit outside it does not inflate the missing count
6. Upgradable is reported when a higher-ranked entry exists in the target set
7. A target set the policy cannot rank produces the same refusal curation does — reported, never guessed
8. Pivots by system, by region, and by both
9. Region matching is set intersection: `(USA, Europe)` satisfies a USA target
10. `(World)` satisfies every region target and is never collapsed into a list
11. Catalog tokenization is cached; a second run does not re-tokenize
12. The join is indexed, not O(n·m) — assert on a corpus large enough to matter
13. Report is read-only: a full run leaves the tree byte-identical and writes no journal
14. `--json` and rendered output carry the same fields
15. Export is usable as a re-acquisition queue — one line per title, enough to search on
16. The report names the policy and the target set it was computed against
17. All prior phase gates still pass

Then stop and report. Phase 9 is the Avalonia GUI.
