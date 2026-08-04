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

   **DRY-RUN scope.** The DRY-RUN default protects *user data* — ROM collections, archives, anything the user would grieve. It does not extend to ARK's own configuration: `config set` writes the value the user just typed, and previewing that is ceremony, not safety. Config writes still go through `Plan` → `Executor` and are still journaled; only the `--apply` requirement is lifted. This boundary is deliberate and closed. Any future request to skip `--apply` on an operation touching user data is refused, and this clause is not precedent for it.

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

> **Gate:** `MedicalBayService` unit-tested with no console attached. Human render and `--json` serialize the same report object, enforced by a field-parity test. Zero logic in the CLI command file.
>
> Output deliberately **does not** match v1: `--json` now carries the full report rather than the tool array alone, and the global tool-missing verdict is removed. Requirements are per-operation.

**Forward note — external processes are outside the Executor guarantee.** `File.Move`/`Delete`/`WriteAllText`/`CreateDirectory` are covered; launching `chdman` to write a CHD is not. When tool execution returns in a later phase, external tools write to a staging directory and `Executor` places the results, so the journal stays accurate and the output stays reversible.

### Phase 2 — System definitions + DAT infrastructure
Coupled, because a system definition includes which DAT sources cover it.

**System definitions.** v1's `SystemProfiles` is a PlayStation-only registry that silently falls back to `psx` on an unknown code. Replace it: code, display name, aliases, file extensions, archive expectations, format qualifiers (`Headered`, `BigEndian`, `Decrypted`, `NKit RVZ`), and DAT sources. Unknown codes report as unrecognized — never substituted.

**DAT infrastructure.** Parse, index, cache, query by name and by hash.

**No-Intro is import-only, not scraped.** Datomatic requires a session-cookie GET/POST form flow ending in a "Prepare" step; the established tool for it (`datoso-seed-nointro`) requires Firefox and geckodriver. Bundling a headless browser contradicts the portable single-EXE promise, and a silent-breaking scraper is worse than none. Datomatic offers a "Daily" full pack — so:

- `ark dat import <path>` — user-supplied DAT or Daily pack. **Primary path for No-Intro.**
- `ark dat sync` — automated, for sources with direct URLs. Redump's 77 entries already qualify.

Medical Bay reports catalog coverage so gaps are visible rather than silent.

> **Gate:** `ark dat import` ingests a Daily pack and indexes it. `ark dat sync` fetches a Redump source; second run hits cache. Catalog queryable by name and by hash. Unknown system code reports unrecognized rather than falling back.

**First live network run (Phase 10 hand review):** 79 sources → **52 fetched, 4 cached, 23 failed**, every failure carrying a reason. Two things only a real run could show:

- **A partial-failure guarantee is only as good as its exception list.** The catch filter named `HttpRequestException`, `IOException`, `FormatException`, `InvalidDataException`, `TaskCanceledException` — and Redump's HTML error page threw `XmlException`, which is none of them. The doc comment promised "a failure on one source does not abort the rest"; the run died on source 4 with a stack trace. The filter is now unfiltered, because the contract *is* "never abort", and any enumerated list can only cover the failures already seen.
- **21 of 79 Redump systems publish no DAT** and serve their ordinary HTML page. That is not an error to fix, it is the catalog's actual shape — detected before parsing and reported as "the source most likely publishes no DAT for this system", because `Reference to undeclared entity 'bull'` describes nothing a user can act on.

**Known gap:** 2 sources (GameCube BIOS, PS2 BIOS) serve raw **clrmamepro** text rather than zipped Logiqx XML. Valid DATs in a format the parser does not read; they fail cleanly and are excluded from coverage. A clrmamepro reader is unbuilt.

### Phase 2.1 — one system, N DAT variants keyed by qualifier

A DAT name carrying a format qualifier resolves to **(system, qualifier)**, not to a system alone. `(Headered)` and `(Headerless)` are two distinct hash sets over the same ~4,500 games; a headered ROM will never match a headerless DAT, and N64 splits the same way across byte orders.

Resolution is exact alias first; failing that, strip a **single trailing parenthetical** and require both that the base matches an alias **and** that the parenthetical is declared in that system's `formatQualifiers`. Anything else is unrecognized.

