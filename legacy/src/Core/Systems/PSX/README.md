# PSX System Module

This module provides comprehensive PSX (PlayStation 1) ROM management functionality including renaming, conversion, and metadata extraction.

## Features

### Disc Suffix Normalization
- Automatically normalizes `(Disc N of M)` format to `(Disc N)` for consistency
- Enforces disc suffix for multi-disc titles across BIN, CUE, and CHD files
- Single-disc titles omit disc suffix entirely

### Serial Number Handling
- Extracts standard PSX serials (e.g., `SLUS-01234`, `SCUS-94567`)
- Supports Lightspan educational disc serials (e.g., `LSP-990121`)
- Provides detailed warnings for missing serials

### Content Classification
- **Mainline**: Standard PSX games
- **Cheat**: Cheat discs (GameShark, Action Replay, Xploder, etc.)
- **Educational**: Educational/Lightspan titles
- **Demo**: Demo and preview discs

## Architecture

### Core Components

#### PsxDiscInfo
Metadata record containing:
- Title, region, serial number
- Disc number and total disc count
- Content classification
- File extension
- Diagnostic warnings

#### PsxNameParser
Parses PSX filenames to extract metadata:
- Handles standard format: `Title (Region) [Serial] (Disc N of M).ext`
- Extracts disc information from various formats
- Classifies content types
- Generates appropriate warnings

#### PsxNameFormatter
Generates canonical filenames:
- Enforces consistent naming format
- Applies disc suffix rules (multi-disc vs single-disc)
- Sanitizes invalid characters

#### PsxRenamePlanner
Plans batch rename operations:
- Scans directories for PSX files
- Detects files needing normalization
- Prevents conflicts with existing files

#### PsxConvertPlanner
Plans CUE to CHD conversions:
- Scans for CUE files
- Generates canonical CHD filenames
- Preserves disc metadata in output names

### Interfaces

#### IPsxSerialResolver
- `TryFromFilename`: Extract serial from filename
- `TryFromDat`: Resolve serial from DAT files (stub)
- `TryFromDiscProbe`: Extract serial from disc image (stub)

#### IPsxContentClassifier
- `Classify`: Determine content type from filename and serial

## Usage Examples

### Rename Command
```bash
# Dry run (preview changes)
ark-retro-forge rename psx --root C:\PSX --recursive

# Apply renames
ark-retro-forge rename psx --root C:\PSX --recursive --apply

# Verbose output with full paths
ark-retro-forge rename psx --root C:\PSX --recursive --verbose
```

### Convert Command
```bash
# Dry run (preview conversions)
ark-retro-forge convert psx --root C:\PSX --recursive

# Apply conversions
ark-retro-forge convert psx --root C:\PSX --recursive --apply

# Convert and delete source files
ark-retro-forge convert psx --root C:\PSX --recursive --apply --delete-source
```

## Naming Conventions

### Multi-Disc Titles
```
Before: Game Title (USA) [SLUS-01234] (Disc 1 of 2).bin
After:  Game Title (USA) [SLUS-01234] (Disc 1).bin

Before: Game Title (USA) [SLUS-01235] (Disc 2 of 2).cue
After:  Game Title (USA) [SLUS-01235] (Disc 2).cue
```

### Single-Disc Titles
```
Correct: Final Fantasy VII (USA) [SCUS-94163].bin
Wrong:   Final Fantasy VII (USA) [SCUS-94163] (Disc 1).bin
```

### Educational/Lightspan Discs
```
16 Tales 1 [LSP-990121].bin
P.K.'s Math Studio [LSP-06019].bin
```

## Diagnostics

### Warning Types
- **Serial number not found**: Standard titles without PSX serial
- **Cheat/utility disc; serial intentionally not enforced**: Cheat discs
- **Educational/Lightspan content; non-standard ID format**: Educational titles with LSP-xxxxx IDs

### Summary Metrics
- Already named count
- To rename count
- Total warnings
- Cheat disc count
- Educational disc count
- Missing serial count

## Future Enhancements

The following features are planned for future releases:
- Multi-disc grouping (auto-detect disc 2, 3, etc. from title matching)
- DAT-based serial resolution
- On-disc serial probing (reading SYSTEM.CNF)
- M3U playlist generation and management
- Region detection from disc images

## Testing

The module includes comprehensive test coverage:
- 26 unit tests covering parsers, formatters, and classifiers
- 3 integration tests covering real-world scenarios
- All tests validate against the "Alone in the Dark" multi-disc example from the specification
