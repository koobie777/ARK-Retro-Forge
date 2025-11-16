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
- **Convert**: CHD/CSO/RVZ compression via external tools (now bi-directional CHD↔BIN/CUE/ISO with automatic CD/DVD detection)
- **Merge**: Consolidate PSX multi-track BIN sets into a single BIN + rewritten CUE
- **Extract**: Built-in ZIP/7Z/RAR extraction workflow with optional source cleanup
- **Cache**: Local ROM catalog that records scan/verify results for cross-referencing with scraped metadata
- **Doctor**: Environment validation and tool checking
- **Portable**: Single-file EXE, no registry, no admin required

## Quick Start

1. Download the latest release
2. Place external tools (e.g., chdman.exe) in `.\tools\` directory
3. Run `ark-retro-forge` with no arguments to launch the interactive operations menu
4. Run `ark-retro-forge doctor` (or pick option 1 in the menu) to verify setup
5. Run `ark-retro-forge scan --root C:\ROMs` (or set a ROM root from the menu) to build the local ROM cache
6. Run `ark-retro-forge verify --root C:\ROMs` to update hashes and integrity info in the cache

### Local Development Shortcut

When working from source, use the provided `ark-retro-forge.cmd` shim in the repo root:

```powershell
# From repo root
ark-retro-forge doctor
ark-retro-forge scan --root C:\ROMs --workers 4
```

Set `ARKRF_CONFIGURATION=Release` before running the script if you want it to invoke the Release build instead of the default Debug configuration.

## CLI Usage

```bash
# Check environment and tools
ark-retro-forge doctor

# Launch interactive menu (doctor/scan/verify/rename/convert/merge/extract)
ark-retro-forge
# Toggle the persistent DRY-RUN/APPLY mode from the interactive menu (resets to DRY-RUN on next launch)
# Run a second CLI session with its own database and logs
ark-retro-forge --instance psx-dev

# Scan for ROMs
ark-retro-forge scan --root C:\ROMs --workers 4

# Verify ROM integrity
ark-retro-forge verify --root C:\ROMs --crc32 --md5 --sha1

# Preview rename operations (dry-run by default)
ark-retro-forge rename --root C:\ROMs

# Apply rename operations
ark-retro-forge rename --root C:\ROMs --apply --force

# Convert PSX media in either direction (CUE -> CHD, CHD -> BIN/CUE or ISO)
ark-retro-forge convert psx --root C:\ROMs --to chd --apply
ark-retro-forge convert psx --root C:\ROMs --to bin --apply --delete-source
ark-retro-forge convert psx --root C:\ROMs --to iso --apply

# Merge PSX multi-track BINs into a single BIN/CUE (prompts before deleting the sources)
ark-retro-forge merge psx --root C:\ROMs --recursive --apply

# Extract archives (zip/7z/rar) from a directory tree, optionally deleting the source archives
ark-retro-forge extract archives --root C:\Downloads --output C:\ROMs\Imports --recursive --apply --delete-source
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

