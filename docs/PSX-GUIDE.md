# PlayStation (PSX) Rename and Convert Guide

This guide covers ARK-Retro-Forge's PlayStation (PSX) disc image management features.

## Overview

ARK-Retro-Forge provides safe, user-friendly tools for:
- Renaming PSX disc images to canonical format
- Converting BIN/CUE to space-efficient CHD format
- Handling multi-disc titles correctly
- Managing cheat/utility discs
- Validating serial numbers

## Canonical Naming Format

### Single-Disc Titles
Format: `Title (Region) [SERIAL].ext`

Example:
```
Crash Bandicoot (USA) [SCUS-94163].cue
Crash Bandicoot (USA) [SCUS-94163].bin
```

### Multi-Disc Titles
Format: `Title (Region) [SERIAL] (Disc N).ext`

Example:
```
Final Fantasy VII (USA) [SLUS-00001] (Disc 1).cue
Final Fantasy VII (USA) [SLUS-00001] (Disc 2).cue
Final Fantasy VII (USA) [SLUS-00001] (Disc 3).cue
```

## Disc Suffix Normalization

The tool automatically normalizes various disc suffix patterns to canonical `(Disc N)` format.

### Patterns Recognized
- `(Disc 1 of 2)` → `(Disc 1)`
- `(Disc 2 of 3)` → `(Disc 2)`
- `(CD 1)` → `(Disc 1)`
- `(CD1)` → `(Disc 1)`
- `(DVD 2 of 3)` → `(Disc 2)`
- `(Disk 1)` → `(Disc 1)`

### Rules
1. Multi-disc titles ALWAYS get `(Disc N)` suffix
2. Single-disc titles NEVER get disc suffix
3. "of M" is always removed
4. Disc number is preserved

## Cheat and Utility Disc Handling

Use `--cheats <mode>` flag to control behavior:

### `standalone` (Default, Recommended)
Each cheat disc is treated as its own title, never grouped with games.

### `omit`
Cheat discs are completely excluded from operations.

### `as-disc` (Advanced)
Associate cheat discs with nearby games (may cause confusion).

⚠️ **Not recommended** - can mis-label cheat discs as game discs.

## Command Line Reference

```bash
ark-retro-forge rename psx [options]
  --root <path>       Root directory (required)
  --recursive         Scan subdirectories
  --apply             Apply renames (default: dry-run)
  --cheats <mode>     omit|standalone|as-disc (default: standalone)
  --json              JSON output

ark-retro-forge convert psx [options]
  --root <path>       Root directory (required)
  --recursive         Scan subdirectories
  --apply             Apply conversions (default: dry-run)
  --delete-source     Delete BIN/CUE after conversion (requires --apply)
  --cheats <mode>     omit|standalone|as-disc (default: standalone)
  --json              JSON output
```

## Best Practices

1. **Always preview first**: Use dry-run to verify operations
2. **Backup critical collections**: Before bulk operations
3. **Use `--recursive`**: To process entire directory trees
4. **Keep BIN/CUE initially**: Don't use `--delete-source` until CHDs are verified
5. **Add serials manually**: For best results, add `[SERIAL]` to filenames