The negatives carry the weight. `Nintendo - Nintendo 64 (Mario no Photopi SmartMedia)` has a matching base but names a *subset, not a format* — admitting it would file a handful of SmartMedia dumps as the N64 library. An alias list is the wrong fix for the same reason: it would make qualified names resolve and then silently merge incompatible hash sets.

**This binds Phase 5.** Hash verification must target the variant the ROM actually is. Catalog lookups take `(system, qualifier)` and a null qualifier is matched exactly, never as "any" — the unqualified DAT is its own third set.

Real catalog, after the fix: `nes (Headered)` 4,505 · `nes (Headerless)` 4,509 · `n64 (BigEndian)` 1,157 · `n64 (ByteSwapped)` 1,157 · `snes` 4,128.

### Phase 3 — Naming (parser + formatter)
`Core/Naming/`: `TokenVocabulary`, `NameTokenizer`, `NameFormatter`, `ParsedName`. Vocabulary per `ARK-FILENAME-VOCABULARY.md`.

Pure string work, zero I/O — the fastest feedback loop in the project. Corpus in two halves:
- **Clean** — harvested from Phase 2's No-Intro DATs. Every name in a DAT is canonical by definition and covers releases nobody owns.
- **Dirty** — synthesized by corrupting clean names: stacked tags, scrambled order, GoodTools brackets, `Rev A` vs `Rev 1`, inverted articles, copier-header artifacts.

> **Gate:** `parse(format(parse(x))) == parse(x)` across the entire corpus. Language tags never classify as regions. Unknown bucket never silently empties. 100% on both halves.

### Naming: matching keys are token sets, not strings

Canonical token order is a **partial** order, derived from the precedence graph observed in real data. Categories with unambiguous evidence are ordered; categories whose observed order conflicts are mutually unorderable and preserve input order. This is what makes `format(parse(x)) == x` hold on clean No-Intro names — an invented total order would rewrite names that were already correct.

**Consequence:** two names carrying identical tokens in different orders are *both* canonical. A scrambled name normalizes to something that may not equal the DAT string byte-for-byte despite being the same release.

Therefore all grouping, matching, and comparison operate on **`ParsedName` token sets**, never on formatted strings. This binds Phase 5 name matching, Phase 7 variant grouping, and Phase 8.5 target-set comparison. String equality is a valid fast path only when both sides originate from the same DAT.

### Phase 4 — Scan + game unit resolution
`Core/Units/`: `IGameUnitResolver`, `GameUnit`. `CartridgeUnitResolver` returns one unit per archive. Disc resolver interface defined, implementation stubbed.

Also lands **`ark parse "<filename>"`** — carried over from Phase 3. Prints title, each classified token, the unknown bucket, the canonical reassembly, and the token-set match key. Exits non-zero on a flagged name. This is the debugging surface for every phase after naming.

**Scan classifies directories, not files.** Real drives mix ROM sets with emulators, tools, firmware, cheats, and unrelated projects. Folder names are no help — the reference drive uses `No-Intro`, `SNES Roms`, `Minerva_Myrient`, `Nintendo - Game Boy Advance`, and `PS2 Downloads` for the same kind of thing.

Two signals, both required:

| Signal | Threshold |
|---|---|
| Extension homogeneity — share of files with the most common extension | ≥ 90% |
| Naming conformance — share of files containing a recognized region token | ≥ 80% |

Validated against a 22,050-file mixed-use drive: **17 ROM-set directories / 10,045 files** identified, 140 directories / 11,050 files correctly excluded.

> **Validated against the real drive listing** (`tests/ARK.Tests/corpus/reference-drive.txt`, 22,050 real paths across 622 directories). The full reconciliation, at `MinimumFileCount = 5`:
>
> | | Directories | Files |
> |---|---|---|
> | ROM sets | 17 | 10,045 |
> | Excluded, enough files to profile | 179 | 11,283 |
> | Excluded, below the minimum file count | 426 | 722 |
> | **Total** | **622** | **22,050** |
>
> The earlier "140 directories / 11,050 files" figure was a partial count — it omitted the 426 directories too small to profile, which is where the missing 955 files were. The classifier reproduces this split exactly.

