# CLAUDE.md — ARK Retro Forge v2

**Standing orders. Read fully before writing a single line.**

---

## Mission

Universal ROM management. One tool, every system, full pipeline: identify → verify → dedupe → curate → rename → organize → convert.

v1 shipped in a week, hit ~42k Reddit impressions, then broke on real collections. v2 exists because v1's foundation was wrong, not because its ideas were.

**v2 scope order:** cartridge systems first (N64, SNES, NES), discs second. Cartridge logic is the same problem without the disc-structure landmines, and it carries over.

---

## Architecture

Three layers.

| Layer | Contains | Rule |
|---|---|---|
| **ARK.Core** | All logic. Parsing, hashing, DAT, policy, planning, execution. | No UI dependencies, and **no logic outside it.** If a service can't be called from a unit test with no console attached, it's in the wrong layer. |
| **ARK.Cli** | Verb/flag parsing. Rendering Core's results. | Thin. If a command file passes 300 lines, logic has leaked — move it to Core. |
| **ARK.Gui** | Avalonia. Later phase. | Renders the same Core services the CLI does. |

**Note on v1:** Core's *package* references were already clean — Spectre lived only in Cli. The failure was logic placement: Cli was 7,463 LOC against Core's 4,982, `Program.cs` was 2,436 lines, and Medical Bay existed as a private method inside it, unreachable from any other frontend. Gates measure **where logic lives**, not what a csproj references.

### Plan → Execute → Journal

One object, four uses. This is the spine of the whole tool.

Operations **never touch the filesystem.** They return a `Plan`: an ordered list of intended actions.

- **DRY-RUN** — build the plan, render it, stop.
- **APPLY** — build the plan, render it, hand it to the single `Executor`.
- **Journal** — the executed plan, serialized to `journal/<session-id>.json`.
- **Undo** — that same plan, inverted.

Only `Executor` may call `File.Move`, `File.Delete`, or `Directory.Create`. No operation code anywhere else may. This makes DRY-RUN safety, quarantine, and reversibility structurally impossible to violate rather than matters of discipline.

---

## Hard Prohibitions

Violating any of these is a defect regardless of whether tests pass.

1. **Never regex a whole filename positionally.** Tokenize against a vocabulary. v1 used `^(.+?)\s*\(([^)]+)\)$`. Verified behavior:
   - `Crash Bandicoot (USA) (USA)` → title `Crash Bandicoot (USA)`, region `USA` — the title swallows the prior tag, the formatter re-appends, and you get `(USA) (USA) (USA)`.
   - `Tekken 3 (USA) (En,Fr,De)` → title `Tekken 3 (USA)`, region `En,Fr,De` — a **language tag lands in the region slot on a clean No-Intro name.** This corrupts correct files on first contact.
   The approach guarantees both. No patch fixes it.

2. **Never fix a bug by special-casing a title.** If a proposed fix contains a specific game name, the fix is wrong and the underlying rule is what needs changing. v1 accumulated per-title patches and never converged.

3. **Never put business logic in a UI layer.**

4. **Never operate on individual files inside a game unit.** All operations take a `GameUnit` — one file (cartridge), or one CUE plus every BIN it references grouped with its disc siblings (disc). Deleting a byte-identical Disc 2 out of an otherwise complete set is the failure class this prevents.

5. **Never delete. Quarantine.** Removals move to `quarantine/<session-id>/` with a manifest, on the same volume by default (a cross-volume move is a copy). Purge is a separate, explicit, opt-in verb.

6. **Never guess identity.** Confident match → proceed. Ambiguous → ranked candidates with "none of the above." No match → skip and log. A skipped file always beats a wrongly-renamed one.

7. **Never let an operation touch the filesystem.** Operations return plans. `Executor` acts. See above.

8. **Never drop an unrecognized token.** Unknown parentheticals go to the unknown bucket — preserved, flagged, surfaced. Some are genuinely part of a title.

9. **Never proceed to the next phase before the current gate passes.** v1 failed because every tool was added before any tool was proven. This is the rule that failure produced.

10. **Never ship a file-touching operation before it is reversible.** `Executor` journals from the moment it exists, in Phase 0. `ark undo` lands before the first destructive verb.

