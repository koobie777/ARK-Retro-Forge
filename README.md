# ARK-Retro-Forge

> **Mission Tagline:** Ark Station distributes a portable .NET 8 toolkit for charting, validating, and organizing ROM cargo across Sony/Nintendo/SEGA sectors. Every sortie launches in **DRY-RUN** and ships as a single-file EXE; no installers, no registry debris.

---

## Mission Signal

- **Command Deck** – Running `ark-retro-forge` opens the Spectre.Console UI with live telemetry, DRY-RUN/APPLY toggle, remembered ROM root, and per-instance routing.
- **Instance Profiles** – `--instance <profile>` keeps databases, DAT caches, and logs under `./instances/<profile>/`. Spin RC, dev, and lab branches in parallel without cross-contamination.
- **Zero ROM Policy** – Ark transports tooling only. Bring your own ROMs/BIOS/keys plus the external utilities (`chdman`, `maxcso`, `wit`, `ffmpeg`, etc.) under `./tools/`.
- **DAT Intelligence** – `dat sync` pulls Redump/No-Intro catalogs into each instance. Renamer, Cleaner, and Merge planners use this intel to recover serials, disc counts, playlists, and warnings even when filenames are noisy, while PSX analyzers now read BIN/ISO headers first and only fall back to DAT entries when necessary.
- **Telemetry-Free** – No network traffic unless you explicitly run `dat sync`. No analytics. Logs stay in your mission folder.

---

## Flight Manifest

| Operation | Purpose | Highlights |
|-----------|---------|------------|
| `medical-bay` | Tooling diagnostics | Detects required/optional utilities, shows version + path, emits JSON & Serilog logs. |
| `scan` | ROM discovery | Recursive Spectre progress, per-extension stats, hydrates the ROM cache. |
| `verify` | Hash integrity | Streams CRC32/MD5/SHA1 with throughput metrics; updates ROM cache. |
| `rename psx` | Deterministic naming | `Title (Region) [Serial]` output, playlist planner integration. |
| `convert psx` | BIN/ISO/CHD pipelines | Media detection, delete-source safeguards, `--rebuild` support. |
| `merge psx` | Multi-track consolidation | DAT-aware planner rewrites single BIN/CUE and highlights blockers. |
| `clean psx` | Organizer & staging | Moves multi-track + multi-disc sets into `<Title (Region)>/<Title (Region) (Disc N)>`, builds missing CUEs, flattens safe folders, ingests staged imports. |
| `duplicates psx` | Hash dedupe | SHA1/MD5/CRC32 detection with live hashing progress + optional JSON report. |
| `extract archives` | ZIP/7Z/RAR management | Batch header, ESC cancel, optional delete-source. |
| `dat sync` | Catalog fetcher | Downloads JSON-defined Redump/No-Intro DATs into instance `dat/` folders. |

---

## Mission Console (Interactive Menu)

1. **Medical Bay** – always run before deploying. Confirms tooling loadout, shows a DAT snapshot, and can drop into the DAT console for per-system syncs.
2. **Scan / Verify** – offers recursive toggle, ROM-root reminder, Spectre dashboards per run.
3. **PSX Ops** – rename / convert / merge / clean / duplicates w/ prompts mirroring CLI flags and DRY-RUN awareness.
4. **Archive Extract & DAT Sync** – manage ingestion + catalog refresh without leaving the deck.

> DRY-RUN persists per session. Flip to APPLY before destructive operations; the deck resets to DRY-RUN next launch. Press **ESC** to cancel any prompt or return to the previous menu.

---

## Ops Briefs
All verbs now open with a Spectre header that highlights the current instance, scope, and DRY-RUN/APPLY mode so you immediately know whether changes will be written. ESC always cancels the current prompt/menu.

### Medical Bay
- Replaces the legacy `doctor` check with richer Spectre output + JSON export.
- Displays Ready/Optional/Missing status, minimum version, and path per tool.
- Surfaces DAT catalog health (Ready/Stale/Missing) per system, renders a compact snapshot, and offers the DAT console for filtering/searching the full catalog plus quick-sync presets (missing/stale, ready/force, active system). ESC skips any prompt instantly.
- `ark-retro-forge medical-bay` or via the menu.

### Scan
- Discovers supported extensions (`.bin/.cue/.iso/.chd/.pbp` + Nintendo/Sega formats).
- Hydrates the SQLite ROM cache per instance.
- Shows per-extension totals + file size distribution.

### Verify
- Streams CRC32/MD5/SHA1 per file, recording throughput + elapsed time.
- Safe to cancel; best executed after `scan` to keep cache hot.

