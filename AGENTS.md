# Repository Guidelines & Agent Protocols

## 1. Project Architecture

### Core Components (`src/Core`)
*   **`IO/ArkStaging`**: **CRITICAL**. All file operations (Move, Copy, Delete, Write) MUST go through `ArkStaging`. This ensures DRY-RUN safety, rollback capabilities, and operation tracking. Never use `File.*` or `Directory.*` directly for modifying user data.
*   **`Dat/`**: Handles Redump/No-Intro DAT parsing and metadata lookups. `DatMetadataIndex` is the source of truth for serials and disc counts.
*   **`Database/RomRepository`**: SQLite cache for scanned files. Used by Cleaner and other tools to avoid re-scanning disk.
*   **`Systems/PSX`**: Domain logic for PlayStation operations (Rename, Merge, Convert, Playlist, Clean).

### CLI Layer (`src/Cli`)
*   **`Spectre.Console`**: UI library for all commands. Use `AnsiConsole` for output.
*   **`Medical Bay`**: The diagnostic tool. Always the first step in debugging user environment issues.
*   **`Infrastructure`**: `SessionStateManager` (persists root/system/dry-run), `CancellationMonitor` (global ESC handler).

## 2. Development Workflow

*   **Build**: `dotnet build` (Treats warnings as errors).
*   **Test**: `dotnet test` (xUnit).
*   **Run**: `dotnet run --project src/Cli/ARK.Cli.csproj -- <command> <args>`
*   **Format**: `dotnet format`

## 3. Testing Strategy

### Unit Tests (`tests/`)
*   Focus on logic (Parsers, Planners, Formatters).
*   Mock `IArkStaging` or file system abstractions where possible.

### Workspace Integration Tests (Agent-Driven)
*   **Mandatory for File Operations**: When verifying CLI commands (Rename, Merge, Clean), DO NOT run against real user data.
*   **Procedure**:
    1.  Create a temporary directory: `mkdir test_workspace`
    2.  Populate with dummy files: `New-Item ...`
    3.  Run the CLI command against this workspace.
    4.  Verify results (file existence, content).
    5.  Clean up: `Remove-Item test_workspace -Recurse`

## 4. Release Protocol

*   **Versioning**: Managed by `MinVer`.
    *   **Dev**: `v1.1.0-alpha.0.1` (Automatic on `dev` push).
    *   **RC**: `v1.1.0-rc.1` (Tag from `dev` or `rc` branch).
    *   **Stable**: `v1.1.0` (Tag from `main` branch).
*   **Branch Flow**:
    *   `dev`: Main development branch.
    *   `main`: Stable release branch.
    *   **Merge Flow**: `dev` -> `main` (via PR or direct merge for admins).
*   **Tagging**: Agents must manually push tags to trigger release workflows.
    *   `git tag v1.1.0 && git push origin v1.1.0`

## 5. Agent Instructions (How to Help)

1.  **Context First**: Check `Medical Bay` status and `UPDATE.md` history before suggesting fixes.
2.  **Safety First**: Always assume **DRY-RUN** is the default. Use `ArkStaging` for everything.
3.  **Verification**: Use the "Workspace Integration Test" pattern to prove your fix works.
4.  **Documentation**: Update `UPDATE.md` with every user-facing change.

### Specific Knowledge
*   **PSX Merge**: Handles "Track 1" vs "Track 01" fuzzy matching.
*   **PSX Rename**: Normalizes paths to prevent case-insensitive deletion bugs on Windows.
*   **PSX Playlist**: Only generates `.m3u` for multi-disc games (2+ discs).
*   **Cancellation**: All long-running loops must check `context.CancellationToken`.
