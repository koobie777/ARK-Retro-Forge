# Phase 9 — Rename + Organize

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Scope discipline:** implement Phase 9 and stop. No GUI.

**Roadmap change:** this phase is new. In `CLAUDE.md`, insert it as Phase 9 and renumber the Avalonia GUI to Phase 10. Nothing depends on the GUI's number, so this is a one-line edit rather than a reshuffle.

---

## This is what the naming subsystem was built for

Phase 3 built a tokenizer, a formatter, a 9,363-name corpus, a boundary rule and a round-trip invariant — all in service of an operation that does not yet exist. Phase 5 threads a `IsRenameEligible` flag through every verified unit for the same reason.

**And renaming is precisely what v1 destroyed collections doing.** `(USA) (USA) (USA)`, `(Disc 1) (Disc 1)`, language tags landing in region slots. Every prohibition in `CLAUDE.md` about naming was written from that wreckage. This phase is where the foundation is either vindicated or isn't.

---

## Two modes, and they must never be confused

| Mode | Name comes from | Requires |
|---|---|---|
| **Canonicalize** | The DAT entry the unit's **hash** matched | Verified state |
| **Normalize** | Reformatting the unit's **existing** name | Parseable name; explicit opt-in |

**Canonicalize is authoritative.** The name is the DAT's, and the hash proved which entry applies. This is the default and the only mode that runs without a flag.

**Normalize is repair, not identification.** It fixes stacked tags, scrambled order, `Disk`/`Disc` variants and un-inverted articles in a collection that was previously damaged — including by v1. It makes no claim that the resulting name is correct, only that it is well-formed. It requires an explicit flag and is reported as a distinct operation.

**ARK never derives a canonical name from a filename.** The tokenizer exists to *understand* names, never to *authorize* them. A unit with no DAT match has no known canonical name — it is refused and reported, never guessed at. This is the single rule that separates this phase from v1.

---

## Verification gates renaming

Only **Verified** units may be canonicalized. The flag already exists; here it becomes load-bearing.

A corrupt file renamed to its canonical name looks verified forever after — worse than v1's damage, which at least announced itself in the filename. Mismatched, In Progress, and Unrecognized units are reported and left alone.

---

## Check before write

**A unit already carrying its canonical name is skipped — no filesystem write.**

The check always runs; the write only happens when it changes something. On a set that is already 100% conformant this makes the whole operation a no-op, which is both correct and kind to the drive.

**Compare ordinally.** A case-insensitive comparison would skip `game (usa).zip` → `Game (USA).zip`, which is a real correction.

---

## Filesystem traps

These will each bite on the first real run.

**Case-only renames.** Windows filesystems are case-insensitive but case-preserving, so `File.Move` between two names differing only in case is unreliable — it may throw, or silently no-op. Stage through a temporary name. Test it explicitly.

**Collisions.** Two units canonicalizing to the same name means either they are duplicates (Phase 7's job) or one is misidentified. **Refuse and report both.** Never overwrite. Never append `(2)` — a disambiguating suffix is a fabricated name, which is the thing this phase must never produce.

**Swaps and cycles.** If A must become B while B must become C, renaming A first destroys B. The planner must **project the filesystem across the whole batch** — the same technique Phase 6's undo preconditions use — and stage through temporary names where a cycle exists. A batch that is individually valid and collectively destructive is the worst possible outcome here.

---

## Archives are renamed, never rewritten

A unit is `Game (USA).zip` containing `Game (USA).gb`. **This phase renames the archive only.**

The entry inside keeps its name. Rewriting an archive means recompressing it — slow, changes the archive bytes, and touches ROM data for a cosmetic gain. DAT verification reads the entry regardless of its name, so nothing downstream cares. If entry-name conformance is ever wanted, it is a separate explicit operation.

Rename stays a pure filesystem move: instant, reversible, and it never touches a byte of ROM content.

---

## Organize is a different operation

**Rename** changes a filename in place. **Organize** moves units into a directory structure. Both are journaled and reversible; they are reported separately and neither is a removal.

Default destination structure is the DAT name (`Nintendo - Game Boy`), which real collections already use — the reference drive's folders are byte-identical to their DAT names. Configurable beyond that.

---

## Gate

Every APPLY gate also asserts `ark undo` restores the set exactly.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. Only **Verified** units are canonicalized; Mismatched, In Progress and Unrecognized are reported and untouched
4. The canonical name comes from the **DAT entry**, never from the existing filename
5. A unit with no DAT match is refused — no name is invented
6. Normalize is a separate explicit mode, reported distinctly, and never claims the name is correct
7. **A unit already correctly named is skipped with no filesystem write**
8. The skip check is ordinal — a case-only difference is renamed, not skipped
9. **A case-only rename succeeds on a case-insensitive filesystem**
10. Renaming twice produces identical results; the second run writes nothing
11. A collection carrying v1-style damage (`(USA) (USA) (USA)`) normalizes to a single correct token
12. Two units canonicalizing to the same name are **refused and reported** — never overwritten, never suffixed
13. A batch containing a swap (A→B, B→A) completes without destroying either file
14. A batch containing a longer cycle completes correctly
15. The planner projects the filesystem across the batch before planning any move
16. Archives are renamed, never rewritten — archive bytes and ROM bytes both unchanged
17. Organize is distinct from rename, journaled, reversible, and not reported as removal
18. Nothing moves without `--apply`; DRY-RUN is the default
19. Every rename is journaled
20. `ark undo` after an APPLY restores the set byte-for-byte
21. An interrupted APPLY reverses exactly what completed
22. Directories showing active-download signals are refused for APPLY while still reported
23. `CLAUDE.md` records this phase and the GUI is renumbered to Phase 10
24. All prior phase gates still pass

Then stop and report.

---

## Hand review

Definition of Done #3 applies with force here. Run it against a real set — the reference collection is already 100% conformant, so **the correct result is that nothing happens**, and confirming that is the point. Then run it against a deliberately damaged copy carrying stacked tags, scrambled order, and case errors, and confirm it repairs them.