11. **Never call `SharpCompress.IArchive.WriteToDirectory()`.** CVE-2026-44788 (GHSA-6c8g-7p36-r338, CVSS 5.9) is a zip-slip path traversal in that method, escalating to arbitrary file writes on TAR via symlink chaining. **No patched version exists** — every release through 0.47.4 is affected, so a version bump is not a fix. ARK does not need it: scan reads the entry list, hashing streams a single entry. Enforce with an architecture test. If archive extraction ever becomes a user-facing verb, it is hand-rolled with per-entry path validation against the target root, never delegated.

---

## Definition of Done

A component is done when **all four** hold:

1. Test corpus written **before** the implementation
2. 100% of the corpus passes
3. DRY-RUN executed against a real set, output reviewed by hand
4. Logic lives in Core; the CLI only renders

"It works on the examples I was given" is not done. That is the v1 failure mode by name.

---

## Build Phases

Each phase depends only on phases above it.

### Phase 0 — Skeleton
Branch `v2` off `main`. `main` stays at v1.1.0 so current downloads keep working. The v2 branch sets a version prefix so `ark --version` reports `2.0.0-alpha.x` — MinVer would otherwise inherit `1.1.x` and make a rewrite look like a patch. Old code moves to `legacy/` — mined for **data, not code**.

- Solution: `ARK.Core`, `ARK.Cli`, `ARK.Tests`
- Instance path resolution: where DB, DATs, logs, journal, quarantine live per `--instance`
- Serilog wired from the first commit
- `Plan`, `PlannedAction`, `Executor` with journaling built in

**Port clean from v1:** `Crc32`, `FileHasher`, `HashOptions`, `HashResult`, `config/dat/dat-sources.json`
**Do not port:** `PsxNameParser`, `PsxRenamePlanner`, `CleanPsxCommand`, `Program.cs`

> **Gate:** `dotnet build` and `dotnet test` green. `ark --version` responds. A synthetic 3-action plan executes and writes a journal.

### Phase 1 — Medical Bay (extraction, not rewrite)
The v1 logic works, it's just trapped in `Program.cs`. Lift it into `Core/Diagnostics/MedicalBayService.cs` returning a `MedicalBayReport`. Port `ToolManager`, `ExternalTool`, `ToolCheckResult`. CLI renders only.

Exists to establish the Core/UI boundary on low-risk code. Every later component copies its shape.

> **Gate:** `ark medical-bay` matches v1 output. `MedicalBayService` unit-tested with no console attached. Zero logic in the CLI command file.

### Phase 2 — DAT infrastructure
Download, parse, index, cache. **Add No-Intro sources** — v1's `dat-sources.json` has 79 entries: 77 Redump, 2 No-Intro. Cartridge work has almost no DAT backing until that's fixed.

> **Gate:** `ark dat sync --system n64` fetches and indexes. Second run hits cache. Catalog queryable by name and by hash.

### Phase 3 — Naming (parser + formatter)
`Core/Naming/`: `TokenVocabulary`, `NameTokenizer`, `NameFormatter`, `ParsedName`. Vocabulary per `ARK-FILENAME-VOCABULARY.md`.

Pure string work, zero I/O — the fastest feedback loop in the project. Corpus in two halves:
- **Clean** — harvested from Phase 2's No-Intro DATs. Every name in a DAT is canonical by definition and covers releases nobody owns.
- **Dirty** — synthesized by corrupting clean names: stacked tags, scrambled order, GoodTools brackets, `Rev A` vs `Rev 1`, inverted articles, copier-header artifacts.

> **Gate:** `parse(format(parse(x))) == parse(x)` across the entire corpus. Language tags never classify as regions. Unknown bucket never silently empties. 100% on both halves.

### Phase 4 — Scan + game unit resolution
`Core/Units/`: `IGameUnitResolver`, `GameUnit`. `CartridgeUnitResolver` returns one unit per archive. Disc resolver interface defined, implementation stubbed.

**Scan classifies directories, not files.** Real drives mix ROM sets with emulators, tools, firmware, cheats, and unrelated projects. Folder names are no help — the reference drive uses `No-Intro`, `SNES Roms`, `Minerva_Myrient`, `Nintendo - Game Boy Advance`, and `PS2 Downloads` for the same kind of thing.

Two signals, both required:

| Signal | Threshold |
|---|---|
| Extension homogeneity — share of files with the most common extension | ≥ 90% |
| Naming conformance — share of files containing a recognized region token | ≥ 80% |

Validated against a 22,050-file mixed-use drive: **17 ROM-set directories / 10,045 files** identified, 140 directories / 11,050 files correctly excluded.

