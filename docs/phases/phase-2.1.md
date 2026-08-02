# Phase 2.1 — Qualifier-Aware DAT Matching

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**This is a defect fix, not new scope.** Small and focused. Do not begin the next phase.

---

## The defect

Importing the real No-Intro Love Pack revealed that **only SNES resolved to a system.** N64 and NES did not:

```
snes            Nintendo - Super Nintendo Entertainment System        4,128  ✓
(unrecognized)  Nintendo - Nintendo Entertainment System (Headered)   4,505  ✗
(unrecognized)  Nintendo - Nintendo Entertainment System (Headerless) 4,509  ✗
(unrecognized)  Nintendo - Nintendo 64 (…)                                  ✗
```

SNES's DAT name carries no parenthetical. NES and N64 carry format qualifiers. Alias matching is exact-string, so the qualifier breaks it.

Consequence: two of the three shipped systems have no DAT backing, and Phase 4's **Identified** bucket is empty for them.

---

## Why an alias list is the wrong fix

`(Headered)` and `(Headerless)` are **two distinct hash sets covering the same ~4,500 games.** A headered ROM will never match a headerless DAT. N64 has three variants for byte order.

The model is **one system, N DAT variants keyed by qualifier** — not one system, one DAT. Adding qualified strings to the alias list would make them resolve, then silently merge incompatible hash sets under one system. That is worse than not matching.

---

## The fix

Use the `formatQualifiers` field the Phase 2 system definitions already carry.

**Resolution order:**

1. Exact alias match → system, no qualifier
2. Strip a single trailing parenthetical, match the remainder against aliases:
   - Base matches **and** the parenthetical is in that system's declared `formatQualifiers` → **that system, that qualifier**
   - Base matches but the parenthetical is **not** declared → **unrecognized.** Do not guess.
3. No match → unrecognized

This is deliberately conservative and the negative cases matter as much as the positive ones:

| DAT name | Result | Why |
|---|---|---|
| `Nintendo - Nintendo Entertainment System (Headered)` | `nes` + `Headered` | Declared qualifier |
| `Nintendo - Nintendo 64 (BigEndian)` | `n64` + `BigEndian` | Declared qualifier |
| `Nintendo - Nintendo 64 (Mario no Photopi SmartMedia)` | **unrecognized** | Not a declared qualifier — a subset, not a format |
| `Nintendo - Nintendo 64DD` | **unrecognized** | Different system; base never matches |
| `Nintendo - Super Nintendo Entertainment System` | `snes`, no qualifier | Exact alias |

### Catalog changes

Entries are keyed by **(system, qualifier)**, not by system alone. Lookups must be able to target a specific variant.

Where no qualifier applies, the qualifier is null and behaviour is unchanged.

### Ensure the definitions declare what exists

Verify `nes` declares `Headered` and `Headerless`, and `n64` declares the byte-order qualifiers as they actually appear in No-Intro DAT names. Match the real strings, not assumed ones.

---

## Also: `dat list` is unusable at real scale

The imported catalog holds over a million entries across roughly 200 DATs. `dat list` currently prints every one with full author lists.

Add:
- `--system <code>` — filter to one system
- `--recognized` / `--unrecognized` — filter by resolution state
- Default to a **summary**: system, DAT name, entry count, version. Authors only on `--verbose`.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. `Nintendo - Nintendo Entertainment System (Headered)` resolves to `nes` with qualifier `Headered`
4. `Nintendo - Nintendo Entertainment System (Headerless)` resolves to `nes` with qualifier `Headerless`, as a **separate** entry set
5. N64 byte-order variants resolve to `n64` with their respective qualifiers
6. `Nintendo - Nintendo 64 (Mario no Photopi SmartMedia)` stays **unrecognized** — undeclared parenthetical is never treated as a qualifier
7. `Nintendo - Nintendo 64DD` stays unrecognized
8. Unqualified names still resolve as before — SNES unchanged
9. Catalog lookups can target a specific (system, qualifier) pair
10. `dat list --system nes` shows both variants with distinct counts
11. `dat list` defaults to a summary; authors require `--verbose`
12. Re-import remains idempotent — Phase 2 gate 9 still holds
13. All prior phase gates still pass

Then stop and report.
