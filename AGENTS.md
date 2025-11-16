# Repository Guidelines

## Project Structure & Module Organization

* **Repository**: ARK-Retro-Forge
* `src/RetroForge.Core` - scan/verify/rename/combine/convert/DAT/doctor/launch
* `src/RetroForge.Cli` - Spectre.Console CLI verbs
* `src/RetroForge.Gui` - WPF (.NET 8) MVVM GUI (themes: void/orbital/plain)
* `plugins/` feature packs; `config/` profiles/templates/scraper catalog; `tools/` user CLIs (e.g., chdman, ffmpeg)
* `tests/` (`RetroForge.*.Tests`); `logs/`; `reports/`; `.docs/`

## Build, Test, and Development Commands

* Restore: `dotnet restore`
* Build (Release): `dotnet build -c Release`
* Run CLI: `dotnet run --project src/RetroForge.Cli -- scan --root F:\ROMs --json`
* Run GUI: `dotnet run --project src/RetroForge.Gui`
* Publish portable (CLI): `dotnet publish src/RetroForge.Cli -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`
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

## Commit & Pull Request Guidelines

* **Conventional Commits** (e.g., `feat(core): add CHD planner (#123)`).
* One logical change per PR. Include description, linked issue, CLI/GUI screenshots for UI changes, test plan, rollback notes.
* CI must pass; update docs (README/UPDATE.md) on behavior changes.

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
