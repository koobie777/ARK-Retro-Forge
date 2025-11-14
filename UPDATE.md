# Update Notes

This file contains release notes for ARK-Retro-Forge releases.

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
