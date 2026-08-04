# Phase 10 — Disc Units

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Roadmap change:** insert this as Phase 10 and renumber the Avalonia GUI to Phase 11, so the document reads in build order.

**Scope discipline:** disc **resolution and verification** only. No CHD, no conversion, no ripping — those need external process orchestration, which no phase has built and which sits outside the `Executor` guarantee. Stop at the gate.

---

## This is where v1 died

Multi-track read as multi-disc. Multi-disc read as variants. CUE sheets rewritten from assumptions instead of contents. A thousand-game PSX set corrupted badly enough to need re-downloading.

Every prohibition in `CLAUDE.md` traces to that wreckage, and the reference drive is still mostly unsupported because of it: **1,762 PSX, 659 GameCube, 140 PS2, 172 PSP, 62 PS3.** More of the collection sits outside ARK than inside it.

---

## What has already been solved elsewhere

Do not rebuild these. Confirm they hold.

- **Disc number is an identity axis** (Phase 8). `(Disc 1)` and `(Disc 2)` can never be collapsed as variants of each other. The multi-disc destruction scenario is already structurally prevented — verify it still is with disc units in play.
- **Operations take whole units** (Prohibition 4). Now load-bearing: quarantining a BIN without its CUE breaks the game.
- **Prefer matching the DAT variant over transforming the file** (Phase 2.1, Phase 5). This is the big simplification — see below.

---

## Do not rewrite CUE sheets

v1 generated CUE sheets from what it *believed* the structure was. That is the root defect.

**Redump DATs hash every constituent file, the `.cue` included.** So a CUE that matches its DAT hash is correct, provably, and must never be touched. Verification is a comparison, not a repair.

Regenerating a CUE is a fallback for one that does *not* match — and it is **out of scope for this phase**. Report the mismatch. Do not fix it here.

This is the same rule that let a headered NES ROM match the Headered DAT untouched. It applies with more force to discs, because the file being rewritten would be the one describing where the game's data lives.

---

## The three "multi" cases are distinct

They share no code path. Conflating any two is what produced v1's damage.

| Case | Structure | Unit shape |
|---|---|---|
| **Multi-track** | One game, one disc, audio tracks split across several BINs | **One unit** |
| **Multi-disc** | One game, N discs | **N units**, grouped into a set |
| **Single-file image** | One BIN or one ISO | **One unit** |

**Multi-track is not multi-disc.** A CUE with twelve TRACK entries is one disc. The track count says nothing about disc count.

**Multi-disc units are never merged.** Disc 1 and Disc 2 are two units that belong to one set. The set is a grouping over units, not a unit itself.

---

## Resolution

**The CUE is the manifest.** Parse it to learn which BINs belong to the unit — never infer membership from filename similarity. A CUE naming a file that isn't present is an **incomplete unit**: reported, not resolved, and never partially assembled.

Shapes to handle, all present on the reference drive:

- **Archive containing a disc image** — `Game (USA).zip` holding `Game (USA).bin` + `Game (USA).cue`. The 1,762 PSX case.
- **Bare ISO** — `PSP\Roms`, 90 files, no archive wrapper.
- **Loose CUE + BINs** on disk, no archive.

Descriptor formats: `.cue` (required), plus `.gdi` and `.ccd` recognized and reported even if not fully parsed — say so rather than silently mis-resolving them.

**NKit RVZ stays unsupported this phase.** It is a container format needing external tooling; report it as such, as scan already does.

---

## Multi-disc sets move together or not at all

A set is removed whole or untouched. Quarantining Disc 2 of a three-disc set leaves a broken game and a user who does not know it.

This is the hazard identified before any code existed — a byte-identical Disc 2 shared between two releases, deleted because a hash matched, gutting a complete set. Phase 7 prevented it by operating on units; this phase must extend that to sets.

**Verification propagates to the set.** A set is complete only when every disc in it verifies. One damaged disc makes the set incomplete, and that is what the report should say.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. A CUE plus the BINs it references resolves to **one** disc unit
4. Membership comes from **parsing the CUE**, never from filename similarity
5. A CUE with many TRACK entries is **one** unit — track count never implies disc count
6. `(Disc 1)` and `(Disc 2)` resolve to **two** units, grouped as a set, never merged
7. Disc units are never treated as variants of one another — the Phase 8 identity rule still holds
8. A CUE referencing a missing file is reported **incomplete**, never partially resolved
9. A disc unit verifies only when **every** constituent file matches its DAT entry
10. **A CUE matching its DAT hash is never rewritten** — no CUE is written in this phase at all
11. A CUE whose hash does not match is **reported**, not repaired
12. An archive containing `.bin` + `.cue` resolves to a disc unit instead of unsupported-format
13. A bare `.iso` resolves as a single-file disc unit
14. Loose CUE + BINs on disk resolve without an archive
15. `.gdi` and `.ccd` are recognized and reported, never silently mis-resolved
16. No operation ever acts on a constituent file independently of its unit
17. A multi-disc set is removed whole or not at all
18. Set completeness reflects every disc: one damaged disc makes the set incomplete
19. Scan on the real PSX set resolves 1,762 units instead of reporting them unsupported
20. Anything that writes is DRY-RUN by default, journaled, and reversed exactly by `ark undo`
21. All prior phase gates still pass

Then stop and report.

---

## Hand review

Run against the real PSX set — 1,765 archives, the set v1 destroyed. Confirm unit counts, multi-disc grouping, and that verification against a Redump DAT is possible once one is imported. `ark dat sync` has never been exercised against a live network; this is the phase that needs it.

Expect the real drive to surface something neither of us predicted. It has in every phase so far.
