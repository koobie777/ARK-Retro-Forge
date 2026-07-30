# Phase 1.5 — Settings Store

**Brief for Claude Code.** `CLAUDE.md` in the repo root is the authority. Read it first.

**Scope discipline:** implement Phase 1.5 and stop. No DAT, naming, scan, hashing, dedup, or policy work.

**Why 1.5 and not 2.** This is an insertion, not a reordering. Renumbering the existing phases in a document treated as authority carries more risk than a decimal.

**Why now.** Phase 1 shipped with `MedicalBayContext.Empty`, so ROM root permanently reports "not set." Phase 4 scan cannot run without a ROM root either. The state has no owner, and it's cross-cutting — CLI reads it, the Avalonia GUI will read it, later phases add to it. That makes it early standalone infrastructure rather than something to bolt onto whichever phase trips over it first.

v1's `SessionStateManager` is **not** the model. It wrote `session.json` directly, and it was PSX-specific. This replaces it and is system-agnostic.

---

## Design decisions, already made

**Writes go through `Executor`. No exemption.**

Settings changes are file writes, so Prohibition 7 applies. Route them through a plan like anything else. Journal clutter from config changes is a real but small cost; carving the first exception into the rule that has already caught two defects is a larger one. If clutter becomes a genuine problem, solve it then — do not pre-empt it with an exception now.

**Reads are pure.** `SettingsStore` reading involves no `Executor` and no mutation. A missing file yields defaults.

**This is the first real use of `WriteText` + `Content`.** That pairing was added in Phase 0 and left uncovered. Exercise it properly here; that closes the gap without contriving a file export elsewhere.

---

## Deliverables

**`Core/Settings/ArkSettings`** — a record. Keep it minimal; do not speculate fields for later phases.

- `SchemaVersion` (int) — present from day one so migration is possible later
- `RomRoot` (string?)
- `ActiveSystem` (string?) — system code, persisted so it survives exit and becomes the default next launch

Policy profiles belong to Phase 8. Do not add them now.

**`Core/Settings/SettingsStore`**

- `Read(InstancePaths)` → `ArkSettings`. Pure. Missing file returns defaults.
- `BuildWritePlan(ArkSettings)` → `Plan` containing a `WriteText` action carrying the serialized settings in `Content`.

Location: `instances/<name>/settings.json`, resolved through `InstancePaths`. No path composed inline.

**Malformed file handling.** A settings file that fails to parse is **reported as an error**, not silently replaced with defaults. Silently resetting a user's configuration is a data-loss pattern. Fail loud.

**Medical Bay** — replace `MedicalBayContext.Empty` with values read from the store. ROM root and active system now report truthfully.

**CLI** — minimal surface:
- `ark config show`
- `ark config set rom-root <path>`
- `ark config set system <code>`

Rendering only. No logic in the command file.

---

## Gate

Report each with evidence.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. Round-trip: write settings, read them back, values identical
4. Write goes through `Executor` and is journaled — this is the first `WriteText`/`Content` coverage, assert `Content` carries the payload and `Destination` carries only a path
5. Read is pure: reading touches nothing and creates no journal
6. Missing settings file returns defaults and does not throw
7. **Malformed settings file surfaces an error and does not silently reset**
8. `ark config set rom-root` then `ark medical-bay` reports the real ROM root, not "not set"
9. No path composed outside the resolver — existing architecture test still passes
10. All Phase 0 and Phase 1 gates still pass

Then stop and report. Phase 2 is DAT infrastructure, pending a source-access question being settled first.
