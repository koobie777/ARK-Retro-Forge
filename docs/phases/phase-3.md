# Phase 3 — Naming Tokenizer

**Brief for Claude Code.** `CLAUDE.md` is the authority; `ARK-FILENAME-VOCABULARY.md` is the evidence base. Read both.

**Scope discipline:** implement Phase 3 and stop. No scan, no hashing, no dedup, no policy. This phase touches no user files at all — it is pure string work with zero I/O.

---

## Why this phase is different

Every prior phase built plumbing. **This is the component that destroyed v1.** The region-stacking, the language tags landing in region slots, the multi-disc and multi-track collisions, the per-title patches that never converged — all of it originated here.

v1's parser matched regexes against whole filenames positionally. `^(.+?)\s*\(([^)]+)\)$` against `Crash Bandicoot (USA) (USA)` captures title `Crash Bandicoot (USA)` and region `USA`; the formatter re-appends and you have three. The same regex against `Tekken 3 (USA) (En,Fr,De)` captures region `En,Fr,De` — **it corrupts a clean No-Intro filename on first contact.**

Prohibition 1 exists because of this. Do not read `legacy/src/Core/Systems/PSX/PsxNameParser.cs`. Its approach is contagious and its details are worthless.

---

## The boundary rule

Derived from 9,928 real filenames across 16 systems, not from assumption:

> **The first recognized region token is the boundary between title and metadata. Everything before it is title — parentheses included. Everything after it is metadata.**

Region sits at index 0 in 9,917 of 9,928 names. All 11 exceptions are titles legitimately containing parentheses:

```
Interactive CD Sampler Pack Volume Three (Version 3.5) (USA) (Rev 1)
Analog Controller Service Disc (Revised) (USA)
4 Games on One Game Pak (Nickelodeon Movies) (USA)
```

`(Version 3.5)` is title. `(Rev 1)` is metadata. Identical in shape, distinguishable only by side of the boundary.

**A name with no recognized region cannot be safely tokenized. Flag it. Do not guess.**

---

## Deliverables

`Core/Naming/`:

| Type | Responsibility |
|---|---|
| `TokenVocabulary` | Closed vocabularies loaded from `config/naming/*.json` — regions, languages, dev status, licensing, distribution, hardware flags, bracket flags. Extensible without recompile, matching the Phase 2 system-registry pattern. |
| `NameTokenizer` | Applies the boundary rule, then classifies tokens. Returns `ParsedName`. |
| `NameFormatter` | Reassembles `ParsedName` into canonical order. |
| `ParsedName` | Title, plus one slot per category, plus the unknown bucket. |

### Canonical order

```
Title (Region) (Languages) (Version|Revision) (DevStatus) (Date) (Disc) (Licensing) (Distribution) (Publisher) (HardwareFlags) [Flags]
```

Each token appears **exactly once**. Always rebuilt from scratch, never appended to.

### Classification order — strictly first-match-wins

1. Split on the region boundary; everything left is title
2. Match remaining tokens against **closed vocabularies**
3. Match against **open pattern classes** — serial, then compilation, then publisher
4. Anything left → **unknown bucket**: preserved, flagged, surfaced

**Vocabulary decides. Position only breaks ties.** `(Japan)` is a region, `(Ja)` a language, `(No)` is Norwegian. `(Virtual Console, Classic Mini, Switch Online)` is comma-space separated and structurally identical to `(USA, Europe)` — only vocabulary separates them.

**Never drop an unknown token.** The unknown bucket is how the vocabulary tables grow.

---

## Corpus — provided, commit it

Three files under `tests/ARK.Tests/corpus/`. They are the input to this phase, not an output of it. Write tests against them **before** implementing.

### `real-names.txt` — 9,363 unique real filenames

From a live 16-system No-Intro/Redump collection. **Invariant only, no hand labels:**

```
parse(format(parse(x))) == parse(x)
```

Decompose, rebuild, decompose again — identical tokens both times. This is idempotency expressed as an executable property, and it makes stacking structurally impossible rather than something you hope you fixed.

Also assert: **for a clean No-Intro name, `format(parse(x)) == x`.** These names are already canonical, so a correct tokenizer is a no-op on them. Any divergence is a bug in one direction or the other.

### `traps.tsv` — 50 hand-selected cases, 16 categories

Each row is a rule the tokenizer must get right, drawn from real data: boundary cases, alphabetic revisions, `Rev 10`/`Rev 11`, locale-suffixed languages, `+`-separated language sets, non-region comma lists, article inversion (including inside tokens), non-game content, licensing, hardware flags, compilations, serials, five-token stacks.

### `dirty.tsv` — 15 synthesized corruptions with expected recovery

**A clean set contains zero of these**, which is exactly why they must be built. Format: `kind → dirty input → expected canonical output`.

Covers stacked regions (2× and 3×), stacked discs, scrambled order, language-before-region, `Disk`/`Disc` spelling, malformed `Rev1`, collapsed whitespace, un-inverted articles, underscore scene renames, GoodTools flags, unknown tokens, and one `FLAG:no-region` case.

Two rows encode decisions worth stating explicitly:

- `[!]` is **stripped** — it asserts a verified dump, which the DAT already tells us
- `[b]` is **preserved** — a bad-dump marker is real information and discarding it loses data

---

## Non-goals

- No filesystem access. This phase reads no files and renames nothing.
- No DAT lookups. The tokenizer *extracts*; whether a token is correct is verification's job.
- No system-specific logic. Naming conventions proved identical across all 16 systems in the corpus.
- No header stripping or byte-order handling. That is Phase 5.

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. Vocabularies load from `config/naming/*.json`; a term added by config alone is recognized with no code change
4. **Round-trip invariant holds for all 9,363 entries in `real-names.txt`**
5. **`format(parse(x)) == x` for all clean corpus entries** — a correct tokenizer is a no-op on canonical input
6. All 50 `traps.tsv` cases classify correctly
7. All 15 `dirty.tsv` cases produce their expected canonical output
8. `Tekken 3 (USA) (En,Fr,De)` → region `USA`, languages `En,Fr,De`. The v1 regression, asserted by name.
9. `Crash Bandicoot (USA) (USA) (USA)` → single region token
10. Titles containing parentheses survive: `(Version 3.5)` before the boundary stays in the title while `(Rev 1)` after it is a revision
11. A name with no recognized region is flagged, not guessed
12. Unknown tokens are preserved and retrievable, never silently dropped
13. `Rev A` and `Rev 10` both parse; `Rev 10` sorts above `Rev 9`; numeric and alphabetic schemes never compare against each other
14. No I/O anywhere in `Core/Naming` — assert by architecture test
15. All Phase 0/1/1.5/2 gates still pass

Then stop and report. Phase 4 is scan and game-unit resolution.

---

## If a case is genuinely ambiguous

Stop and ask. Do not resolve it by adding a rule that names a specific game — Prohibition 2. If a fix under consideration contains a title, the rule is wrong, not the title.