Both signals are necessary. Cheat directories (`.cht`, `.ps3savepatch`) score 100% homogeneity and 0% conformance — homogeneity alone swallows them. `PSP\Roms` is 90 bare `.iso` files with no archive — conformance catches it where an extension allowlist would not.

Directory profiling reads listings only. No file is opened to decide whether a directory is a ROM set.

**Three output buckets, never two:**
1. **Identified** — DAT hash or name match
2. **Candidate** — in a ROM-set directory, no DAT match. Reported, never acted on.
3. **Excluded** — with a stated reason, visible in the report. Active-download directories are excluded from write operations here, before any unit reaches an operation.

Exclusion rules live in editable config, not in code. One user's junk is not the next user's junk.

**Known gap:** conformant naming does not prove game content. `Nintendo - Wii U - Disc Keys` is 516 flawlessly No-Intro-named archives containing disc keys, not games. Needs a third check on folder qualifier or archive contents.

Built now, while a cartridge unit looks trivial, so disc support later is one new resolver instead of a downstream rewrite.

> **Gate:** No code outside resolvers touches `FileInfo`. `ark scan <root>` on a mixed drive reports the three buckets with reasons. Directory classifier reproduces the 17/140 split on the reference corpus.

### Phase 4.1 — identification is scoped to one DAT

**A ROM-set directory is identified against exactly one DAT, never the whole catalog.** Resolution: the directory name equals an indexed DAT name (these are byte-identical on real collections — `GB\Nintendo - Game Boy` against the DAT `Nintendo - Game Boy`); failing that, the directory resolves to a system + qualifier that has a DAT indexed; failing that, **nothing is identified**. There is no catalog-wide fallback.

A catalog-wide search is a Prohibition 6 violation with a confident label on it. Against 1.5M entries across 334 DATs, 308 Redump PlayStation images matched `Non-Redump - Sony - PlayStation` and `Sony - PlayStation (PS one Classics) (PSN)` purely on shared titles — different artifacts, different hashes. Phase 5 would then hash each against the wrong entry, fail every one, and report **Mismatched**: false corruption on healthy files, which is precisely the alarm fatigue the five-state model exists to prevent.

**This is not a downgrade.** Game Boy identifies correctly with `gb` undefined as a system, while PlayStation becomes honest — no Redump DAT imported means no identification. The scan names the DAT each directory was compared against, or states that none was.

### Unsupported format is not an anomaly

| Class | Meaning |
|---|---|
| **Unsupported format** | Correct structure, no resolver for it yet — a `.cue` plus its `.bin` tracks while the disc resolver is stubbed |
| **Anomaly** | Genuinely malformed — unreadable archive, or two unrelated games in one archive |

One track sheet plus data files is one disc image. Several track sheets, or none, means genuinely more than one thing in the archive. Unsupported-format units are **summarized by count, never listed per file**: 1,762 of 1,765 PlayStation archives on the reference drive are `.bin` + `.cue`, and listing them buried the 2 real corrupt downloads underneath. An anomaly list that is 99.8% normal files is a list nobody reads.

### Phase 5 — Hashing + verification
Tiered hashing for **deduplication**: group by size (free) → CRC32 survivors → SHA1 only on CRC collisions. Unique sizes are never hashed. SQLite cache keyed path + size + mtime, per instance.

**Verification is a separate concern and always hashes.** Size and presence prove nothing. Incomplete torrent transfers pre-allocate, and pieces span file boundaries — so a deselected file adjacent to a selected one receives partial data and ends up with the correct name, the correct size, and the wrong contents. Every cheap check passes it.

`ark verify` sorts a set into five states:

| State | Meaning |
|---|---|
| **Verified** | Hash matches a DAT entry |
| **In Progress** | Actively being written, or inside a declared incomplete-download directory. **Not judged.** |
| **Mismatched** | Name matches a DAT entry, hash does not — incomplete, corrupt, or a different dump |
| **Unrecognized** | No match by hash or by name |
| **Excluded** | Not ROM content |

**In Progress is distinct from Mismatched and the distinction is not cosmetic.** A file mid-download fails hash verification, which is technically true and uselessly reported. Sixty "corrupt" files that are merely still downloading is the alarm-fatigue failure — a user who sees one false corruption report stops reading the report.

