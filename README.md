# ARK-Retro-Forge

> **ARK Protocol Field Manual** – Portable .NET 8 toolkit for scanning, verifying, converting, and organizing ROM payloads across Sony/Nintendo/SEGA ecosystems. Every verb defaults to **DRY-RUN** and ships as a single-file EXE with zero installers.

## Transmission Overview

- **Command Deck:** `ark-retro-forge` (no args) launches a Spectre.Console menu with live status, DRY-RUN/APPLY toggle, ROM-root memory, and per-instance routing.
- **Zero ROM Policy:** No ROMs, BIOS, keys, or third-party tools are bundled. You supply your own assets and utilities (chdman, maxcso, etc.).
- **Instance Profiles:** `--instance <name>` isolates databases, logs, and DAT caches under `./instances/<profile>/` for parallel RC/dev/stable workflows.
- **DAT Intelligence:** `dat sync` plus a built-in metadata index lets rename/merge/clean flows reason about serials, disc counts, and playlists even when filenames are noisy.

## Operations Manifest

| Operation | Purpose | Highlights |
|-----------|---------|------------|
| `medical-bay` | Environment readiness | Detects missing external tools, prints fixes, JSON export. |
| `scan` | Inventory ROMs | Recursive Spectre progress, per-extension stats, ROM cache hydration. |
| `verify` | Hash integrity | Streaming CRC32/MD5/SHA1 updates ROM cache; throughput metrics. |
| `rename psx` | Deterministic naming | `Title (Region) [Serial]` output, playlist planner integration. |
| `convert psx` | CHD/BIN/ISO pipelines | Bidirectional CHD↔BIN/ISO with media (CD/DVD) detection, delete-source/rebuild flags. |
| `merge psx` | Multi-track to single BIN | Validates CUE sheets, rewrites destination BIN/CUE, optional cleanup. |
| `clean psx` | Organizer & staging | Moves multi-track BINs into dedicated folders, generates missing CUEs, ingests external directories using ROM-cache + DAT intelligence, flattens stray single-disc folders. |
| `duplicates psx` | Hash-based dedupe | SHA1/MD5 detection, grouped reports, optional JSON to `logs/`. |
| `extract archives` | ZIP/7Z/RAR | Batch header, ESC cancel, optional delete-source after extraction. |
| `dat sync` | DAT catalog fetcher | Downloads Redump/No-Intro JSON-defined sources into instance-scoped `dat/` folders. |

## Mission Console (Interactive Menu)

1. **Medical Bay** – run this first to confirm toolchain alignment.  
2. **Scan / Verify** – each prompt offers recursive toggle, ROM-root confirmation, and brings Spectre dashboards online.  
3. **PSX Ops** – rename / convert / merge / clean / duplicates; menu prompts mirror CLI flags and respect global DRY-RUN state.  
4. **Extract / DAT Sync** – manage archives and DAT catalogs from the same console.

> Tip: DRY-RUN mode persists during the session. Switch to APPLY from the menu before executing destructive operations. Sessions reset back to DRY-RUN when restarted.

## Operation Details

