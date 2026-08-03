# Phase 8 — Variant Policy Engine

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first, along with `ARK-FILENAME-VOCABULARY.md` — the token categories below are defined there with real occurrence counts.

**Scope discipline:** implement Phase 8 and stop. No collection reports (8.5), no GUI.

---

## Variants are not duplicates

Phase 7 collapsed byte-identical units. Everything here has **different bytes** and is therefore a legitimate, distinct release. Removing any of it is a **curation preference**, never a redundancy claim.

That difference sets the standard: dedup could argue a removed file was recoverable from its twin. Nothing here can. Every removal is a real loss of a real release, so quarantine, journaling, undo, and DRY-RUN all apply exactly as in Phase 7 — and report-only is again the shipped default.

---

## Curation is multi-axis, not a single ranking

The central design decision, and the one most likely to be got wrong: **there is no single "best" copy.** A user's preferences are independent per axis, and collapsing them into one ranking produces silent, wrong deletions.

| Axis | Values | Independent question |
|---|---|---|
| **Revision** | Rev 0 (untagged) · Rev 1..N · Rev A..Z | Keep latest, keep all, keep a specific one |
| **Version** | `v1.0`, `v1.1`, `(Version 3.5)` | Separate scheme from revision — never merged |
| **Development status** | Retail (untagged) · Alpha · Beta · Proto · Sample · Demo · Kiosk · Preview · Debug | Keep retail only, keep all, keep retail + protos |
| **Region** | USA · Europe · Japan · World · lists | Keep one region, keep several, keep all |
| **Licensing** | Licensed (untagged) · Unl · Aftermarket · Pirate · Homebrew | **Separate axis from dev status** |
| **Distribution** | Original (untagged) · Virtual Console · e-Reader · Switch Online · LodgeNet · Arcade | Keep original, keep all |
| **Language** | Language token sets | Rarely a curation axis; expose it, default to ignoring it |

**"Keep retail only" must not silently remove 390 unlicensed titles.** On the reference corpus, development status covers 245 files and licensing covers 390 — different files, different intent. A user who wants no prototypes has said nothing about homebrew.

**Hardware flags are never a curation axis.** `(SGB Enhanced)`, `(GB Compatible)`, `(NDSi Enhanced)` describe cartridge capability, not release lineage. They must not participate in grouping or ranking at all.

---

## Grouping

A variant group is the set of releases of **the same game**. Grouping key comes from `ParsedName` **token sets, never formatted strings** — canonical order is partial, so two equivalent names can format differently. See the naming section of `CLAUDE.md`.

The key is the **base title** plus whichever axes the policy treats as *identity* rather than *variance*. If region is an identity axis, `Game (USA)` and `Game (Japan)` are different games. If it's a variance axis, they're two variants of one game. **This is a policy choice, not a fixed rule**, and it must be explicit in the report — the user has to see which grouping produced a given decision.

**Non-game content is excluded from variant grouping entirely.** `(DLC)` 171, `(Save Data)` 14, `(Bonus Disc)` 5, `(Test Program)` 9, `(Tech Demo)` 3 — 202 files on the reference corpus. A Bonus Disc is not a revision of the game it shipped beside; comparing it against retail is a category error.

---

## Ordering, and where it must refuse

| Rule | Detail |
|---|---|
| **Absent rev tag = Rev 0 = the OLDEST** | The likeliest bug in this phase. "Keep latest" that silently keeps the untagged original is exactly backwards. Untagged is the *original release*. |
| **`Rev 10` > `Rev 9`** | Parse numerically. String sort places `Rev 10` between `Rev 1` and `Rev 2`. Both exist in the reference corpus. |
| **`v1.10` > `v1.9`** | Same trap, version scheme. Parse per segment. |
| **Numeric and alphabetic revisions never compare** | `Rev A` and `Rev 1` are separate schemes. A title carrying both is **flagged, not ordered**. Rev A/B/D all appear in real data. |
| **Retail outranks every pre-release build** | Unambiguous, safe to enforce. |
| **Pre-release builds have NO reliable ordering** | Alpha/Beta/Proto do not map to a consistent timeline across publishers. Use an attached date tag when present; **otherwise refuse and ask.** Never guess a sequence. |