The dangerous case is not the obviously-partial torrent. It is the one stalled at 99.9%: it looks finished, it will sit that way indefinitely, and the few incomplete files are indistinguishable from complete ones by name and size.

Detection, cheapest first:

1. Incomplete-file extensions — `.!qB`, `.part`, `.!ut`, `.bc!`, `.crdownload`, `.aria2`. Free, but only present if the client is configured to append them.
2. Active write — mtime within a recent window, or the file refuses an exclusive open (a sharing violation means something owns the handle now).
3. Declared incomplete-download directory, from settings.
4. Size mismatch against the DAT — defeated by pre-allocation, still worth checking.
5. Hash — the only definitive answer.

**Write operations refuse, not warn.** A directory showing active-download signals is excluded from renaming, moving, and quarantine by default. Reports still run: coverage, missing, mismatched all remain available. Nothing is modified until the set has settled.

**Renaming files in an active torrent breaks the seed.** The client loses its paths, reports the files missing, and stops seeding — silent damage, and real damage to anyone maintaining ratio on a private tracker. This applies to *completed* torrents still seeding, not only to downloads in flight, so completeness is not sufficient grounds to write.

**Documented workflow: seed from the download directory, build the organized collection elsewhere.** ARK reads torrent output and writes to a separate root, leaving the seed intact.

### Unselected-file spillover

Torrent pieces span file boundaries, so files the user explicitly marked *do not download* still accumulate partial data at their edges. Observed on a real 490 GB GameCube set: unwanted entries sitting at 5.8%, 28.3%, 53.0%, 57.7% — and some at **100%**, complete and valid despite never being requested.

This splits into two categories needing opposite responses:

| Case | Reality | Correct action |
|---|---|---|
| Unwanted, 100% | Genuine complete file, hash-verifies | Region/variant **policy** decides — not a verification concern |
| Unwanted, partial | Fragment of something never requested | Delete — no re-acquisition wanted |
| Wanted, partial | Incomplete download of a desired title | Re-acquire |

**ARK cannot separate rows 2 and 3 unaided.** Both present identically: name matches a DAT entry, hash does not. Without knowing the user's per-file priorities, an unwanted fragment and a corrupt download are the same observation.

### Optional torrent-client integration

qBittorrent's Web API exposes per-file progress *and* per-file priority on localhost. Where available, it:

- Distinguishes *unwanted fragment* from *genuinely corrupt* — impossible by hashing alone
- Replaces a multi-hour hash pass over a large set with an instant lookup
- Identifies in-flight files definitively, no heuristics

**Strictly optional.** Hash verification remains the universal path and the only requirement. No client integration is ever a dependency, and absence of it degrades cleanly to the hash path.

**Cheap detection layers demonstrably fail here.** In the observed case there was no `.!qB` extension (the client was not configured to append one) and the file was pre-allocated to full size with an ordinary timestamp — a 0.99 GB file missing 16 MB that passes both the extension check and the size check. Treat extension and size as optimizations that reduce hashing, never as sufficient evidence of completeness.

The Phase 5 hash cache keys on path + size + mtime, so a hash taken mid-download self-invalidates on the next write. That safety falls out of the existing cache design and needs no additional work.

Mismatched is **diagnosable, not unknown**, and must be reported as such. Detection is layered cheapest-first: client extensions (`.part`, `.!ut`, `.!qB`, `.crdownload`), zero-byte files, size mismatch against the DAT, then hash for whatever survives.

**Verification gates renaming.** Only Verified units receive canonical names. Renaming a corrupt file to its canonical name produces a file that looks verified and will never be questioned again — worse than v1's damage, which at least announced itself in the filename.

Nothing is deleted. Mismatches are reported; quarantine is offered and never automatic. The report is exportable in a form usable for re-queuing specific titles in a torrent client, so recovery is targeted rather than a full re-download.

> **Gate:** Second run over an unchanged set performs approximately zero hashing. A file with correct name and size but corrupted contents reports Mismatched, not Verified and not Unrecognized. A rename operation refuses a Mismatched unit. A file with an incomplete-download extension, or one inside a declared incomplete directory, reports In Progress rather than Mismatched. Write operations refuse a directory showing active-download signals while still producing reports for it.

