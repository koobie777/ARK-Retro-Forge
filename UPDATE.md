# Update Notes

This file contains release notes for ARK-Retro-Forge releases.

## v0.2.0-rc.3 (2025-11-16)

### CLI
- Menu header now shows the running RC version, uses ASCII prompts, and refuses to open when there is no interactive console to avoid Spectre exceptions.
- Archive extraction reuses the saved ROM root, clears the planning log before applying, and shows a progress bar with per-archive status plus ESC/Ctrl+C cancellation.

### Packaging
- Windows build outputs `ark-retro-forge.exe` (and workflows/checksums were updated) so RC artifacts match the CLI shim name.

## v0.1.0-preview.12 (2025-11-16)

### CLI Ergonomics
- Added a Windows `ark-retro-forge.cmd` shim that shells out to the CLI project so the documented `ark-retro-forge <verb>` commands work from the repo root without extra arguments
- Allow switching between Debug/Release by setting `ARKRF_CONFIGURATION` before running the shim and documented the workflow in README
- Added an interactive CLI menu (run `ark-retro-forge` with no args) that surfaces doctor/scan/verify/rename/convert/merge/extract flows without memorizing verbs
- Menu now renders with Spectre.Console panels/prompts, exposes a persistent DRY-RUN/APPLY toggle, remembers the ROM root, and now supports named instance profiles (via menu or `--instance`) so multiple conversions/scans can run concurrently with isolated caches/logs.
- Release automation now includes a dedicated `Release Candidate` workflow invoked by pushing tags like `vX.Y.Z-rc.1`, creating prereleases alongside the stable release workflow.

### PSX Tooling
- `convert psx` now understands `--to chd|bin|iso`, automatically picks `createcd` vs `createdvd`, converts CHD back to BIN/CUE or ISO, and respects `--delete-source` across every direction without relying on rename metadata.
- New `merge psx` command: detects multi-track BIN layouts via CUE sheets, merges them into a single BIN, rewrites a clean CUE referencing title-only filenames, and optionally prunes the original files after an explicit confirmation prompt
- Dedicated planner/service pipeline (`PsxBinMergePlanner`/`PsxBinMergeService`) plus unit coverage to guarantee deterministic BIN concatenation and cue timings

### Archive Utilities
- New `extract archives` verb handles ZIP/7Z/RAR imports through SharpCompress, supports recursive discovery, custom destinations, and optional archive deletion for large batches
- Added automated tests to ensure ZIP extraction works end-to-end and prevent regressions
- Archive extractor now wipes/recreates destination directories before unpacking so re-running the command overwrites stale files reliably and ESC/Ctrl+C cancels only the in-flight archive with automatic rollback using a faster streaming path.

### ROM Cache
- Scan and verify now populate a per-instance SQLite ROM cache capturing size, hashes, titles, and regions to cross-reference future scrapers and speed up downstream tooling.

## v0.1.0-preview.8 (2025-11-14)

### PSX Toolchain Enhancements - Verification and Documentation

#### Separate SKU Handling
- **Verified playlist exclusion**: C&C variants (GDI/NOD), Red Alert (Allies/Soviet), Retaliation, and Shockwave Assault episodes are correctly excluded from playlist generation
- **Title-based grouping**: Playlist planner groups by exact title match, preventing single-disc SKUs from creating playlists
- **Comprehensive tests**: Added test suite for separate SKU scenarios to prevent regressions

#### Spacing and Formatting
- **Verified spacing**: Confirmed no double-space issues in formatted filenames (e.g., "16 Tales 1 (USA)" formats correctly)
- **Title trimming**: PsxNameFormatter properly trims titles, preventing spacing artifacts
- **Added tests**: Lightspan title formatting tests ensure no regressions

#### CHD Media Type Support
- **New ChdMediaType enum**: CD, DVD, and Unknown media types for future-proofing
- **ChdMediaTypeHelper class**: Determines appropriate chdman command based on extension and system context
- **PSX defaults to CD**: All PSX conversions use `createcd` command with clear extension points for PS2/PSP DVD support
- **Test coverage**: Full test suite for media type detection and command generation

### Technical Details
- New `ChdMediaType` enum and `ChdMediaTypeHelper` class in ARK.Core
- Enhanced `ConvertPsxCommand` to use media type detection
- Added `PsxSeparateSkuTests` test class for C&C/Shockwave scenarios
- Added spacing tests to `PsxNameFormatterTests`
- Added `ChdMediaTypeTests` for media type helper validation

