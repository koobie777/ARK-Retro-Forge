# Phase 1 — Medical Bay

**Brief for Claude Code.** `CLAUDE.md` in the repo root is the authority. Read it first.

**Scope discipline:** implement Phase 1 and stop. No naming, DAT sync, scan, hashing, dedup, or policy work. Report at the gate and wait.

**Purpose of this phase.** Medical Bay's logic already works — it is trapped in `legacy/src/Cli/Program.cs` as a private static method, unreachable from any other frontend. Lifting it is low-risk, which is exactly why it goes first: it establishes the Core/UI boundary shape that every later component copies.

---

## What already lives in Core

Read `legacy/src/Core/Tools/` and `legacy/src/Cli/Program.cs` (`RunMedicalBayAsync`, around line 109).

The data gathering is **already** in Core and is sound:

- `ToolManager.CheckAllTools()` → `IEnumerable<ToolCheckResult>`
- `DatStatusReporter.Inspect()` → `IEnumerable<DatStatusSummary>`
- `SessionStateManager.State`, `SystemProfiles.Resolve()`

What sits wrongly in `Program.cs` is only the **orchestration and the exit-code decision**. Rendering, prompting, and markup are legitimately CLI concerns and stay in Cli. This is a smaller extraction than it first appears — do not inflate it.

---

## Defects found in v1 during review — fix these as part of the extraction

### 1. `--json` returns a different, smaller report

In v1 the `--json` path short-circuits before DAT status, session state, and system profile are ever gathered. It serializes only the tool-check array. So the machine-readable output and the human-readable output describe different things.

**Fix:** `MedicalBayReport` is the single source. The human render and the JSON output are two serializations of one object. Neither may contain a field the other cannot see.

### 2. The required/optional distinction is dead code

Every one of the six tools in `ToolManager`'s registry is declared `IsOptional = true`. Therefore `missingRequired` is always empty, and the "Action Items" panel plus the `ExitCode.ToolMissing` branch **can never execute.**

**Fix:** delete the unreachable branch. Then note the deeper issue — tool requirements are **per-operation, not global.** `chdman` is required for CHD conversion and irrelevant to NES deduplication. Medical Bay reports *status*; whether a given absence is fatal is the calling operation's judgement.

`MedicalBayReport` therefore carries per-tool status and no global pass/fail verdict. Operations declare their own tool dependencies in later phases.

### 3. `ToolManager` only searches `tools/`, and composes its own paths

`_toolsDirectory` defaults to `Path.Combine(AppContext.BaseDirectory, "tools")` and lookup is a bare `Path.Combine(_toolsDirectory, tool.ExecutableName)`. Consequences:

- A tool installed system-wide and present on `PATH` is reported missing
- `ExecutableName` is used verbatim, so extension handling is wrong cross-platform — this matters, the GUI target is Avalonia
- It composes its own paths, contradicting the Phase 0 rule that nothing composes paths outside the path resolver

**Fix:** search `tools/` first, then `PATH`. Resolve platform-appropriate executable names (`.exe` on Windows, bare elsewhere). Take the tools root from the path resolver rather than building it inline. Tools are shared across instances, not per-instance — extend the resolver accordingly rather than reaching around it.

---

## Deliverables

**`Core/Diagnostics/MedicalBayService.cs`** — returns `MedicalBayReport`. Pure query: it reads, it does not mutate. No `Executor` involvement, no plan.

**`MedicalBayReport`** — one object carrying:
- Instance name
- ROM root (and whether it is set)
- Active system profile
- Per-tool status: name, found, path, version, minimum version, meets-minimum, error
- Per-system DAT catalog summaries: has catalog, stale, local file count, last updated

**Port forward** `ToolManager`, `ExternalTool`, `ToolCheckResult` — with the fixes above. Audit the six-tool registry while you are in there and report what it contains; the cartridge-first scope may need none of them, which is worth knowing explicitly.

**CLI** — `ark medical-bay` and `ark medical-bay --json`. Rendering, tables, panels, and any interactive DAT prompt stay here. The command file contains no logic and no decisions derived from report contents beyond presentation.

---

## Note on scope honesty

v2 is cartridge-first, and cartridge work needs no external tools — archive reading and hashing are both in-process. Medical Bay may well report an empty relevant-tool set right now. That is fine and should not be disguised. The value of this phase is the boundary pattern, not the diagnostics.

Also: `WriteText`/`Content` remains untested after this phase. `--json` writes to stdout, which is the correct composable design and involves no file write. Do not contrive a file export to manufacture coverage — add a small standalone `WriteText` unit test instead, whenever convenient.

---

## Gate

Report each with evidence.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. `MedicalBayService` unit test passes **with no console attached** — no `AnsiConsole`, no `System.Console`
4. Field-parity test: every field in the human render is present in `--json` output, and vice versa
5. `ToolManager` finds a tool present in `tools/`
6. `ToolManager` finds a tool present on `PATH` but absent from `tools/`
7. Platform-appropriate executable resolution is tested
8. No path composed outside the path resolver — extend the Phase 0 architecture test if it does not already cover this
9. The unreachable `ToolMissing` branch is gone; `MedicalBayReport` exposes no global pass/fail verdict
10. `ark medical-bay` CLI command file contains no logic
11. All nine Phase 0 gates still pass

Then stop and report. Phase 2 is DAT infrastructure.
