# Plugins Directory

Drop-in .NET plugin DLLs go here. Plugins extend ARK-Retro-Forge with system-specific functionality.

## Plugin Structure

Each plugin should implement the `ISystemModule` interface from `ARK.Core.Plugins`.

Example structure:
```
plugins/
  MyPlugin/
    MyPlugin.dll
    MyPlugin.plugin.json
```

## Plugin Manifest (plugin.json)

```json
{
  "name": "MyPlugin",
  "version": "1.0.0",
  "systems": ["PS1", "PS2"],
  "capabilities": ["hash", "verify", "convert"],
  "minCoreApi": "1.0",
  "entryAssembly": "MyPlugin.dll"
}
```

## Security

Plugins run in isolated AssemblyLoadContext instances for safety. However:

- Only install plugins from trusted sources
- Plugins have access to file system within declared roots
- Plugins can be unloaded safely

## Development

See `docs/plugin-development.md` for creating your own plugins.

## Discovery

Run `ark-retro-forge` with `--verbose` to see plugin loading details.
