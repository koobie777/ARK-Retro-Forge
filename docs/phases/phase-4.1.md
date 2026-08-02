# Phase 4.1 — Scoped Identification + Unsupported Formats

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**Defect fix, not new scope.** Both defects were found by running `ark scan` against a real drive, not by tests.

---

## Defect 1 — Identification is not scoped to a DAT

`ark scan` matches names against the entire catalog — 1.5M entries across 334 DATs — regardless of which DAT an entry belongs to or whether it relates to the directory being scanned.

Evidence from a real drive:

| Directory | Identified | Candidate | Note |
|---|---|---|---|
| `GB\Nintendo - Game Boy` | 591 | 1 | **`gb` is not a defined system.** Matching happened anyway. |
| `PSX\...\Redump\Sony - PlayStation` | 308 | 1,457 | Redump set. **The Redump PSX DAT is not imported.** |
| `PS2\...\Redump\Sony - PlayStation 2` | 27 | 113 | Same. |

The PSX matches are the dangerous ones. With no Redump DAT present, those 308 files matched entries from a *different* DAT — `Non-Redump - Sony - PlayStation` or `Sony - PlayStation (PS one Classics) (PSN)`. A Redump disc image and a PSN re-release share a title and are entirely different artifacts with different hashes.

**Consequence if unfixed:** Phase 5 hashes those 308 against the wrong DAT entry, they all fail, and they report **Mismatched** — false corruption reports on healthy files. That is the alarm-fatigue failure the five-state model exists to prevent, arriving through the front door.

This also violates Prohibition 6. A cross-DAT name collision is a guess wearing a confident label.

### Fix — resolve the directory to a DAT, then match within it

ROM-set directory names are byte-identical to DAT names on real collections:

```
folder  GB\Nintendo - Game Boy
DAT     Nintendo - Game Boy

folder  N64\No-Intro\Nintendo - Nintendo 64 (BigEndian)
DAT     Nintendo - Nintendo 64 (BigEndian)
```

Resolution order per ROM-set directory:

1. Directory name matches a DAT name in the catalog → **identify only within that DAT's entries**
2. Directory resolves to a known system + qualifier (Phase 2.1) → identify within that (system, qualifier) DAT
3. Neither → **every file is Candidate.** Do not fall back to a catalog-wide search.

Report the resolved DAT in the scan output, so a user can see what their files were compared against — and see when nothing was.

**This is not a downgrade.** Directory-to-DAT matching identifies Game Boy correctly without `gb` ever being defined as a system, while making PSX honest: no Redump DAT imported means no identification, which is the true answer.

---

## Defect 2 — Correct disc structure reported as an anomaly

1,762 of 1,765 PSX archives contain a `.bin` + `.cue` pair and are reported under `multiple-roms-in-archive`.

That is the correct, expected structure of a PSX disc image. `CartridgeUnitResolver` is right to refuse it; the classification is wrong. "Anomaly" means *this is malformed*. The truth is *this resolver does not handle this format yet* — the disc resolver is still stubbed.

An anomaly list where 99.8% of entries are normal files is a list nobody reads.

### Fix — separate the two

| Class | Meaning | Example |
|---|---|---|
| **Unsupported format** | Correct structure, no resolver for it yet | `.bin` + `.cue` in an archive |
| **Anomaly** | Genuinely malformed or unexpected | Unreadable archive; two distinct games in one archive |

Unsupported-format units report once with a count and the reason, not one line per file. They are not defects in the collection and must not read as such.

**Keep `unreadable-archive` as a true anomaly.** It found five real corrupt or truncated downloads on the reference drive, which is exactly the value scan should deliver.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. A ROM-set directory whose name matches a DAT identifies **only** within that DAT
4. A ROM-set directory with no resolvable DAT reports **all files as Candidate** — no catalog-wide fallback
5. `scan F:\GB` still identifies ~591 via directory-to-DAT matching, with `gb` undefined as a system
6. `scan F:\PSX` reports **0 Identified** while no Redump PSX DAT is imported
7. Scan output names the DAT each ROM-set directory was matched against, or states that none was
8. A `.bin` + `.cue` archive classifies as **unsupported format**, not anomaly
9. Unsupported-format units are summarized by count, not listed per file
10. `unreadable-archive` remains an anomaly and still surfaces the five known corrupt archives
11. Anomaly output is not dominated by correctly-structured files
12. All prior phase gates still pass

Then stop and report. Phase 5 is hashing and verification.
