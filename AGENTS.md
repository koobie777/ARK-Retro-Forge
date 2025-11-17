# Repository Guidelines

## Project Structure & Module Organization

* **Repository**: ARK-Retro-Forge
* **`src/Core`** – hashing, DAT metadata index, planners (scan/verify/rename/convert/merge/clean/extract).
* **`src/Cli`** – Spectre.Console verbs + interactive menu (Medical Bay, scan/verify, PSX ops, DAT sync, archive extract).
* **`plugins/`** experimental feature packs; **`config/`** profiles/templates/DAT catalog; **`tools/`** user-supplied binaries (chdman, maxcso, wit, ffmpeg, etc.).
* **`instances/<profile>/`** – per-instance `db/`, `dat/`, `logs/`, `session.json` storing remembered ROM root/system and DRY-RUN state.
* **`tests/`** (`ARK.Tests`) – xUnit coverage for planners/detectors.

## Build, Test, and Development Commands

* Restore: `dotnet restore`
* Build (Release): `dotnet build -c Release`
* Run CLI: `dotnet run --project src/Cli/ARK.Cli.csproj -- scan --root F:\ROMs --json`
* Publish portable CLI: `dotnet publish src/Cli/ARK.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
* Format/lint: `dotnet format`
* Test + coverage: `dotnet test /p:CollectCoverage=true /p:CoverletOutput=TestResults/coverage.xml`

## Coding Style & Naming Conventions

* C# 12, .NET 8, nullable enabled, **treat warnings as errors**.
* Names: PascalCase (types/methods), camelCase (locals/params), `_camelCase` (private fields), UPPER_SNAKE (const).
* File-scoped namespaces, explicit access modifiers.
* Tools: EditorConfig + .NET analyzers (optional: StyleCop.Analyzers).

## Testing Guidelines

* Framework: **xUnit**; one test project per module.
* Test names: `Method_Scenario_Expected()` using AAA (Arrange-Act-Assert).
* Fixtures under `tests/Fixtures/`. Aim >=70% line coverage for Core; justify exceptions in PR.

## Commit, Branch & Release Guidelines

* **Conventional Commits** (e.g., `feat(core): add CHD planner (#123)`).
* Work lands on feature branches -> `rc-upgrade`. Open PRs targeting `main`; branch protections require green `Build and Test`, `CodeQL`, `Release Candidate` checks.
* Tag RCs `vX.Y.Z-rc.N` to trigger the release-candidate workflow; stable releases use `vX.Y.Z`.
* One logical change per PR with description, linked issue, CLI screenshots for UX shifts, test plan, and rollback notes.
* CI must pass; behavior changes demand README/UPDATE.md edits plus any necessary diagrams.

## Security & Configuration Tips

* **No ROMs/keys/firmware.** Portable only; do not write outside the repo folder.
* Verify checksums for downloaded tools; keep `tools/` quarantined. Redact personal paths in logs.

## Agent-Specific Instructions (for AI/Copilot)

Provide **in this order**:

1. Complete file(s) or minimal working diff.
2. One-liner patch.
3. Paths & exact apply steps.
4. `UPDATE.md` entry (semver + bullets).
5. Brief reasoning + rollback.

Default to dry-run for destructive ops; keep outputs deterministic and reversible.
Default to dry-run for destructive ops; keep outputs deterministic and reversible.
See also `.github/instructions.md` for Copilot-specific reminders (branch rules, Medical Bay, DAT intel).

### Project-Specific Knowledge

* **Medical Bay** replaces the old doctor verb; always mention it first in docs and Quick Start steps.
* The CLI menu remembers ROM root/system/dry-run via `session.json`; respect this when touching Program.cs.
* DAT intelligence lives under `config/dat` + `DatMetadataIndex`; PSX flows (rename/merge/clean) rely on it, so never regress multi-disc detection or serial recovery without tests.
* Archive Extract, Scan, Verify, and PSX operations share a unified quit handler (ESC/Ctrl+C) � keep behavior consistent.
* RC builds are produced from tags; releases rely on `.github/workflows/release-candidate.yml` (beware reserved env variables like `VERSION`).
