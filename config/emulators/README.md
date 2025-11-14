# Emulator Configuration Templates

JSON templates for launching ROMs in emulators.

## Template Format

```json
{
  "exe": ".\\\\emulators\\\\pcsx2\\\\pcsx2.exe",
  "args": "--portable --fullscreen --nogui --game=\"{rom}\"",
  "romVar": "{rom}",
  "biosCheck": [".\\\\emulators\\\\pcsx2\\\\bios\\\\SCPH39001.bin"],
  "portableConfigDir": ".\\\\emulators\\\\pcsx2\\\\portable_data"
}
```

## Variable Tokens

- `{rom}` - Full path to ROM file
- `{dir}` - Directory containing the ROM
- `{title}` - ROM title (if known)
- `{id}` - ROM ID (if known)

## BIOS/Keys Validation

The `biosCheck` array lists files that must exist before launching.
ARK-Retro-Forge will validate their presence but NEVER download them.

## Portable Configurations

When `portableConfigDir` is specified, the emulator should use that directory
for saves, configs, etc., keeping everything self-contained.

## Legal Notice

**You are responsible for:**
- Providing your own BIOS files
- Providing your own encryption keys
- Ensuring you have legal rights to the content you're running

**ARK-Retro-Forge will NEVER:**
- Download BIOS files
- Download encryption keys
- Provide copyrighted firmware