**Any group the policy cannot order is reported and skipped**, exactly as dedup handles ties. Prohibition 6: a guess wearing a confident label is worse than an unanswered question.

---

## Policies are data

Saved to the instance settings, selectable, and shipped as presets — not hardcoded rules. A preservationist and a casual player have opposite correct answers.

Presets to ship:

- **Report only** — *default.* Groups and rankings shown; nothing is chosen, nothing moves.
- **1G1R (one game, one ROM)** — the common ask: one region, latest revision, retail only.
- **Retail only** — drop pre-release, leave every other axis alone.
- **Latest revision** — collapse revisions, leave every other axis alone.
- **Everything** — no removal; used to drive Phase 8.5 reports without curating.

Plus **custom**: per-axis rules the user composes. Each preset is expressible as a set of per-axis rules — no preset is special-cased in code.

---

## Two different operations

Distinguish them clearly; they have different risk profiles:

- **Quarantine** — removal. Full Phase 7 machinery: manifest, journal, same-volume, undo.
- **Sort into subfolders** — organization. Files move but nothing leaves the collection. Still journaled and reversible, but it is not a deletion and must not be presented as one.

Both are DRY-RUN by default. Both refuse directories showing active-download signals.

---

## Gate

Every APPLY gate also asserts `ark undo` restores the set exactly.

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. **Untagged release ranks as Rev 0 and is the oldest** — "keep latest revision" keeps `(Rev 1)` over the untagged one
4. `Rev 10` ranks above `Rev 9`; `v1.10` above `v1.9`
5. `Rev A` and `Rev 1` in one group is **flagged, not ordered**
6. Retail outranks Alpha, Beta, Proto, Sample, Demo
7. **Two pre-release builds with no date tag are refused, not ordered**
8. A group the policy cannot order is reported and skipped
9. Licensing and development status are independent — "keep retail only" leaves `(Unl)` titles untouched
10. Hardware flags never affect grouping or ranking
11. Non-game content is excluded from variant grouping
12. Grouping uses `ParsedName` token sets, not formatted strings
13. The report states which axes were treated as identity vs variance
14. Presets are expressed as per-axis rules; none is special-cased
15. Policies round-trip through settings
16. Report-only is the default; a first run with no flags moves nothing
17. Nothing moves without `--apply`
18. Sort-into-subfolders is journaled and reversible, and is not reported as removal
19. Quarantine writes a manifest and is journaled
20. `ark undo` after an APPLY restores the set byte-for-byte
21. An interrupted APPLY reverses exactly what completed
22. Directories showing active-download signals are refused for APPLY while still being reported
23. **A quarantine directory beneath a scanned root is excluded from scanning**, with a stated reason
24. All prior phase gates still pass

Then stop and report. Phase 8.5 is collection reports.

---

## Carry-over from Phase 7

Item 1 is a defect and comes first. The rest are improvements.

1. **The quarantine directory must be excluded from scanning.** Quarantine lands at `<root>/.ark-quarantine/<session>/`, inside the tree that gets scanned. A subsequent `ark scan` walks into it and finds conformant archives in a directory scoring high on both classifier signals — so quarantined units re-enter the pipeline as live candidates, and a later dedup run could select a quarantined copy as the one to *keep*. Same shape as the journaled-leaf defect: an operation's output leaking into the next operation's input.

   Verify whether `scan-rules.json` already excludes it. If not, add it, and add a test asserting a quarantine directory beneath a scanned root is excluded with a stated reason.

2. **Make the recent-write window configurable** through settings rather than a constructor default. It is the weakest In-Progress signal and the only circumstantial one.

3. **Surface recent-write exclusions actionably.** A run that skips everything because the files are minutes old currently reads as "no duplicates found" with the explanation buried. Say what to do: re-run once the set has settled.