### Phase 6 — `ark undo`
The journal already exists from Phase 0. This adds inversion and the verb.

**Nothing persists an enum by its ordinal.** The hash cache did, and deleting one member silently relabelled 592 cached rows. In the journal the same mistake is far worse: `ActionKind` shifting by one makes undo replay a session as the wrong operations — a `Move` read as a `Quarantine` — turning the safety net into the hazard with no signal until the damage is visible. Every enum is written by **name** through the single `ArkJson` serializer, an unrecognized name decodes to an explicit `Unknown`, and anything carrying `Unknown` is refused by undo rather than guessed. An architecture test enforces that no other type builds its own `JsonSerializerOptions`.

**Undo verifies before it acts**, against a projection of the filesystem as each earlier inverse leaves it — not against the world as it stands when the run starts. A precondition failure stops the run, names the action, and changes nothing further. Restoring over a file that has appeared since, or over one edited since, is refused without an explicit per-run `--force`.

`WriteText` is invertible because the `Executor` captures what it displaced at the moment of writing. A null prior content means the file did not exist, so the faithful inverse removes it.

> **Gate:** A synthetic session of moves, renames, and quarantines reverses to the exact starting state. Survives process restart and partial failure.

### Phase 7 — Deduplication
Hash-identical `GameUnit`s only. This is **not** variant handling — different revisions have different hashes and are not duplicates.

`ark dedupe <root>` ships **report-only and DRY-RUN**: a first run with no flags decides nothing and moves nothing, twice over. A `--policy` chooses what to keep; `--apply` carries it out.

**A tie is reported, never resolved.** When the policy cannot separate two copies the group is listed and skipped — a coin flip presented as a decision is Prohibition 6 with a confident label on it.

**Verification state gates participation.** Verified and Unrecognized are eligible; **In Progress is excluded outright** (its hash describes bytes about to change); Mismatched is reported and never collapsed, because two identical corrupt files are two corrupt files and collapsing them hides the second.

Quarantine goes to `<root>/.ark-quarantine/<session-id>/`, keeping each unit's path relative to the root — same volume by design, since a cross-volume move is a copy. Every directory level is journaled separately: `CreateDirectory` builds a whole chain in one call, and journaling only the leaf would leave undo unable to remove the levels above it.

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

**Curation is multi-axis, not a single ranking.** There is no one "best" copy: preferences are independent per axis, and collapsing them into one ranking produces silent, wrong deletions. Each axis is Identity (differing means a *different game*, never compared), Variance (differing means variants of one game, ranked), or Ignored. **Licensing and development status are separate axes** — "keep retail only" must not touch 390 unlicensed titles. **Hardware flags are on no axis at all**; they describe cartridge capability, not release lineage.

Grouping is `ParsedName` token sets plus the identity axes, so the same collection curates differently depending on which axes identify — and the report states which reading produced its decisions. Non-game content never enters a group.

**Every removal here is a real loss of a real release.** Dedup could argue a removed file was recoverable from its byte-identical twin; nothing here can. Presets are per-axis rules with none special-cased, report-only ships as the default, and a policy that cannot order a group reports and skips it whole.

**Quarantine is removal; sort-into-subfolders is organization.** Different risk profiles, never conflated in the report. Both journaled, both reversible, both DRY-RUN by default.

ARK's own quarantine lands inside the tree it curated, so `.ark-quarantine` is excluded from scanning — matched against **every path segment**, not just the leaf, or the quarantined sets nested below it would re-enter the pipeline as live candidates.

### Phase 8.5 — Collection reports
The join of catalog (Phase 2), scan (Phase 4), verification (Phase 5), and policy (Phase 8). No new subsystem — this is what the pipeline was built to produce, and it is the headline user-facing feature.

Three reports, pivotable by system, by region, or both:

| Report | Definition |
|---|---|
| **Missing** | In the declared target set, absent from disk |
| **Unrecognized** | On disk, absent from every DAT — bad dumps, hacks, foreign sources |
| **Upgradable** | Present on disk, but a higher revision exists in the DAT |

