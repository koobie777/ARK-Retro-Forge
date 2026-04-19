# Changelog

All notable changes to ARK Retro Forge are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Parallel workers for convert psx** — `--workers N` flag (default 4, clamped 1–8) runs N chdman processes concurrently via `SemaphoreSlim` + `Task.WhenAll`
- **HDD detection warning** — warns when `workers > 4` on a `DriveType.Fixed` drive to protect throughput
- **Workers persisted in session state** — `ConvertPsxOptions.Workers` field in `SessionState`; interactive menu prompts and remembers value across sessions
- **Spectre markup audit** — all user-supplied strings (filenames, paths, error messages) escaped via `EscapeMarkup()` or `[[` `]]` pattern across all PSX command files
- `CLAUDE.md` v3.0 — constitutional standing orders for Commander (Claude Code)
- `CHANGELOG.md` — technical change history following Keep a Changelog
- `.ark/missions/` directory structure for mission lifecycle tracking
- Governance protocol — every mission completion triggers CHANGELOG, mission archive, and version bump
- Sacred Rules Checklist — 16-point pre-commit verification
- Five Pillars framework for architectural decision-making
- Prime Directive — "The ARK is meant for all"
- Expanded chain of command: Admiral / Captain / Commander

### Changed
- `ExecuteConversionsAsync` rewritten — sequential foreach replaced with parallel `SemaphoreSlim` worker pool; thread-safe counters via `Interlocked`; failures collected in `ConcurrentBag`
- Documentation governance now mandatory per Law 13
- Command structure formalized with rank-based responsibilities

### Fixed
- `[N/M]` progress label crash — bracket sequences escaped with `[[` `]]` in `ExecuteConversionsAsync`
- Unescaped path and error strings in `ConvertPsxCommand`, `CleanPsxCommand`, `CuePsxCommand`, `DuplicatesPsxCommand`, `Program.cs`

### Removed
- `AGENTS.md` — legacy file superseded by `CLAUDE.md` v3.0

---

## [1.1.1] - 2026-04-19

### Added
- **Pre-flight validation** for convert psx operations
  - Disk space check (2.5× source size estimate)
  - Source file readability check (locked files silently removed from queue)
  - chdman availability check via ToolManager
  - Destination writability check (write-probe pattern)
  - Runs in both DRY-RUN and APPLY modes
- **Parallel workers support** planned for convert (see ROADMAP)
- **Pre-flight panel** with bordered display showing all 4 check results
- **Completion summary** now includes elapsed time, data processed, avg speed (MB/s)
- **DRY-RUN plan panel** — bordered panel stating "No files were modified. This is a plan only."
- **Progress label enhancement** — shows `[N/M] filename` counter
- **Cache validation methods** in `RomRepository`:
  - `CountByRootAsync` — count cache entries for a given root path
  - `DeleteByRootPathAsync` — wipe cache entries for rescan
- **`--rescan` flag** on scan command to force full cache refresh
- **Tool walk-up logic** in `ToolManager` — finds repo root `tools/` from binary location
- **Cache-empty guards** in rename and merge commands (graceful exit with warning)

### Changed
- **Scan command is now pure directory discovery**
  - Removed PsxNameParser initialization and calls
  - Removed BIN/ISO header probing (was 512KB read per file)
  - Removed chdman extraction for CHD serial probing during scan
  - Removed title/region regex parsing
  - Scan now stores only: path, filename, extension, size, timestamp
  - Title, region, serial, disc number left null until rename runs
  - Performance improvement: 509-file CHD set now scans in seconds instead of hours
- **Convert is format conversion ONLY** — architectural law enforced
  - Removed `_parser` field and constructor parameter from `PsxConvertPlanner`
  - All three plan methods (`PlanChdToBinCue`, `PlanCueToChd`, `PlanChdToIso`) now static
  - `PsxConvertOperation` record no longer carries `DiscInfo`
  - Output filenames derive purely from source stem — no formatter involvement
  - `FindChdman()` removed from `ConvertPsxCommand` — delegates to `ToolManager`
