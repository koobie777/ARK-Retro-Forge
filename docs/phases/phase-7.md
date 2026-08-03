# Phase 7 — Deduplication

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Scope discipline:** implement Phase 7 and stop. No variant policy, no renaming, no collection reports.

---

## This is the first phase that modifies user files

Everything through Phase 6 was read-only or touched ARK's own config. Dedup moves the user's ROMs.

That changes the standard. Every gate below that involves an APPLY must also assert that `ark undo` restores the set exactly. A dedup that works but cannot be reversed is a failed phase, not a partial one.

---

## Carry-over from Phase 6

Two items, both small, both before new work:

1. **`DeleteFile` inverse must check content like `WriteText` now does.** If a session created a file and the user edited it afterwards, deleting it on undo destroys that edit — the symmetric case of the defect already fixed for the restore path. Refuse without `--force`.
2. **Architecture test: only `JournalInverter` constructs `RemoveDirectory` or `DeleteFile`.** "Produced by inversion, never by an operation" is currently convention. Make it structural, the same way the Executor rule is.

---

## What dedup is, and is not

**Duplicates are byte-identical ROM content.** Same hash, genuinely redundant, safe to collapse.

**Variants are not duplicates.** Rev 0 and Rev 1 have different hashes. So do Beta, Proto, Demo, and Sample. Removing them is a curation preference — that is Phase 8, a different subsystem with a different risk profile, and the two must not be conflated in code or in the report.

**Compare ROM hashes, not archive hashes.** Zip compression is not deterministic, so two archives holding the identical ROM have different archive bytes. Phase 5 already computes the inner-ROM hash; use it.

---

## Hard rules

**Operate on `GameUnit`s, never on files inside them.** A unit is one archive for cartridges. Disc units are stubbed. Individual track files are never independently removed — the failure this prevents is deleting a byte-identical Disc 2 out of an otherwise complete multi-disc set because the hash matched something elsewhere.

**Never delete. Quarantine.** Removals move to `quarantine/<session-id>/` with a manifest, on the same volume by default — a cross-volume move is a copy, and quarantining hundreds of gigabytes across volumes turns an instant operation into hours. Purge is a separate, explicit, opt-in verb and is **not** in this phase.

**Report-only is the default.** First run must never quarantine anything. `--apply` is required, and it is required per run.

**Verification state gates participation:**

| State | Dedup behaviour |
|---|---|
| Verified | Eligible |
| Unrecognized | Eligible — an unidentified file can still have an exact twin |
| **In Progress** | **Excluded.** A file still being written will change; its hash means nothing. |
| Mismatched | Reported, not acted on. Two identical corrupt files are two corrupt files; collapsing them leaves one corrupt file and hides the second. |
| Excluded | Not ROM content |

**Active-download directories are refused for write operations**, per `CLAUDE.md`. Reports still run over them.

---

## Tiered comparison

Dedup is a set operation, so it can be cheap:

1. **Group by ROM size.** Free, from scan. A unique size cannot have a duplicate and is never hashed.
2. **CRC32 the survivors.** From the Phase 5 cache where available.
3. **SHA1 only on CRC32 collision.** 32-bit collisions are real at 1.5M-entry scale; SHA1 settles them.

A duplicate group is only ever confirmed by full hash equality, never by size or name.

---

## Which copy to keep

Selectable, and the default is to decide nothing:

| Policy | Behaviour |
|---|---|
| **Report only** | **Ships as default.** Groups are listed; nothing moves. |
| Keep identified | Prefer a unit in a ROM-set directory with a resolved DAT over a loose copy elsewhere |
| Keep shortest path | Prefer the least-nested location |
| Keep oldest / newest | By mtime |
| Ask per group | Interactive |

**When a policy cannot break a tie, the group is reported and skipped — never resolved arbitrarily.** Prohibition 6 applies: a coin flip presented as a decision is a guess wearing a confident label.

---

## Quarantine manifest

Written into `quarantine/<session-id>/`, and it must be sufficient to understand the operation without the journal:

- Original path, quarantine path
- ROM hash, ROM size
- Which unit was kept, and why that one
- The policy that made the call

The journal is what reverses it; the manifest is what explains it.

---

## Gate

Every APPLY gate must also assert undo restores the set exactly.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. **`DeleteFile` inverse refuses a file edited since the session created it, without `--force`**
4. **Architecture test: only `JournalInverter` constructs `RemoveDirectory` or `DeleteFile`**
5. Two archives with identical ROM content but different archive bytes are detected as duplicates
6. Two units with the same name and different content are **not** duplicates
7. Different revisions of the same title are **not** duplicates
8. Unique-sized units are never hashed
9. A CRC32 collision is resolved by SHA1, not accepted
10. Nothing moves without `--apply`
11. In Progress units are excluded from duplicate groups entirely
12. Mismatched units are reported and not acted on
13. A directory showing active-download signals is refused for APPLY while still reporting
14. Quarantine lands on the same volume by default
15. Quarantine manifest is written and self-sufficient
16. Every quarantine action is journaled
17. **`ark undo <session>` after a dedup APPLY restores the set byte-for-byte**
18. **A dedup APPLY interrupted partway leaves a journal that reverses exactly what completed**
19. A tie the policy cannot break is reported and skipped, never resolved arbitrarily
20. Report-only is the default; a first run with no flags quarantines nothing
21. All prior phase gates still pass

Then stop and report. Phase 8 is the variant policy engine.