**Completeness is measured against a declared target, never against the whole DAT.** A full No-Intro DAT carries every region, revision, proto, beta, sample, unlicensed and aftermarket release. Comparing a USA collection against all of it reports tens of thousands missing — true and useless. The target set comes from the Phase 8 policy (*USA retail, latest revision, no pre-release* is a different question from *everything tracked*). This is the 1G1R concept, and it is why this phase depends on the policy engine.

**The tokenizer runs on DAT entry names here, not only on filenames.** Region, revision, and dev-status filtering all require parsing catalog entries. DAT names are the clean canonical case Phase 3 handles best.

Output must be exportable in a form usable for re-acquisition — the missing list is a work queue, not a wall of text.

> **Gate:** Missing/unrecognized/upgradable computed against a declared target set. Pivots by system and by region. Changing the policy changes the missing count and nothing else. Export is machine-readable.

**The key inversion: the policy runs over the catalog.** Phase 8 runs it over *your files* to decide what to remove; this runs the identical policy over *the DAT* to decide what you should have. Same grouping, same ranking, same axes, same refusals — different input. `VariantEngine` is the one implementation both call, so curation and the reports can never describe different collections.

**Four states, not three.** Present · Missing · **Damaged** · Unrecognized. Damaged is a Mismatched file for a title *inside* the target set — a gap you can close, and you already know which title. The same file outside the target set is Unrecognized and must not inflate the missing count.

**Region matching is set intersection.** `(USA, Europe)` satisfies a USA target — it *is* that release. `(World)` satisfies every region target and is never collapsed into a region list.

Catalog tokenization is memoized per run and scoped to the DATs the scan actually resolved to; the join is indexed on the token-set match key, never O(n·m).

### Phase 9 — Rename + Organize
What the naming subsystem was built for, and what v1 destroyed collections doing.

**Two modes, never confused.** *Canonicalize* takes the name from the DAT entry the unit's hash confirmed — the default, and the only mode requiring no flag. *Normalize* reformats the unit's own existing name to repair v1-style damage; it is an explicit opt-in, reported separately, and claims only that the result is well-formed, never that it is correct.

**ARK never derives a canonical name from a filename.** The tokenizer exists to *understand* names, never to *authorize* them. No DAT match means no known canonical name: refused and reported, never guessed. This is the single rule separating this phase from v1.

**Verification gates renaming.** Only Verified units are canonicalized. A corrupt file wearing its canonical name looks verified forever after — worse than v1's damage, which at least announced itself in the filename.

**Check before write.** A unit already carrying its canonical name is skipped with no filesystem write, so a conformant set is a no-op. The comparison is *ordinal*: `game (usa).zip` → `Game (USA).zip` is a real correction.

**The planner projects the filesystem across the whole batch** before planning any move. Two units canonicalizing to one name are refused and reported — never overwritten, never suffixed, because a disambiguating suffix is a fabricated name. Swaps and cycles stage through temporary names; so do case-only renames, which `File.Move` handles unreliably on a case-insensitive filesystem.

**Archives are renamed, never rewritten.** The entry inside keeps its name. Rewriting means recompressing — slow, changes archive bytes, and touches ROM data for a cosmetic gain that DAT verification does not care about.

*Organize* moves units into a directory structure (default: the DAT name). Distinct from rename, separately reported, and neither is a removal.

> **Gate:** Only Verified units canonicalized. No DAT match is refused, never invented. Already-correct names are skipped without a write. Collisions refused; swaps and cycles complete without loss. `ark undo` restores the set byte-for-byte.

### Phase 10 — Disc units
Where v1 died: multi-track read as multi-disc, multi-disc read as variants, CUE sheets rewritten from assumptions. Most of the reference drive still sits outside ARK because of it — 1,762 PSX, 659 GameCube, 140 PS2, 172 PSP, 62 PS3.

**The three "multi" cases share no code path.** Conflating any two is what produced the damage.

| Case | Structure | Unit shape |
|---|---|---|
| Multi-track | One game, one disc, audio split across BINs | **One unit** |
| Multi-disc | One game, N discs | **N units**, grouped into a set |
| Single-file image | One BIN or one ISO | **One unit** |

A CUE with twelve TRACK entries is one disc. Track count says nothing about disc count.

**The CUE is the manifest.** Membership comes from parsing it, never from filename similarity. A CUE naming a file that is not present is an *incomplete unit* — reported, never partially assembled.