- **All commands read from cache** — only scan writes to cache
  - `RenamePsxCommand` exits gracefully with warning if cache empty for root
  - `MergePsxCommand` exits gracefully with warning if cache empty for root
  - `CleanPsxCommand` non-blocking on empty cache (ingest skips silently)
- **Progress label bracket escaping** — `[[{i+1}/{total}]]` to prevent Spectre markup crash

### Fixed
- **P1: CHD→BIN conversion pipeline incomplete**
  - `PlanChdToBinCue` was creating empty `DiscInfo` stub with no CUE generation
  - Now populates full `DiscInfo` via parser for metadata (originally — later removed per Law 2)
  - Generated CUE content attached to operation record
  - Executor writes our CUE only when chdman produces nothing (guard with `!File.Exists`)
  - Multi-track audio games protected — chdman's CUE wins when present
- **P2: Region duplication in rename output**
  - Fixed in `PsxNameParser` at Step 5b — added region rescue pass
  - `(Region)(Disc N)` filenames now correctly extract region into `DiscInfo.Region`
  - `StripRepeatedSuffix` in formatter handles already-corrupted names (`(USA) (USA)`)
  - Idempotency verified — second rename pass produces zero changes
- **P3: CHD serial probing absent**
  - Added `TryFromChdProbeAsync` in `PsxSerialResolver`
  - Uses chdman `extractcd` to system temp with unique GUID stem
  - Probes three candidates: `stem.bin`, `stem (Track 1).bin`, `stem (Track 01).bin`
  - `finally` block cleans all temp files on success, failure, or cancellation
  - Graceful fallback to DAT lookup then filename parsing on any failure
- **P4: Multi-disc vs multi-track grouping edge case**
  - `BuildGroupKey` excludes extension — mixed CHD+BIN disc sets now group correctly
  - Added `ClusterIntoLogicalDiscs` three-pass algorithm:
    1. Cluster by explicit DiscNumber
    2. Cluster remaining items by Serial
    3. Remaining items as individual slots
  - Multi-track audio files never assigned disc numbers
- **Tool path walk-up logic** — `AppContext.BaseDirectory` trailing separator bug
  - `Path.TrimEndingDirectorySeparator(startFrom)` before walk-up loop
  - Depth 0 and 1 no longer checked the same directory
  - Walk-up now correctly reaches repo root `tools/` at depth 5
- **Spectre markup crash** on progress label
  - `[1/1999]` was being parsed as color/style tag
  - Changed to `[[{i+1}/{total}]]` — escaped brackets render as literals

### Security
- Pre-flight checks prevent partial batch operations that could leave library in inconsistent state
- Locked files silently removed from conversion queue — no mid-batch failures

---

## [1.1.0] - 2025-11-20

### Added
- **ArkStaging** core service — centralized file operations engine with rollback and dry-run safety
- **Merge Resilience** — fuzzy matching for "Track 1" vs "Track 01" mismatches in CUE sheets

### Fixed
- **Rename Safety** — CUE files no longer accidentally deleted on Windows due to case-insensitive path normalization
- **Playlist Logic** — single-disc games strictly excluded from playlist generation

---

## [1.0.9] - 2025-11-20

### Added
- **Smart Version Detection** — `PsxNameParser` correctly identifies `(Rev 1)` and `(v1.0)` tags as versions not regions
- **`--include-version` flag** on rename psx to append detected version to filename
- **`playlist psx` command** for independent playlist creation and updates
- **`--no-multi-disc` and `--no-multi-track` flags** to optionally disable grouping during rename

### Fixed
- Playlists now only generated for multi-disc titles (2+ discs)
- Cached ROMs retain Version and ContentType metadata across sessions
- Region detection strictly differentiates Region codes from Version tags

### Changed
- "Clean library" is now first option in PSX menu
- Interactive menu remembers rename options (Recursive, Version, Articles, Playlist) between runs

