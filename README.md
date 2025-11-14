# ARK-Retro-Forge

Portable .NET 8 C# ROM manager for PS/Nintendo/Xbox/SEGA: scan/clean/verify/compress/organize + emulator launch. Single-file EXE, no installer, dry-run by default.

## ⚠️ NO-ROM POLICY ⚠️

**This software does NOT include:**
- ROM files
- BIOS files
- Encryption keys
- Copyrighted game content
- External tools (chdman, maxcso, etc.)

**You are responsible for:**
- Legally obtaining your own ROM files
- Providing your own BIOS/firmware files
- Downloading external tools from official sources
- Complying with copyright laws in your jurisdiction

## Features

- **Scan**: Fast directory scanning with ROM discovery
- **Verify**: Streaming hash verification (CRC32, MD5, SHA1)
- **Rename**: Deterministic renaming to "Title (Region) [ID]" format
- **Convert**: CHD/CSO/RVZ compression via external tools
- **Doctor**: Environment validation and tool checking
- **Portable**: Single-file EXE, no registry, no admin required

## Quick Start

1. Download the latest release
2. Place external tools (e.g., chdman.exe) in `.\tools\` directory
3. Run `ark-retro-forge doctor` to verify setup
4. Run `ark-retro-forge scan --root C:\ROMs` to discover files
5. Run `ark-retro-forge verify --root C:\ROMs` to check integrity

## CLI Usage

```bash
# Check environment and tools
ark-retro-forge doctor

# Scan for ROMs
ark-retro-forge scan --root C:\ROMs --workers 4

# Verify ROM integrity
ark-retro-forge verify --root C:\ROMs --crc32 --md5 --sha1

# PSX: Rename disc images to canonical format (dry-run by default)
ark-retro-forge rename psx --root C:\PSX --recursive

# PSX: Apply rename operations
ark-retro-forge rename psx --root C:\PSX --recursive --apply

# PSX: Convert BIN/CUE to CHD format (dry-run by default)
ark-retro-forge convert psx --root C:\PSX --recursive

# PSX: Apply conversion and delete source files
ark-retro-forge convert psx --root C:\PSX --recursive --apply --delete-source
```

## PlayStation (PSX) Features

### Disc Suffix Normalization
- Automatically normalizes disc suffixes to canonical `(Disc N)` format
- Converts `(Disc 1 of 2)`, `(CD 1)`, `(DVD 1)` → `(Disc 1)`
- Removes disc suffix for single-disc titles

### Serial Validation
- Extracts serials from filenames: `[SLUS-00001]`, `[SCUS-94163]`
- Validates serial format (SLUS, SCUS, SLPS, SCPS, SLES, SCES, LSP)
- Warns when serials are missing or invalid

### Cheat/Educational Disc Handling
Three modes available via `--cheats` flag:
- `standalone` (default): Cheat discs kept as separate titles
- `omit`: Cheat discs excluded from operations
- `as-disc`: Advanced mode (may associate cheats with games)

Supported cheat types:
- GameShark, Xploder, Action Replay, CodeBreaker
- Lightspan educational discs

### Canonical Naming Format
- Single-disc: `Title (Region) [SERIAL].ext`
- Multi-disc: `Title (Region) [SERIAL] (Disc N).ext`

Example:
```
Before: Final Fantasy VII (USA) (Disc 1 of 3) [SLUS-00001].cue
After:  Final Fantasy VII (USA) [SLUS-00001] (Disc 1).cue
```

## Global Options

- `--dry-run` (default): Preview changes without applying
- `--apply`: Apply changes to files
- `--force`: Force operation even with warnings (requires --apply)
- `--workers N`: Number of parallel workers (default: CPU count)
- `--verbose`: Verbose output
- `--report <dir>`: Directory for reports
- `--theme dark|light`: Color theme

## Requirements

- Windows 11 or Windows 10
- .NET 8.0 Runtime (self-contained in single-file EXE)
- External tools (optional): chdman, maxcso, wit, dolphin-tool

## Building from Source

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build -c Release

# Run tests
dotnet test -c Release

# Publish single-file EXE
dotnet publish src/Cli/ARK.Cli.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:PublishTrimmed=true
```

## Project Structure

```
src/
  Core/        - Core libraries (hashing, DAT, tools, plugins, database)
  Cli/         - Command-line interface
tests/         - xUnit tests
tools/         - External CLI tools (user-supplied)
plugins/       - Plugin DLLs (drop-in)
config/        - Configuration files
  emulators/   - Emulator launch templates
db/            - SQLite databases
logs/          - Rolling log files
```

## Security

- See [SECURITY.md](SECURITY.md) for security policy
- No telemetry by default
- No internet access required
- Open source - review the code!

## License

MIT License - see [LICENSE](LICENSE) for details

## Contributing

Contributions welcome! Please read our contributing guidelines and code of conduct.

## Support

- GitHub Issues: https://github.com/koobie777/ARK-Retro-Forge/issues
- Documentation: https://github.com/koobie777/ARK-Retro-Forge/wiki

## Disclaimer

This tool is for managing legally obtained ROM files only. Users are responsible for complying with all applicable laws and regulations regarding ROM files, BIOS files, and copyrighted content in their jurisdiction.

