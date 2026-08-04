# AGENTS.md

**The authority for this repository is [CLAUDE.md](CLAUDE.md). Read it before writing a line.**

This file previously described ARK v1 — `src/Core`, `ArkStaging`, `src/Cli/ARK.Cli.csproj`, per-system PSX command modules, a `dev` → `main` branch flow. None of that exists any more. v2 is a ground-up rebuild and the old guidance was actively misleading, so it has been removed rather than updated. v1 survives under `legacy/`, mined for data, not code.

There is one authority, not two. Everything below is a pointer.

## Where things are

| Path | Contents |
|---|---|
| `src/ARK.Core` | All logic. Parsing, hashing, DAT, policy, planning, execution. |
| `src/ARK.Cli` | Verb and flag parsing, and rendering Core's results. Nothing else. |
| `tests/ARK.Tests` | xUnit, including architecture tests that enforce the rules structurally. |
| `config/` | Shipped configuration: naming vocabulary, scan rules, DAT sources. |
| `docs/` | [USAGE.md](docs/USAGE.md), [FIRST-RUN.md](docs/FIRST-RUN.md), and the phase briefs under `docs/phases/`. |
| `legacy/` | v1. Reference only. |

## Commands

```
dotnet build          # warnings are errors
dotnet test
dotnet run --project src/ARK.Cli -- <verb> [args]
```

## The rules that are not negotiable

Stated in full in [CLAUDE.md](CLAUDE.md), and several are enforced by `tests/ARK.Tests/ArchitectureTests.cs`. In short:

- **Only `Executor` touches the filesystem.** Operations return a `Plan`; the executor acts and journals. This is what makes DRY-RUN, quarantine, and undo structural rather than a matter of discipline.
- **Never delete — quarantine**, with a manifest, on the same volume.
- **Never guess identity.** Confident match, ranked candidates, or skip. A skipped file always beats a wrongly-renamed one.
- **Never operate on a file inside a game unit.** Operations take whole `GameUnit`s, and multi-disc sets move whole or not at all.
- **Never regex a whole filename positionally.** Tokenize against the vocabulary.
- **Never special-case a title.** If a proposed fix names a specific game, the fix is wrong and the underlying rule is what needs changing.
- **Never put logic in the CLI layer.** If a command file passes 300 lines, logic has leaked.
- **Never call `SharpCompress.IArchive.WriteToDirectory()`** — unpatched zip-slip (GHSA-6c8g-7p36-r338). An architecture test enforces this.

## Working on real data

Do not run write operations against the owner's collection. Use a copy, and follow [docs/FIRST-RUN.md](docs/FIRST-RUN.md) — it is written for exactly this. Hand review against real data is part of the Definition of Done, and every phase so far has surfaced something synthetic fixtures did not.