### Medical Bay (Environment Diagnostics)
- Replaces the legacy `doctor` verb with richer Spectre output, a JSON export, and Serilog logging so hardware/tool mismatches are obvious.
- Runs before every RC release to confirm `chdman`, `maxcso`, `wit`, `ffmpeg`, etc. exist in `.\tools\`.
- Accessible via `ark-retro-forge medical-bay` or from the interactive menu.

### Scan
- Discovers supported extensions (`.bin/.cue/.iso/.chd/.pbp` plus Nintendo/Sega formats).
- Populates the per-instance SQLite ROM cache (`file_path`, `size`, `title`, `region`, `rom_id`, timestamps).
- Presents per-extension totals and file-size aggregation to highlight what was indexed.

```ps1
ark-retro-forge scan --root F:\ROMs --recursive
```

### Verify
- Streaming hashes per file; updates cached CRC32/MD5/SHA1 and throughput metrics.
- Designed for large libraries (cancellable with Ctrl+C). Works best after `scan`.

```ps1
ark-retro-forge verify --root F:\ROMs --recursive
```

### Rename (PSX)
- Applies the canonical `Title (Region) [Serial]` schema with disc numbering.
- Integrates playlist planner to build or update `.m3u` multi-disc playlists.
- Dry-runs by default; `--apply` commits changes.

```ps1
ark-retro-forge rename psx --root F:\PSX --recursive --apply
```

### Convert (PSX)
- Supports `--to chd|bin|iso` with CD/DVD auto-detection; leverages user-supplied `chdman`.
- `--delete-source` requires `--apply`. Use `--rebuild` to force reconversion even if CHDs exist.

```ps1
ark-retro-forge convert psx --root F:\PSX --to chd --recursive --apply --delete-source
```

### Merge (PSX)
- Uses `PsxBinMergePlanner` plus the DAT metadata index to identify multi-track BIN/CUE layouts, prevent multi-disc SKUs from merging incorrectly, and rewrites a single BIN with updated CUE references.
- Optional source deletion (post-merge) safeguarded by prompts/dry-run.

```ps1
ark-retro-forge merge psx --root F:\PSX --recursive --apply
```

### Clean (PSX Organizer)
- Corrals multi-track sets into `PSX MultiTrack/<Game>/` (configurable).
- Generates missing single-track CUE sheets, flattens stray single-disc directories back into the root, and ingests ROMs from a secondary location if the ROM cache/DAT catalog confirm legitimacy.
- Prompts to hydrate the ROM cache via `scan` when needed.

```ps1
ark-retro-forge clean psx --root F:\PSX --recursive --move-multitrack --generate-cues --flatten --ingest-root F:\Imports --apply
```

### Extract Archives
- Presents a clean header with root/output metadata, supports recursive scanning, optional delete-source, and ESC/Ctrl+C cancellation.

```ps1
ark-retro-forge extract archives --root C:\Downloads --output F:\Staging --recursive --apply --delete-source
```

### DAT Sync
- Reads `config/dat/dat-sources.json`, fetches each source into `instances/<profile>/dat/<system>/`, and skips downloads unless `--force` is provided.

```ps1
ark-retro-forge dat sync --system psx --force
```

## Quick Start Checklist

1. **Download Release:** grab the latest `ark-retro-forge.exe` (RC/stable).
2. **Provision Tools:** place `chdman.exe`, `maxcso.exe`, etc. inside `.\tools\`.
3. **Run `medical-bay`:** ensure all dependencies are detected.
4. **Set ROM Root:** via menu or CLI `--root`. Save it for future sessions.
5. **Scan + Verify:** hydrate the ROM cache and record hashes.
6. **Run Ops:** rename/convert/merge/clean/extract as needed (toggle APPLY to execute).

## CLI Reference

```ps1
ark-retro-forge medical-bay
ark-retro-forge scan --root D:\ROMs --recursive
ark-retro-forge verify --root D:\ROMs --recursive
ark-retro-forge rename psx --root D:\PSX --recursive --apply
ark-retro-forge convert psx --root D:\PSX --to chd --apply --delete-source
ark-retro-forge merge psx --root D:\PSX --recursive --apply
ark-retro-forge clean psx --root D:\PSX --ingest-root D:\Imports --apply
ark-retro-forge extract archives --root C:\Downloads --output D:\Incoming --recursive --apply
ark-retro-forge dat sync --system psx --force
```

Global options (available on every command):

- `--dry-run` (default), `--apply`, `--force`
- `--workers <N>` control parallelism where applicable
- `--instance <name>` isolates data stores
- `--report`, `--verbose`, `--theme` for additional diagnostics/preferences

## Development & Building

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish src/Cli/ARK.Cli.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true
```

Project layout:

```
src/
  Core/   - hashing, DAT catalog, serializers, database, planners
  Cli/    - Spectre.Console verbs + interactive menu
config/   - emulator templates, DAT source catalog
tools/    - user-supplied external executables
instances/<profile>/
  db/     - SQLite ROM cache
  dat/    - downloaded DAT archives
  logs/   - rolling Spectre/Serilog logs
```

## Security & Policy

- Follows the strict **NO ROM / NO BIOS / NO DRM KEYS** rule (see `SECURITY.md`).
- No telemetry, no network access required (except optional `dat sync`).
- Open-source under MIT – audit or extend as needed.

## Support Channels

- Issues: [github.com/koobie777/ARK-Retro-Forge/issues](https://github.com/koobie777/ARK-Retro-Forge/issues)
- Wiki / Docs: ongoing at the same repo.
- Side Project Inspiration: [ARK-Ecosystem](https://github.com/koobie777/ARK-Ecosystem) – theming/alignment for protocols.

## Release Flow

- **RC builds** (`vX.Y.Z-rc.N`) – ship early features for testers. Tag and push to trigger the “Release Candidate” workflow.
- **Stable builds** (`vX.Y.Z`) – once RC feedback lands, tag/push to run the “Release” workflow.

---

*ARK-Retro-Forge is part of the broader ARK tooling experiments. Treat every operation like a mission: plan with DRY-RUN, confirm with APPLY, and keep your ROM cache synchronized.*
