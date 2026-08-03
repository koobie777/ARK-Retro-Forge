# Phase 6 — `ark undo`

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Scope discipline:** implement Phase 6 and stop. No dedup, no policy, no renaming.

**Why this comes before anything destructive.** Prohibition 10: no file-touching operation ships before it is reversible. Phase 7 is the first phase that moves user files. Undo lands first so there is never a window where ARK can do something it cannot take back.

The journal has existed since Phase 0 and every `Executor` run writes one. This phase adds inversion, preconditions, and the verb.

---

## Part A — Journal integrity

### Audit every persisted enum before anything else

Phase 5 found that `RomFormat` was cached as an integer ordinal. Removing one enum member silently relabelled 592 cached rows — wrong data served confidently, with nothing to indicate it.

**The journal has the same exposure and far worse consequences.** If `ActionKind` persists as an ordinal, adding or removing a member reorders every value in every existing journal. `ark undo` then replays a session as the wrong operations: a `Move` read as a `Quarantine`, a `Rename` read as a `Delete`. The safety net inverts into the hazard, with no signal until the damage is visible.

Before writing undo:

- Audit **every** enum that reaches disk or SQLite — journal `ActionKind`, exclusion reasons, verification states, anomaly codes, scan buckets
- Persist by **name**, never by ordinal
- An unrecognized name degrades explicitly to an `Unknown` member, and anything carrying `Unknown` is **refused by undo**, not guessed at
- Add an architecture or serialization test asserting no enum is persisted by ordinal

### Schema version

Journals gain a schema version if they lack one. A journal whose version is newer than the running build is **refused**, not best-guessed. A user who upgrades, undoes, then downgrades must not silently get a mangled replay.

---

## Part B — Inverse operations

Undo replays a session's actions in **reverse order**, inverting each.

| Action | Inverse | Notes |
|---|---|---|
| `Move` A→B | `Move` B→A | Straightforward |
| `Rename` A→B | `Rename` B→A | Same as Move |
| `Quarantine` A→Q | `Move` Q→A | Restores to original path |
| `CreateDirectory` D | Remove D **only if empty** | Never delete a directory the user has since filled |
| `WriteText` P | Restore prior content | **See below — this is the hard case** |

### `WriteText` is not currently invertible

`PlannedAction` carries the content being written. It does not carry what was there before. So a `WriteText` over an existing file cannot be reversed from the journal alone.

Two acceptable resolutions — pick one and state it plainly:

1. **Capture prior content in the journal** when the target exists. Simple, works, and grows the journal by the size of what's overwritten. Acceptable for settings; unacceptable if `WriteText` ever carries something large.
2. **Declare `WriteText` non-invertible** and have undo refuse a session containing one, naming the reason.

Option 1 is preferred for settings-sized payloads. Whichever is chosen, undo must never silently skip an action it cannot reverse.

---

## Part C — Preconditions

**Undo verifies before it acts. It never assumes the world is unchanged.**

Before inverting each action, check the current state matches what the journal expects:

- The source of the inverse exists
- The destination of the inverse does **not** exist — restoring over a different file is data loss wearing a safe name
- For quarantine restore, the original path is still free

When a precondition fails: **stop, report which action and why, change nothing further.** Do not partially apply, do not skip and continue, do not guess.

Offer `--force` for the case where a user genuinely wants to overwrite — but it must be explicit, per-run, and never the default.

### Undo is itself journaled

An undo run writes its own journal, marked as reversing session *X*. This makes undo auditable and means a mistaken undo is itself visible. Do not build undo-of-undo as a feature; the journal record is enough.

### Partial failure

Undo must be as crash-safe as `Executor`. Journal each reversal as it succeeds. A process killed mid-undo leaves an accurate record of exactly what was reversed, and re-running resumes rather than double-applying.

---

## Part D — CLI surface

- `ark journal list` — sessions with id, timestamp, operation, action count, and whether already reversed
- `ark journal show <session-id>` — the actions in a session
- `ark undo <session-id>` — DRY-RUN by default, showing every inverse it would apply
- `ark undo <session-id> --apply` — executes

DRY-RUN default applies here as it does everywhere. Undo is a write operation on user files and gets no exemption.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. **No enum is persisted by ordinal anywhere** — asserted by test
4. A journal containing an unrecognized enum name is **refused**, not guessed
5. A journal with a newer schema version is refused
6. A synthetic session of moves, renames, and quarantines reverses to the **exact** starting state
7. Actions are inverted in reverse order
8. `CreateDirectory` inverse removes the directory only when empty; a directory the user has filled is left alone
9. `WriteText` is handled per the chosen resolution, and undo never silently skips an action it cannot reverse
10. Precondition failure stops the run, reports the offending action, and changes nothing further
11. Restoring over an existing file is refused without `--force`
12. `ark undo` without `--apply` is DRY-RUN and touches nothing
13. Undo writes its own journal, marked as reversing the target session
14. A process killed mid-undo leaves an accurate journal; re-running resumes without double-applying
15. `ark journal list` shows sessions and their reversed state
16. All prior phase gates still pass

Then stop and report. Phase 7 is deduplication — the first phase that modifies user files.
