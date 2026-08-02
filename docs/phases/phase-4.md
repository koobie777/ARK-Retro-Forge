# Phase 4 — Scan + Game Unit Resolution

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Scope discipline:** implement Phase 4 and stop. No hashing of file contents, no verification, no dedup, no policy, no renaming.

**This is the first phase that reads user files.** Everything before it was string work, config, or ARK's own data. Read-only throughout — scan produces an inventory and nothing else.

---

## Carry-over from Phase 3

Add **`ark parse "<filename>"`** — prints the token breakdown for one name: title, each classified token with its category, the unknown bucket, and the canonical reassembly. It was argued for as the CLI's debugging surface and then omitted from the Phase 3 brief. It is the fastest feedback loop available for every phase after this one, and it costs almost nothing now that the tokenizer exists.

---

## Part A — Directory classification

**Scan classifies directories, not files.** Real drives mix ROM sets with emulators, tools, firmware, cheats, and unrelated projects. Folder names are no help: the reference drive uses `No-Intro`, `SNES Roms`, `Minerva_Myrient`, `Nintendo - Game Boy Advance`, `PS2 Downloads`, and `Roms` for the same kind of content.

Two signals, **both required**:

| Signal | Threshold |
|---|---|
| Extension homogeneity — share of files carrying the most common extension | ≥ 90% |
| Naming conformance — share of files containing a recognized region token | ≥ 80% |

Validated against a 22,050-file mixed-use drive: **17 ROM-set directories / 10,045 files** identified; 140 directories / 11,050 files correctly excluded.

Both signals are necessary and neither suffices:

- Cheat directories (`.cht`, `.ps3savepatch`) score **100% homogeneity, 0% conformance**. Homogeneity alone swallows 2,378 files.
- `PSP\Roms` is 90 bare `.iso` files with no archive wrapper — conformance catches it where an extension allowlist would not.

Conformance uses the Phase 3 tokenizer. Directory profiling reads **listings only** — no file is opened to decide whether a directory is a ROM set.

**Known false positive, do not paper over it.** `Nintendo - Wii U - Disc Keys` is 516 flawlessly No-Intro-named archives containing disc keys, not games. Conformant naming does not prove game content. Report it; a third signal is future work.

### Active-download exclusion

Directories showing active-download signals are excluded from write operations. In this phase that means recording the signal, since nothing writes yet — but the detection belongs here, with directory classification.

Signals: incomplete-file extensions (`.!qB`, `.part`, `.!ut`, `.bc!`, `.crdownload`, `.aria2`); a declared incomplete-download directory from settings; recent mtime.

**These are optimizations, not proof.** A real 0.99 GB file missing 16 MB was observed with no incomplete extension and a full pre-allocated size — it passes both checks. Full verification is Phase 5.

---

## Part B — Game units

`Core/Units/`: `IGameUnitResolver`, `GameUnit`.

A **GameUnit** is the atom every later operation acts on. Nothing downstream ever sees a bare file.

| Resolver | Unit |
|---|---|
| `CartridgeUnitResolver` | One archive containing one ROM |
| Disc resolver | Interface defined, implementation **stubbed** |

Built now, while a cartridge unit looks trivially like one file, so disc support later is one new resolver rather than a rewrite of everything downstream.

### Archives are the normal case

The reference corpus is **100% `.zip`** — 9,928 of 9,928. Cartridge sets are distributed archived.

- The parseable name is the **archive's**; identity hashing later reads the **ROM inside**
- Zip compression is not deterministic, so hashing the archive can never match a DAT
- Read entry metadata only — name, size, count. **Do not extract.**
- **Prohibition 11: never call `WriteToDirectory()`.** The architecture test already enforces this.

A cartridge archive containing more than one ROM is an anomaly. Report it; do not guess which is the game.

### Format qualifiers

Set folder names carry format metadata the Phase 2 system definitions already model: `Nintendo 64 (BigEndian)`, `Nintendo Entertainment System (Headered)`, `Nintendo DS (Decrypted)`, `GameCube - NKit RVZ`.

Record the qualifier on the unit. **Do not act on it** — header stripping and byte-order normalization belong to Phase 5.

---

## Part C — Output buckets

Three, never two. Every file is accounted for.

| Bucket | Meaning |
|---|---|
| **Identified** | In a ROM-set directory, name matches a DAT entry via token-set comparison |
| **Candidate** | In a ROM-set directory, no DAT match. Reported, never acted on. |
| **Excluded** | Not ROM content, with a stated reason visible in the report |

Identification here is **by name only** — hash verification is Phase 5. Matching uses `ParsedName` token sets, not formatted strings; see the naming section of `CLAUDE.md`.

Exclusion reasons live in editable config, not code. One user's junk is not the next user's junk.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. `ark parse "<name>"` prints title, categorized tokens, unknown bucket, canonical form
4. **Directory classifier reproduces the reference split: ROM-set directories separated from tooling, cheats, and firmware**
5. A 100%-homogeneous, 0%-conformant directory (cheat files) is excluded — homogeneity alone must not admit it
6. A directory of bare `.iso` files with no archive wrapper is admitted — extension allowlists must not exclude it
7. `Nintendo - Wii U - Disc Keys` is reported as a known false positive, not silently admitted
8. Archive entry lists are read without extraction; `WriteToDirectory` architecture test still passes
9. An archive containing multiple ROMs is reported as an anomaly, not resolved by guessing
10. Format qualifier from the folder name is recorded on the unit
11. Files carrying incomplete-download extensions are flagged
12. Scan is read-only — a full scan of a directory tree leaves it byte-identical and writes no journal
13. No code outside resolvers touches `FileInfo`
14. Identification uses token-set comparison, not string equality
15. Three buckets account for every file; nothing is silently dropped
16. All Phase 0/1/1.5/2/3 gates still pass

Then stop and report. Phase 5 is hashing and verification.