## v0.1.0-preview.7 (2025-11-14)

### PSX Toolchain Enhancements

#### Multi-Disc Support
- **Disc suffix enforcement**: All image types (BIN/CUE/CHD) now retain `(Disc N)` suffix for multi-disc titles
- **Improved CHD naming**: Convert planner generates canonical CHD filenames matching rename conventions
- **CHD skip logic**: Existing CHDs are detected and skipped during conversion (use `--rebuild` to force reconversion)

#### .m3u Playlist Management
- **Automatic playlist creation**: Multi-disc titles now get `.m3u` playlists (e.g., `Final Fantasy VIII (USA).m3u`)
- **Playlist updates**: Playlists are updated when filenames change or format switches (BIN/CUE → CHD)
- **Smart file selection**: Prefers CHD > CUE > BIN for playlist entries
- **Backup on update**: Creates `.bak` backup when updating existing playlists
- **Configurable behavior**: Use `--playlists create|update|off` on rename, `--playlist-mode chd|bin|off` on convert

#### Multi-Track Awareness
- **Track detection**: Identifies multi-track disc layouts (e.g., `(Track 02)`, `(Track 03)`)
- **Audio track filtering**: Audio tracks (Track 02+) excluded from playlists and skip unnecessary processing
- **CUE-based conversion**: Convert planner operates at disc-level for multi-track games, avoiding per-BIN processing

#### Duplicate Detection
- **New command**: `duplicates psx` (or `dupes psx`) scans for duplicate disc images
- **Hash-based detection**: Uses SHA1 (or MD5) to identify identical files
- **Multi-disc aware**: Groups duplicates by title, serial, and disc number
- **JSON reports**: Use `--json` to write detailed reports to `logs/` directory
- **Summary statistics**: Shows duplicate groups, wasted space, and files that could be removed

#### CLI Improvements
- **Playlist plan tables**: Shows planned .m3u operations with title, region, operation type, and disc count
- **Better status messages**: "Already converted" instead of "EXISTS" for clarity
- **Extended help**: Updated help text documents all new flags and commands
- **Disc column accuracy**: Properly displays `Disc 1`, `Disc 2`, etc. or `Unknown` when appropriate

### Technical Details
- New `PsxPlaylistPlanner` class for .m3u creation and updates
- New `PsxDuplicateDetector` class for hash-based duplicate detection
- Enhanced `PsxDiscInfo` with multi-track properties (TrackNumber, IsAudioTrack, CueFilePath)
- Updated `PsxNameParser` to detect `(Track N)` patterns
- Updated `PsxConvertPlanner` with rebuild flag and CHD existence checking
- Comprehensive test coverage for multi-track detection, playlist planning, and duplicate detection

### Breaking Changes
- None - all changes are additive and backward-compatible

### Known Limitations
- Duplicate detection does not auto-delete files (manual review required)
- Serial extraction from disc images (DAT/probe) not yet implemented
- Multi-track BIN grouping relies on naming patterns and CUE file presence

## Upcoming Release

### Features
- Initial release of portable .NET 8 ROM toolkit
- Scan command for discovering ROM files
- Verify command with streaming hash support (CRC32, MD5, SHA1)
- Rename command with deterministic preview
- CHD/CSO/RVZ conversion support
- Doctor command to report missing external tools
- Plugin system for extensibility
- Emulator launch functionality with JSON templates
- SQLite database for caching and metadata
- Serilog logging with rolling files

### CLI Features
- `scan` - Scan directories for ROM files
- `verify` - Verify ROM integrity with hash checking
- `rename` - Rename ROMs to standard format "Title (Region) [ID]"
- `chd|cso|rvz` - Convert disc images to compressed formats
- `combine` - Combine split ROM files
- `dat sync` - Synchronize with DAT files
- `doctor` - Check for missing external tools
- `launch` - Launch games in emulators

### Global Options
- `--dry-run` (default) - Preview changes without applying
- `--apply` - Apply changes to files
- `--force` - Force operation even with warnings
- `--workers N` - Number of parallel workers
- `--verbose` - Verbose output

### Technical Details
- Portable single-file executable (< 25 MB)
- No registry modifications required
- No administrator privileges required
- Deterministic builds
- SBOM included
- MIT License
