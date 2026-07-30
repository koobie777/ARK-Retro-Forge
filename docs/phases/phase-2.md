# Phase 2 — System Definitions + DAT Infrastructure

**Brief for Claude Code.** `CLAUDE.md` in the repo root is the authority. Read it first.

**Scope discipline:** implement Phase 2 and stop. No naming/tokenizer work, no scan, no hashing of user files, no dedup, no policy.

**This is the largest phase so far.** The two halves are coupled — a system definition includes which DAT sources cover it, and a DAT cannot be filed without knowing what system it belongs to. Splitting them would mean building one against a stub of the other.

---

## Part A — System Definitions

### The defect being fixed

v1's `SystemProfiles` is a PlayStation-only registry that **silently falls back to `psx`** on an unrecognized code. Phase 1.5 exposed it: `config set system n64` persists correctly, then Medical Bay reports "Sony PlayStation (psx)." A wrong answer delivered confidently is worse than an error.

**Unknown codes report as unrecognized. Never substituted, never defaulted.**

### Definitions are data, not code

The registry loads from `config/systems/*.json`. Adding a system is editing a file, not recompiling. This matters twice over: the reference drive holds 16 systems while v2's scope is 3, and users will want systems ARK doesn't ship.

Schema per system:

| Field | Purpose |
|---|---|
| `code` | Short identifier — `n64`, `snes`, `nes` |
| `displayName` | "Nintendo 64" |
| `aliases` | Folder and input matching — `"Nintendo - Nintendo 64"`, `"N64"` |
| `romExtensions` | `.z64`, `.n64`, `.v64` |
| `archiveExtensions` | `.zip`, `.7z` — reference collections are distributed archived |
| `formatQualifiers` | `BigEndian` / `ByteSwapped` / `LittleEndian`, `Headered` / `Headerless` |
| `datSources` | Which DAT sources cover this system |

### Define three systems now

N64, SNES, NES — the v2 cartridge scope. Do not add the other thirteen; leave that to config once the schema is proven.

### Format qualifiers matter for matching

A headered ROM will not match a headerless DAT, and vice versa. This is a common source of "nothing verifies and I don't know why." The reference collection's folders name their qualifiers explicitly — `Nintendo 64 (BigEndian)`, `Nintendo Entertainment System (Headered)` — so the qualifier must be part of the definition and must participate in choosing which DAT variant to match against.

Record the relationship. Do not implement header stripping or byte-order normalization in this phase; that belongs with hashing.

---

## Part B — DAT Infrastructure

### No-Intro is import-only

Datomatic requires a session-cookie GET/POST flow ending in a "Prepare" step before a file exists. The established tool for automating it requires Firefox and geckodriver — a mature project in this space concluded it needs a real browser. Bundling one contradicts the portable single-EXE promise, and a scraper that breaks silently on a form change is worse than none.

- **`ark dat import <path>`** — primary path for No-Intro. Accepts a single `.dat`, an archive of DATs (Datomatic's "Daily" pack), or a directory.
- **`ark dat sync`** — automated, for sources with direct URLs. Redump's 77 existing entries qualify.
- **`ark dat list`** — catalog coverage per system.

### Parsing

Both No-Intro and Redump publish Logiqx XML. Parse the header (`name`, `description`, `version`, `date`) for staleness reporting — Medical Bay already surfaces stale catalogs and should now have real data behind it.

### Indexing

SQLite, per instance, via `InstancePaths`. Index by CRC32, MD5, SHA1, size, and name.

- `FindByHash` — exact. One result or none.
- `FindByName` — ranked candidates. Keep ranking simple here: exact, then normalized, then partial. Confidence scoring for identification comes with the phase that identifies files.

### Import is idempotent

Importing the same DAT twice leaves the catalog in the same state as importing it once. Re-import updates in place rather than duplicating. This is the same rule as everywhere else in ARK.

---

## Test fixtures

Unit tests use **synthetic Logiqx XML committed to the repo** — fast, deterministic, no network, no dependency on a third party's uptime. Cover both the No-Intro and Redump variants, including entries with multiple hash types and entries missing some.

Real-world validation against a Datomatic Daily pack is a separate manual check, not a test dependency.

---

## Gate

Report each with evidence.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. System registry loads from `config/systems/*.json`; a synthetic system added by config alone is resolvable with no code change
4. **Unknown system code reports unrecognized — no fallback, no substitution.** Assert `config set system xyz` then `medical-bay` does not report psx
5. N64, SNES, NES defined with extensions, archive extensions, aliases, and format qualifiers
6. Logiqx parse, No-Intro variant: entry count, names, and all hash types correct against a committed fixture
7. Logiqx parse, Redump variant: same
8. Header metadata parsed and surfaced for staleness
9. **Import idempotency:** importing the same DAT twice yields identical catalog state
10. `FindByHash` returns the correct single entry; a miss returns none rather than a guess
11. `FindByName` returns ranked candidates
12. `ark dat import` ingests an archive containing multiple DATs
13. `ark dat sync` fetches a Redump source; second run hits cache without refetching
14. Medical Bay reports real catalog coverage per system
15. No path composed outside the resolver
16. All Phase 0, 1, and 1.5 gates still pass

Then stop and report. Phase 3 is the naming tokenizer.

---

## Also in this phase

Fix the Phase 1.5 carry-over: unrecognized system codes must not resolve to psx. Gate 4 covers it, but it is a defect fix rather than new work and should be treated as such.