### Rename (PSX)
- Applies canonical `Title (Region) [Serial]` naming, disc numbering, and playlist building.
- Honors DAT intel to recover missing serials and disc counts.
- Strips trailing language markers like `(En,Ja,Fr)` by default; pass `--keep-language-tags` if you prefer to keep them.

### Convert (PSX)
- CD/DVD-aware CHD/BIN/ISO conversions powered by user-supplied `chdman`.
- `--delete-source` requires `--apply`. `--rebuild` forces conversion even if CHDs exist.

### Merge (PSX)
- Consolidates multi-track BIN/CUE layouts into a single BIN + updated CUE.
- Blocks merges when disc metadata indicates a true multi-disc SKU.

### Clean (PSX Organizer)
- Corrals multi-track discs into `<Title (Region)>/<Title (Region)>` directories (configurable container available).
- Detects Disc 1/Disc 2/Disc 3 sets and rehousing them into `<Title (Region)>/<Title (Region) (Disc N)>` so flattening never destroys true multi-disc structures.
- Generates missing CUE files by grouping Track 01/Track 02 BINs (data track first, audio tracks after), ingests ROMs from staging directories using ROM cache + DAT validation, and flattens only safe single-disc folders.

### Duplicates (PSX)
- Hashes every file (SHA1 default) with a Spectre progress bar and summary stats.
- Groups duplicates by hash; optional `--json` output to `logs/` for review.

### Extract Archives
- Recap header with root/output info, recursive mode, optional delete-source, ESC cancel support.

### DAT Sync
- Reads `config/dat/dat-sources.json`, downloads catalogs into `instances/<profile>/dat/<system>/`, respects cache unless `--force` is supplied.

---

## Quick Start Checklist

1. **Download** the latest RC/stable release.
2. **Provision tools** (`chdman`, `maxcso`, etc.) under `./tools/`.
3. **Run `medical-bay`** to confirm diagnostics.
4. **Set ROM root** (menu or CLI) and save for reuse.
5. **Scan + Verify** to populate ROM + hash caches.
6. **Execute ops** (rename/convert/merge/clean/extract) once APPLY is engaged.

---

## CLI Reference

```ps1
ark-retro-forge medical-bay
ark-retro-forge scan --root D:\ROMs --recursive
ark-retro-forge verify --root D:\ROMs --recursive
ark-retro-forge rename psx --root D:\PSX --recursive --apply
ark-retro-forge convert psx --root D:\PSX --to chd --apply --delete-source
ark-retro-forge merge psx --root D:\PSX --recursive --apply
ark-retro-forge clean psx --root D:\PSX --move-multitrack --move-multidisc --generate-cues --flatten --apply
ark-retro-forge duplicates psx --root D:\PSX --recursive --hash md5 --json
ark-retro-forge extract archives --root C:\Downloads --output D:\Incoming --recursive --apply
ark-retro-forge dat sync --system psx --force
```

**Global options:** `--dry-run` (default), `--apply`, `--force`, `--workers <N>`, `--instance <name>`, `--theme`, `--verbose`.

---

## Build & Layout

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish src/Cli/ARK.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -p:SuppressTrimAnalysisWarnings=true
```

```
src/
  Core/   # hashing, DAT catalog, planners, persistence
  Cli/    # Spectre.Console verbs + interactive menu
config/   # emulator templates, DAT definitions
tools/    # user-supplied binaries (chdman, etc.)
instances/<profile>/
  db/     # SQLite ROM cache
  dat/    # downloaded DAT archives
  logs/   # Serilog + Spectre history
```

---

## Protocol & Support

- **Security** – Strict **NO ROM / NO BIOS / NO DRM KEYS** policy (see `SECURITY.md`). No telemetry.
- **Issues** – [github.com/koobie777/ARK-Retro-Forge/issues](https://github.com/koobie777/ARK-Retro-Forge/issues)
- **Docs** – Live in this repo (README/UPDATE) and the wider [ARK-Ecosystem](https://github.com/koobie777/ARK-Ecosystem).
- **Sponsor** – [github.com/sponsors/koobie777](https://github.com/sponsors/koobie777)

---

## Release Flow

- **RC builds** (`vX.Y.Z-rc.N`) – tag from `rc-upgrade` to trigger the Release Candidate workflow.
- **Stable builds** (`vX.Y.Z`) – after RC sign-off, tag from `main` to push a stable release.

---

*Treat every operation like a sortie: stage in DRY-RUN, confirm with APPLY, keep your ROM cache synchronized, and never skip Medical Bay.*
