# Configuration Directory

JSON configuration files for ARK-Retro-Forge.

## Files

### config.json (Main Configuration)
Application-wide settings.

### systems/*.json (Per-System Profiles)
System-specific configuration including naming rules, region priorities, etc.

### emulators/*.json (Emulator Launch Templates)
Templates for launching ROMs in various emulators.

## Configuration Resolution Order

1. CLI flags (highest priority)
2. Environment variables
3. config/*.json files
4. Built-in defaults (lowest priority)

## Example: config.json

```json
{
  "defaultWorkers": 4,
  "hashBufferSize": 8388608,
  "enableTelemetry": false,
  "theme": "dark"
}
```

## Validation

Configuration files are validated against JSON schemas on startup.
