# Phase 0 — Skeleton

**Brief for Claude Code.** `CLAUDE.md` in the repo root is the authority. Read it first. This document covers only what is specific to Phase 0.

**Scope discipline:** implement Phase 0 and stop. Do not begin Phase 1. Do not write naming, DAT, scan, hashing, dedup, or policy code. Prohibition 9 applies — report at the gate and wait.

---

## 1. Branch

Confirm the working branch is `v2`. Create it off `main` if it doesn't exist. `main` must remain untouched at v1.1.0. Set a version prefix on `v2` so `ark --version` reports `2.0.0-alpha.x` rather than inheriting MinVer's `1.1.x`.

## 2. Archive v1

Move the existing v1 tree aside. It is reference material — **mined for data, never for logic.**

- `src/` → `legacy/src/`
- `tests/` → `legacy/tests/`
- `ARK-Retro-Forge.sln` → `legacy/`
- Leave `config/`, `plugins/`, `tools/`, and all root markdown in place

Nothing in `legacy/` is referenced by the new solution.

## 3. Solution and projects

Fresh solution at the repo root. Three projects, all `net8.0`:

| Project | Path | Type |
|---|---|---|
| `ARK.Core` | `src/ARK.Core` | classlib |
| `ARK.Cli` | `src/ARK.Cli` | console |
| `ARK.Tests` | `tests/ARK.Tests` | xunit |

References: `ARK.Cli` → `ARK.Core`. `ARK.Tests` → `ARK.Core`.

Packages, matching versions already proven in v1:

- **Core** — Serilog 4.3.0, Serilog.Sinks.Console 6.1.1, Serilog.Sinks.File 7.0.0, Microsoft.Data.Sqlite 10.0.0, Dapper 2.1.66, SharpCompress 0.41.0, Polly 8.6.4
- **Cli** — Serilog 4.3.0, System.CommandLine 2.0.0, Spectre.Console 0.49.1
- **Tests** — xunit 2.5.3, xunit.runner.visualstudio 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0

Spectre.Console is retained in Cli **for rendering only** — tables, progress, colour. The v1 interactive menu is not being rebuilt. Core must never reference it.

## 4. Port from legacy

Copy these into `ARK.Core`, namespaces updated, logic unchanged:

- `Crc32`
- `FileHasher`
- `HashOptions`
- `HashResult`

Add an architecture test asserting `WriteToDirectory` appears nowhere in `src/` — see Prohibition 11. No patched SharpCompress exists, so this test is the mitigation.

Copy `config/dat/dat-sources.json` forward as-is. Its contents are corrected in Phase 2, not now.

**Do not port** `PsxNameParser`, `PsxNameFormatter`, `PsxRenamePlanner`, `CleanPsxCommand`, or `Program.cs`. See Prohibition 1 — the parser's failure is architectural, and reading it as a starting point risks reproducing it.

## 5. Instance path resolution

`Core/Instances/InstancePaths.cs`. Given an instance name, resolves and creates on demand:

```
instances/<name>/db/
instances/<name>/dat/
instances/<name>/logs/
instances/<name>/journal/
instances/<name>/quarantine/
```

Default instance name: `default`. Root is resolved relative to the executable, not the working directory — ARK is portable and must behave identically regardless of where it's invoked from.

**Resolution only.** `InstancePaths` does not create directories — Prohibition 7 reserves that for `Executor`. It exposes `BuildProvisionPlan()`, returning a `CreateDirectory` plan the `Executor` applies, so even bootstrap appears in DRY-RUN and lands in the journal.

The journal directory is itself part of the tree being provisioned. Document how that cycle is broken — either bootstrap provisioning is exempt from journaling, or the journal directory is created ahead of the plan. Do not leave it implicit.

Every later component takes its paths from this type. Nothing composes paths itself.

## 6. Logging

Serilog configured in Core, wired from the first commit. Console sink plus a rolling file sink under the instance's `logs/`. Core services log through `ILogger`; they never write to `Console`.

## 7. Plan and Executor

The spine of the tool. See the Plan → Execute → Journal section of `CLAUDE.md`.

**Model:**

```
enum ActionKind { Move, Rename, Quarantine, CreateDirectory, WriteText }

record PlannedAction(
    ActionKind Kind,
    string     Source,
    string?    Destination,
    string     Reason,
    string?    Content = null);

record Plan(
    string                       SessionId,
    DateTimeOffset               CreatedUtc,
    string                       Operation,
    IReadOnlyList<PlannedAction> Actions);
```

`Content` carries payload for `WriteText` only. Never overload `Destination` to hold content.

**Executor contract:**

```
ExecutionResult Execute(Plan plan, bool apply)
```

- `apply: false` — returns the full action list with per-action feasibility. **Touches nothing.**
- `apply: true` — executes in order, appending each completed action to the journal **immediately after it succeeds**, then returns the result.

Incremental journal writes are mandatory. A process killed halfway through must leave a journal that reverses exactly the actions that completed. Batching the write to the end makes partial runs unrecoverable.

Journal path: `instances/<name>/journal/<sessionId>.json`

`Executor` is the **only** type in the entire solution permitted to call `File.Move`, `File.Delete`, `File.WriteAllText`, or `Directory.CreateDirectory`. Add an architecture test asserting this.

## 8. CLI surface

`ark --version` and `ark --help` only. `Program.cs` is wiring and rendering — no logic. If it approaches 150 lines in Phase 0, something belongs in Core.

---

## Gate

Report each of these with evidence. Do not proceed past a failure.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. `ark --version` returns a version
4. `ARK.Core.csproj` references no UI package
5. Architecture test passes: no filesystem-mutating call outside `Executor`
6. `InstancePaths` unit test: creates the full tree for a named instance, resolves relative to the executable
7. **Executor test, dry run** — a 3-action plan against a temp directory with `apply: false` leaves the directory byte-identical and writes no journal
8. **Executor test, apply** — the same plan with `apply: true` performs all 3 actions and writes a journal containing all 3
9. **Executor test, partial failure** — a plan whose second action fails leaves a journal containing exactly one completed action

Then stop and report. Phase 1 is Medical Bay extraction and begins only after this gate is signed off.