Both signals are necessary. Cheat directories (`.cht`, `.ps3savepatch`) score 100% homogeneity and 0% conformance — homogeneity alone swallows them. `PSP\Roms` is 90 bare `.iso` files with no archive — conformance catches it where an extension allowlist would not.

Directory profiling reads listings only. No file is opened to decide whether a directory is a ROM set.

**Three output buckets, never two:**
1. **Identified** — DAT hash or name match
2. **Candidate** — in a ROM-set directory, no DAT match. Reported, never acted on.
3. **Excluded** — with a stated reason, visible in the report

Exclusion rules live in editable config, not in code. One user's junk is not the next user's junk.

**Known gap:** conformant naming does not prove game content. `Nintendo - Wii U - Disc Keys` is 516 flawlessly No-Intro-named archives containing disc keys, not games. Needs a third check on folder qualifier or archive contents.

Built now, while a cartridge unit looks trivial, so disc support later is one new resolver instead of a downstream rewrite.

> **Gate:** No code outside resolvers touches `FileInfo`. `ark scan <root>` on a mixed drive reports the three buckets with reasons. Directory classifier reproduces the 17/140 split on the reference corpus.

### Phase 5 — Hashing + cache
Tiered: group by size (free) → CRC32 survivors → SHA1 only on CRC collisions. Unique sizes are never hashed. SQLite cache keyed path + size + mtime, per instance.

> **Gate:** Second run over an unchanged set performs approximately zero hashing.

### Phase 6 — `ark undo`
The journal already exists from Phase 0. This adds inversion and the verb.

> **Gate:** A synthetic session of moves, renames, and quarantines reverses to the exact starting state. Survives process restart and partial failure.

### Phase 7 — Deduplication
Hash-identical `GameUnit`s only. This is **not** variant handling — different revisions have different hashes and are not duplicates.

> **Gate:** Nothing moves without `--apply`. Quarantine manifest and journal written. `ark undo` restores the set exactly.

### Phase 8 — Variant policy engine
Selectable, savable policies. Not a hardcoded rule — a preservationist and a casual player have opposite correct answers.

- Report only **(ships as default)**
- Keep latest revision
- Keep retail only
- Sort variants into subfolders
- Ask per group
- Custom category rules

> **Gate:** Absent rev tag ranks as Rev 0 / oldest. Pre-release builds never auto-ordered — date tag or ask. `v1.10` sorts above `v1.9`.

### Phase 9 — GUI (Avalonia)
Flagship interface over the same Core. Confirmation flows — candidate picking, dedup review, variant approval — are where a GUI genuinely beats a terminal.

---

## Known Traps

| Trap | Detail |
|---|---|
| Absent Rev tag | Means Rev 0 — the **oldest**. "Keep latest" silently keeping the original is the likeliest Phase 8 bug. |
| `Rev A` vs `Rev 1` | Separate schemes, never compared. v1's `[\d.]+` didn't match `Rev A` at all — verified invisible. |
| `v1.10` vs `v1.9` | String sort inverts this. Parse numeric segments. |
| Rev vs Version | Different schemes. v1 conflated them in one regex. |
| Region vs language | `(Japan)` region, `(Ja)` language, `(No)` Norwegian. Vocabulary decides, position only breaks ties. |
| Pre-release ordering | Alpha/Beta/Proto have no consistent cross-publisher timeline. Retail beats all; between them use a date tag or ask. |
| SNES copier headers | 512 bytes prepended. Same ROM, different hash. Detect and strip before hashing. |
| N64 byte order | `.z64` / `.v64` / `.n64` — same game, three hashes. Normalize to z64 before identity. |
| NES header variants | iNES 1.0 vs NES 2.0. Same ROM data, different hash. |
| Article inversion | `Legend of Zelda, The` and `The Legend of Zelda` group as one game. Multilingual articles. |
| Cross-volume quarantine | A "move" across volumes is a copy. Same volume by default. |
| Clean-set bias | US-only No-Intro sets contain none of the messy cases. Passing on them alone means little. |

---

## When to Stop and Ask

- Two plausible readings of a filename token
- A vocabulary gap — a token that should be recognized but isn't in the tables
- Any change touching more than one phase's code
- Any moment the fix under consideration involves naming a specific game

---

## Maintenance

Update when: verbs are added · a phase gate passes · systems are added · architecture changes · branch strategy changes · version milestones land.

---

*Stage in DRY-RUN. Prove the gate. Then move.*
