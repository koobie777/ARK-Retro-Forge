# Copilot Instructions for ARK-Retro-Forge

## Quick Mission Checklist
1. **Start in DRY-RUN** – every CLI verb defaults to preview mode. Never force destructive actions unless a user explicitly opts into APPLY.
2. **Call Medical Bay First** – the environment check was renamed from `doctor`. Reference `ark-retro-forge medical-bay` (or menu equivalent) before other suggestions.
3. **Respect Instances** – all tooling persists data under `instances/<profile>/`. Read/write paths must stay inside the repo (portable-only security rule).
4. **Know the Branch Flow** – feature work lives on `rc-upgrade`, `main` is protected. Pull requests must target `main` and pass `Build and Test`, `CodeQL`, and `Release Candidate` checks.
5. **Tagging** – RC tags are `vX.Y.Z-rc.N` and trigger `.github/workflows/release-candidate.yml`. Avoid using environment variables named `VERSION` in workflows.

## When Editing Code
- **Languages/Frameworks** – C# 12, .NET 8, nullable enabled, warnings-as-errors. Use file-scoped namespaces, explicit access modifiers, and PascalCase/camelCase conventions.
- **Logging & Prompts** – Spectre.Console is the CLI surface. Keep prompts localized, DRY-RUN vs APPLY messaging obvious, and reuse shared helpers (e.g., session persistence, cancellation monitor).
- **DAT Intelligence** – `DatMetadataIndex` backs PSX rename/merge/clean flows. Preserve multi-disc detection, serial recovery, and playlist logic. Add tests when touching planners.
- **External Tools** – never bundle ROMs/BIOS/keys. Assume user-supplied binaries in `./tools/` (chdman, maxcso, wit, ffmpeg, etc.). Medical Bay should validate their presence.
- **Quit Handling** – ESC/Ctrl+C cancellation is standardized. New commands must respect the cancel token patterns used by extraction/scan/verify.

## Documentation & Releases
- Update `README.md` + `UPDATE.md` whenever user-facing behavior changes (new verbs/options, renamed commands, RC notes).
- `AGENTS.md` captures repo rules for human + AI contributors. Align new guidance with that file.
- Mention Medical Bay (not doctor) in docs, issue replies, and UX text.
- Release steps: push to `rc-upgrade`, open PR to `main`, merge when checks pass, then tag `vX.Y.Z-rc.N`. Stable releases follow later.

## Security & Testing
- Follow the **NO ROM/BIOS/KEY** policy. Keep tooling portable; never write outside the repo folder.
- Tests live in `tests/ARK.Tests`. Target >=70% line coverage for Core changes (justify exceptions). Use AAA naming (`Method_Scenario_Expected`).
- For destructive commands, default to preview mode and provide rollbacks/help text.

## Response Style for Copilot Chat
- Provide complete files or minimal diffs first, then a one-line summary, file paths, UPDATE.md entry, and reasoning/rollback (in that order) per AGENTS.md.
- Highlight branch/test requirements when describing next steps.
- Be explicit about DRY-RUN vs APPLY and note any manual actions the user must take (e.g., rerun Medical Bay, supply external tools).***
