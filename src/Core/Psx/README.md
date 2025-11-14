# PSX Module

This module provides comprehensive PSX (PlayStation 1) file management capabilities for ARK-Retro-Forge.

## Features

### Core Capabilities
- **File Grouping**: Automatically scans and groups PSX files (BIN/CUE, CHD, PBP) into logical title groups
- **Multi-Disc Support**: Recognizes and handles multi-disc games correctly
- **Metadata Extraction**: Extracts title, region, version, and serial information from filenames
- **Article Normalization**: Properly handles titles with articles (e.g., "Legend of Dragoon, The" → "The Legend of Dragoon")

### Rename Pipeline
- Renames PSX files to standardized format: `Title (Region) [vX.Y] [Serial].ext`
- Supports flattening multi-disc titles from per-game folders to parent directory
- Handles associated BIN files when renaming CUE files
- Dry-run mode by default for safety

### Convert Pipeline
- Converts BIN/CUE → CHD using chdman
- Converts CHD → BIN/CUE using chdman
- Automatic M3U playlist generation for multi-disc titles
- Optional source deletion after successful conversion
- Bounded parallelism for batch conversions (default: 4 concurrent)
- Skips titles already in target format

## Naming Convention

### Single-Disc Titles
Format: `Title (Region) [vX.Y] [Serial].ext`

Examples:
- `Crash Bandicoot (USA) [SCUS-94900].chd`
- `Spyro the Dragon (Europe).chd`
- `Final Fantasy VII (USA) [v1.1] [SCUS-94163].chd`

Optional components:
- `[vX.Y]` - Version (omitted if unknown)
- `[Serial]` - Game serial (omitted if unknown)
- `(Region)` - Region (omitted if unknown)

### Multi-Disc Titles
Disc Format: `Title (Region) [vX.Y] [DiscSerial].ext`
Playlist Format: `Title (Region) [vX.Y].m3u`

Examples:
- `Metal Gear Solid (USA) [SLUS-00594].chd` (Disc 1)
- `Metal Gear Solid (USA) [SLUS-00776].chd` (Disc 2)
- `Metal Gear Solid (USA).m3u` (Playlist)

Note: Playlists omit serial numbers as they represent the entire title.

## CLI Commands

### Interactive Helper
```bash
ark-retro-forge psx --root /path/to/psx --recursive [--apply]
```

Provides interactive prompts to:
1. Choose operation (rename only, convert only, or both)
2. Select multi-disc handling (keep folders or flatten)
3. Decide whether to delete sources after conversion

### Rename Command
```bash
ark-retro-forge rename psx --root /path/to/psx --recursive [--flatten-multidisc] [--apply] [--force]
```

Options:
- `--root <path>` - Root directory to scan (required)
- `--recursive` - Scan subdirectories recursively
- `--flatten-multidisc` - Move multi-disc files to parent directory
- `--apply` - Apply changes (default: dry-run)
- `--force` - Skip confirmation (requires --apply)

### Convert Command
```bash
ark-retro-forge convert psx --root /path/to/psx --recursive [--target-format chd] [--delete-source] [--apply] [--force]
```

Options:
- `--root <path>` - Root directory to scan (required)
- `--recursive` - Scan subdirectories recursively
- `--flatten-multidisc` - Move converted files to parent directory
- `--target-format <format>` - Target format (default: chd)
- `--from-chd-to-bincue` - Convert CHD to BIN/CUE (default: BIN/CUE to CHD)
- `--delete-source` - Delete source files after successful conversion
- `--apply` - Apply changes (default: dry-run)
- `--force` - Skip confirmation (requires --apply)

## Architecture

### Core Components

**Models:**
- `PsxDisc` - Represents a single disc with metadata
- `PsxTitleGroup` - Represents a logical game title (one or more discs)
- `RenamePlan` / `ConvertPlan` - Immutable operation plans

**Services:**
- `PsxGrouper` - Scans and groups PSX files
- `PsxNamingService` - Generates standardized filenames
- `PsxRenamePlanner` / `PsxRenameExecutor` - Plan and execute renames
- `PsxConvertPlanner` / `PsxConvertExecutor` - Plan and execute conversions

**Tools:**
- `IChdTool` / `ChdmanTool` - CHD conversion tool abstraction

### Safety Design

1. **Dry-run by default**: All operations preview changes without modifying files unless `--apply` is specified
2. **User confirmation**: Shows summary and asks for confirmation before destructive operations
3. **Conflict detection**: Skips operations if target file already exists
4. **No source deletion**: Source files are never deleted unless `--delete-source` is explicitly set
5. **Error handling**: Captures and reports errors without failing entire operation

## External Dependencies

- **chdman**: Required for CHD conversions (place in `./tools/` directory)
  - Download from: https://mamedev.org/

Run `ark-retro-forge doctor` to verify tool availability.

## Examples

### Dry-run rename (preview only)
```bash
ark-retro-forge rename psx --root C:\ROMs\PSX --recursive
```

### Apply rename with flattening
```bash
ark-retro-forge rename psx --root C:\ROMs\PSX --recursive --flatten-multidisc --apply
```

### Convert BIN/CUE to CHD (dry-run)
```bash
ark-retro-forge convert psx --root C:\ROMs\PSX --recursive
```

### Convert BIN/CUE to CHD and delete sources
```bash
ark-retro-forge convert psx --root C:\ROMs\PSX --recursive --delete-source --apply
```

### Convert CHD back to BIN/CUE
```bash
ark-retro-forge convert psx --root C:\ROMs\PSX --recursive --from-chd-to-bincue --apply
```

### Interactive workflow
```bash
ark-retro-forge psx --root C:\ROMs\PSX --recursive --apply
```