**No CUE is ever written.** Redump DATs hash every constituent file including the `.cue`, so a CUE matching its DAT hash is provably correct and must not be touched. One that does not match is reported, not repaired — regenerating it would rewrite the file describing where the game's data lives.

**A multi-disc set moves whole or not at all.** Quarantining Disc 2 of a three-disc set leaves a broken game and a user who does not know it. A set verifies only when every disc in it does.

**A bare `.bin` is not claimed as a disc.** A BIN named by a CUE is claimed *through* that CUE, which is the only membership evidence worth trusting. One with no CUE anywhere is far likelier a Mega Drive ROM than an orphaned track, and claiming the extension would file the entire Genesis library as discs. The single-file disc shapes are `.iso` and `.img`.

**Resolver order is disc first.** The cartridge resolver claims every file it is offered, so it must run last: a disc has positive evidence — a CUE, an ISO — and a cartridge is the residue.

**A loose multi-file disc unit cannot be renamed.** Renaming the tracks invalidates the `FILE` lines naming them, and the only way to keep the unit coherent is to rewrite the CUE. That is forbidden, so the unit is refused whole rather than renaming the sheet and orphaning what it points at. Archived disc images are unaffected — the archive is renamed and the entries inside keep their names.

> **Gate:** CUE + BINs resolve to one unit. `(Disc 1)` and `(Disc 2)` are two units in one set, never merged. A CUE referencing a missing file is incomplete, never partially resolved. No CUE is written. Sets are atomic.
>
> **Validated against the real drive.** `F:\PSX` — 1,767 files, 1,765 archives, the set v1 destroyed:
>
> | | Before | After |
> |---|---|---|
> | Disc units | 0 | **1,762** |
> | Unsupported format | 1,762 | **0** |
> | Anomalies | 2 corrupt archives | 2 corrupt archives |
>
> Multi-disc grouping reproduces the drive exactly: 199 disc-numbered archives → **91 groups, 75 genuine multi-disc sets**, 16 lone discs whose siblings are absent. Largest set is *Riven* at five discs, held as five units.

**Found on the real drive, not predicted:** one archive in 1,765 (*MediEvil (USA) (Demo 2)*) holds a lone `.bin` and **no CUE**. Its Redump game declares both a `.bin` and a `.cue`, so a single-file unit judged against whichever entry indexed first would report corruption on a healthy file — the exact false alarm the five-state model exists to prevent. Catalog lookups now return the whole bucket and the entry is selected by extension.

**The hash cache extends to constituents**, keyed `<archive path>|<entry name>` and invalidated by the containing file's own size and mtime. Without it Phase 5's "a second pass hashes approximately nothing" would be false for precisely the collections where it matters — a 490 GB set would re-hash in full on every run.

**End-to-end on real Redump data.** *Final Fantasy VII (USA)* Discs 1–3 plus two single-disc titles, against the synced `Sony - PlayStation` DAT (60,168 entries): 5 identified → 5 disc units → **5 Verified**, 25 constituents hashed, 3.1 GB. Second pass: 0 hashed, 25 cached. One byte flipped at offset 400,000,000 of the 531 MB Disc 2 → **Mismatched**, only that unit's 2 constituents re-hashed, the set reported *"1 of 3 discs mismatched"*, and `rename` refused it while calling the other four already-correct with no write.

**The named hazard, refused on real data.** *Fear Effect 2 - Retro Helix (USA)* ships as two revisions × four discs. Under `latest-revision`, disc number being identity yields four groups of two, and all four non-Rev discs are removed — a whole set, allowed, applied, and `ark undo` restored 4.1 GB byte-for-byte. Remove one Rev 1 disc from the directory and the same policy wants three of the four non-Rev discs, which would leave a lone Disc 4 of an unplayable game: **all three refused under `--apply`, nothing moved.** This is the failure Prohibition 4 was written for, exercised against the collection v1 destroyed.

### Phase 11 — GUI (Avalonia)
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
| Bare `.bin` | Mega Drive ROM or orphaned disc track — indistinguishable by extension. Claimed only through a CUE that names it. |
| Multi-entry DAT games | A Redump game is several ROM entries under one name. Taking the first compares a BIN against a CUE's hash. |
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