---

## [1.0.8] - 2025-11-20

### Changed
- **Cleaner Optimization** — clean psx skips staging for files already in correct location

### Fixed
- **Collision Safety** — cleaner falls back to staging only when direct move would collide
- **Orphan Rescue** — clean psx detects and rescues orphaned multi-track files with no CUE

---

## [1.0.7] - 2025-11-20

### Fixed
- **CUE Generation** — clean psx now tracks file movements during execution
- Generated CUE sheets written to correct destination folder with correct filename references
- `DirectoryNotFoundException` on CUE generation to cleaned-up source directories
- Final empty-directory cleanup runs even if errors occur during main execution loop

---

## [1.0.6] - 2025-11-20

### Fixed
- **Cleaner Stability** — fixed crash in clean psx when moving files already moved by previous operation
- `FileNotFoundException` during large batch operations eliminated

---

## [1.0.5] - 2025-11-20

### Added
- **Unified Cache Strategy** — rename psx and merge psx now use same RomRepository cache as cleaner
- **Robust Multi-Disc Detection** — all PSX planners use "Cache → Filename Fallback" strategy
- **Live progress bar** during clean psx file moves
- **Empty Directory Cleanup** — cleaner automatically removes empty source directories

### Changed
- clean psx organizes multi-disc sets into flat `Root/Title (Region)/` instead of nested subfolders
- clean psx clears plan summary before execution in interactive mode

### Fixed
- Folder names no longer end up as `Title (Region) (Region)` when region already in title

---

## [1.0.4] - 2025-11-19

### Added
- **Menu auto-pause** after actions so users can read output before screen clears

### Changed
- **Cleaner Optimization** — clean psx uses RomRepository cache to skip re-scanning indexed files
- **Smart Fallback** — PsxNameParser attempts to resolve serials from filenames against DAT index if probing fails

### Fixed
- Removed redundant "(ESC to return)" text from headers

---

## [1.0.3] - 2025-11-19

### Added
- **DAT Index serial-based reverse lookup** in `DatMetadataIndex`
- **Auto-Scan** — PSX menu automatically scans for games on entry

### Changed
- **Reversed Detection Priority** to Probe → DAT → Filename for more accurate identification
- Scanner inspects binary header first to find Serial, then DAT lookup, then filename parsing

---

## [1.0.2] - 2025-11-18

### Added
- **`--remove-duplicates` flag** on clean psx integrating hash-based duplicate detection

### Fixed
- merge psx no longer creates duplicate merged BIN/CUE files when re-running
- merge psx `--delete-source` now deletes source track BINs immediately after copying each track
- clean psx `--move-multitrack` creates containers inside `Title (Region)` subdirectory

---

## [1.0.1] - 2025-11-18

### Added
- **Spinning status indicator** during merge psx CUE file scan phase
- Total multi-track layout count reported before rendering merge table

### Changed
- Version bumped to v1.0.1 across CLI banner, README, AGENTS.md

---

## [1.0.0] - 2025-11-17

### Added
- **Stable release workflow** (`stable-release.yml`) triggered on `vX.Y.Z` tags from main
- **MinVer baseline** raised to 1.0 with `rc` as default pre-release identifier

### Changed
- `ark-retro-forge --version` now reports v1.0.0
- README quick-start flow updated for v1.0.0 stable build
- AGENTS.md release instructions reference `v1.0.0-rc.1` tagging pattern

---

## Pre-1.0 History

Full pre-1.0 development history (preview releases, RC iterations 1 through 12, initial PSX toolchain, DAT intelligence, Medical Bay introduction) is preserved in `UPDATE.md` under the archived release sections.

---

[Unreleased]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.9...v1.1.0
[1.0.9]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.8...v1.0.9
[1.0.8]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/koobie777/ARK-Retro-Forge/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/koobie777/ARK-Retro-Forge/releases/tag/v1.0.0
