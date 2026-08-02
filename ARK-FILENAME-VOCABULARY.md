# ARK Retro Forge — Filename Token Vocabulary

> **Evidence-based.** Every rule and count below is derived from a real 9,928-file No-Intro/Redump collection spanning 16 systems (NES, SNES, N64, GB, GBC, GBA, NDS, 3DS, GameCube, Wii, Wii U, PSX, PS2, PS3, PSP). Counts are occurrences in that corpus.
>
> An earlier version of this document was written from assumption. It classified **83%** of real files correctly and missed 220 distinct tokens across five whole categories. This version is corrected against ground truth.

---

## THE BOUNDARY RULE

**The first recognized region token is the boundary between title and metadata.**

- Everything **before** it is title — parentheses included.
- Everything **after** it is metadata.

Evidence: region sits at token index 0 in 9,917 of 9,928 names. All 11 exceptions are titles that legitimately contain parentheses:

```
4 Games on One Game Pak (Nickelodeon Movies) (USA)
Diamond Trust of London (Jason Rohrer with Music by Tom Bailey) (USA)
My Boyfriend (Summer of My Life) (USA) (En,Fr,De,Es,It)
Analog Controller Service Disc (Revised) (USA)
Interactive CD Sampler Pack Volume Three (Version 3.5) (USA) (Rev 1)
```

That last pair is the whole reason this rule exists. `(Version 3.5)` is **title**. `(Rev 1)` is **metadata**. They are indistinguishable by shape and distinguishable only by side of the region boundary.

**Corollary:** a name with no recognized region cannot be safely tokenized. Flag it, do not guess.

---

## Canonical Output Order

```
Title (Region) (Languages) (Version|Revision) (DevStatus) (Date) (Disc) (Licensing) (Distribution) (Publisher) (HardwareFlags) [Flags]
```

Each token appears **exactly once**. Rebuilt from scratch on every write, never appended to.

### The order above is a partial order, not a total one

Measured across all 9,363 names: **no single total order reproduces the corpus.** 29 names are mutually contradictory, because No-Intro itself is internally inconsistent on which token comes first:

| Pair | One way | The other |
|---|---|---|
| Licensing / Version | `(Unl) (v2.35)` — 12 | `(v1.1) (Unl)` — 54 |
| Date / DevStatus | `(1994-08-09) (Proto)` — 2 | `(Proto 1) (1993-10-11)` — 36 |
| Revision / DevStatus | `(Rev 1) (Sample)` — 4 | the reverse — 4 |
| Licensing / Revision | `(Unl) (Rev 1)` — 3 | the reverse — 6 |

Imposing one order would rewrite 29 correctly-named files into names their own DAT entry no longer matches — and every other ROM manager would then report them as misnamed. So order is stored as **rank groups**, computed as the strongly-connected components of the observed precedence graph:

```
0: Region   1: Language   2: Compilation   3: Disc
4: Version Revision DevStatus Date Edition Licensing Distribution Publisher Serial Hardware Unknown
5: NonGame
```

Categories in **different** groups have every real observation agreeing, so the order is real information and is enforced. Categories in the **same** group are mutually unorderable, so a **stable** sort preserves whatever order the input had. This reproduces all 9,363 names exactly while still normalizing everything provably wrong — stacked duplicates, metadata stranded before the region boundary, `Disk`/`Disc`, `Rev1`.

**Normalize what is wrong; preserve what is merely different.**

---

## Closed Vocabularies

Match these first. Membership is decidable.

### 1. Regions

Asia, Australia, Brazil, Canada, China, Denmark, Europe, Finland, France, Germany,
Greece, Hong Kong, India, Israel, Italy, Japan, Korea, Latin America, Mexico,
Netherlands, New Zealand, Norway, Poland, Portugal, Russia, Scandinavia, South Africa,
Spain, Sweden, Switzerland, Taiwan, Turkey, UAE, UK, USA, World, Unknown

Observed: `(USA)` 8782 · `(USA, Europe)` 772 · `(Europe)` 180 · `(Japan)` 129 · `(USA, Australia)` 22 · `(USA, Canada)` 15

- A **list**, separated by comma + space: `(USA, Europe)`
- `(World)` means all regions — never merge into a list
- `(Unknown)` is explicit and distinct from a missing tag

### 2. Languages

**Not two-character codes.** Real forms observed:

| Form | Example | Count |
|---|---|---|
| Plain codes | `(En,Fr,Es)` | 815 |
| Long list | `(En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Pl)` | 50 |
| Locale variants | `(En-US,En-GB,Fr,De,Es,It)` · `(En,Fr,Es-XL,Pt-BR)` | 2 |
| **`+` separated sets** | `(En,Fr,De+En)` · `(En,Ja,Fr,De,Es+En,Ja,Fr,De,Es,It)` | 4 |

Base codes: En Ja Fr De Es It Nl Pt Sv No Da Fi Zh Ko Pl Ru Cs Hu El Tr Ar He Ca Hr Sl Sk Ro Bg Uk Th Vi Id Ms Hi Ga Gd Cy Eu Gl Af Sq Et Lv Lt Fa

- Separator is comma with **no space** — contrast regions, which use comma + space
- A code may carry a locale suffix: `En-US`, `Es-XL`, `Pt-BR`
- `+` separates distinct language *sets* within one release. Preserve verbatim; do not flatten.

### 3. Revision

| Form | Observed |
|---|---|
| Numeric | Rev 1 (459) · Rev 2 (72) · Rev 3 (14) · Rev 4 (7) · Rev 5 (8) · Rev 6 (4) · Rev 7 (1) · Rev 9 (2) · **Rev 10 (5) · Rev 11 (1)** |
| Alphabetic | **Rev A (2) · Rev B (2) · Rev D (1)** |
| Absent | = **Rev 0, the original, the OLDEST** |

- Absence means Rev 0. "Keep latest" that silently keeps the untagged original is the likeliest bug in the policy engine.
- **Rev 10 and Rev 11 exist in real data.** String sort places `Rev 10` between `Rev 1` and `Rev 2`. Parse numerically.
- Alphabetic revisions are live, not hypothetical. v1's `[\d.]+` regex made them invisible.
- Never compare numeric against alphabetic. If a single title carries both, flag it.

### 4. Version

`(v1.1)` 14 · `(v2.00)` 10 · `(v2.35)` · `(Version 3.5)` 5

- Separate scheme from Revision. Never merged or converted.
- `v1.10` is **newer** than `v1.9`. Parse numeric segments.
- **`(Version 3.5)` before the region boundary is part of the title.** See The Boundary Rule.

### 5. Development Status

`(Proto)` 73 · `(Sample)` 29 · `(Demo)` 17 · `(Beta 1)` 14 · `(Beta 2)` 13 · `(Kiosk)` 13 · `(Beta)` 12 · `(Proto 1)` 12 · `(Proto 2)` 12 · `(Test Program)` 9 · `(Tech Demo)` 3 · `(Wi-Fi Kiosk)` 15 · `(Alpha)` · `(Preview)` · `(Debug)` · `(Trial)`

Compound forms observed: `(Kiosk, GameCube)` · `(Kiosk, E3 2003)` · `(Kiosk, E3 1997)`

**Retail = no tag.** Retail outranks every pre-release build. **Among pre-release builds ordering is not determinable** — Alpha/Beta/Proto have no consistent cross-publisher timeline. Use an attached date tag; otherwise ask.

### 6. Date

`(1993-10-11)` · `(2003-03-03)` · `(19910501)` · `(1991)`

Attached to protos and tech demos. When present, the **only** reliable ordering signal between pre-release builds.

### 7. Disc / Media

`(Disc 1)` 99 · `(Disc 2)` 98 · `(Disc 3)` 20 · `(Disc 4)` 12 · also `(Disk N)`, `(CD N)`, `(Side A/B)`, `(Tape N)`

Normalize spelling to `(Disc N)`. Preserve Side/Tape where meaningful.

### 8. Licensing Status

`(Unl)` 366 · `(Aftermarket)` 102 · `(Pirate)` 21 · `(Tengen)` 5 · `(Retro-Bit)` 7 · `(Limited Run Games)` 19 · `(Strictly Limited Games)` 5 · `(Not For Resale)`

**A separate policy axis from development status.** "Keep retail only" must not silently mean "delete 390 unlicensed titles" unless the user chose that.

### 9. Distribution / Re-release Channel

`(Virtual Console)` 119 · `(Wii U Virtual Console)` 34 · `(Wii Virtual Console)` 6 · `(LodgeNet)` 32 · `(Wi-Fi Kiosk)` 15 · `(Switch)` 14 · `(Netcard)` 14 · `(e-Reader)` 13 · `(e-Reader Edition)` 13 · `(Switch Online)` 10 · `(Arcade)` 9 · `(Rerelease)` 5 · `(GameCube Edition)` 4 · `(GameCube)` 3

Comma-list forms: `(Virtual Console, Classic Mini, Switch Online)` 5 · `(Virtual Console, Switch Online)` 2

> **Trap:** these lists are comma + space separated and structurally identical to `(USA, Europe)`. Vocabulary must decide, never separator shape.

### 10. Hardware / Feature Flags

`(SGB Enhanced)` 158 · `(GB Compatible)` 133 · `(NDSi Enhanced)` 88 · `(Rumble Version)` 16 · `(NINA-06)` 6 · `(MB-91)` 4 · `(Competition Cart)` 4 · `(DX)`

Describe cartridge capability or PCB design. Never a dedup or variant signal.

### 11. Non-Game Content

`(DLC)` 171 · `(Save Data)` 14 · `(Bonus Disc)` 5 · `(Test Program)` 9 · `(Tech Demo)` 3 — **202 files**

**Excluded from variant grouping entirely.** A Bonus Disc is not a revision of the game it shipped beside. DLC is not a release candidate. Classify, set aside, never compare against retail entries.

### 12. Bracket Flags

Observed in corpus: `[b]` 9 · `[BIOS]` 1

Full legacy set (GoodTools / TOSEC, leaks into mixed sets): `[!]` good · `[!p]` pending · `[b]` bad · `[a]` alt · `[f]` fixed · `[h]` hack · `[t]` trained · `[o]` overdump · `[p]` pirate · `[c]` cracked · `[T+Eng]` / `[T-Eng]` translation · `[BIOS]`

---

## Open Vocabularies

**Unbounded free text. Cannot be enumerated — must be matched as a class, after all closed vocabularies fail.**

### Compilation Source
`(Atari Flashback)` 19 · `(Capcom Town)` 9 · `(Ninja JaJaMaru Retro Collection)` 6 · `(Capcom Classics Mini Mix)` 6 · `(Namco Museum Archives Vol 2)` 6 · `(The Cowabunga Collection)` 6 · `(Castlevania Advance Collection)` 4 · `(Castlevania Anniversary Collection)` 3 · `(Cowabunga Collection, The)` 3

> Note `(The Cowabunga Collection)` and `(Cowabunga Collection, The)` — **article inversion occurs inside tokens too.** Both must normalize to one grouping key.

### Publisher / Attribution
`(Little Orbit)` · `(Jason Rohrer with Music by Tom Bailey)` · `(Nickelodeon Movies)` · `(Nicktoons)`

Frequently appear **before** the region boundary, making them title content. The Boundary Rule resolves this without a publisher list.

### Serial
`(SCUS-94177)` and similar. Pattern: 3–4 letters, hyphen, digits.

---

## Article Inversion

519 occurrences — 5.2% of the corpus.

```
The Legend of Zelda        →  Legend of Zelda, The
Adventures of Tintin, The - The Game (USA) (En,Fr,Es,Pt)
Amazing Spider-Man 2, The (USA)
```

Articles: The, A, An · Le, La, Les, L' · Der, Die, Das, Ein, Eine · El, Los, Las, Un, Una · Il, Lo, I, Gli · De, Het, Een · En, Ett

Both forms normalize to one grouping key. Applies inside tokens as well as to titles.

---

## Classification Order

Strictly ordered. First match wins.

1. Split on the **region boundary** — everything left is title
2. Match remaining tokens against **closed vocabularies** in the order listed above
3. Match against **open vocabulary patterns** (serial, then compilation, then publisher)
4. Anything left → **unknown bucket**: preserved, flagged, surfaced for review

**Never classify by position or separator shape alone.** Both mislead on real data.
**Never drop an unknown token.** The unknown bucket is the input queue for extending these tables.

---

## Storage Reality

The reference corpus is **100% `.zip`** — 9,928 of 9,928 files. Cartridge sets in the wild are distributed archived.

Consequences:
- A cartridge game unit is **an archive containing one ROM**, not a bare file
- The parseable name is the archive's, but identity hashing must read the **ROM inside**
- Zip compression is not deterministic — hashing the archive can never match a DAT
- Set folder names carry format metadata worth reading: `Nintendo 64 (BigEndian)`, `NES (Headered)`, `GameCube - NKit RVZ`, `NDS (Decrypted)`

---

## Corpus Coverage

| | |
|---|---|
| Files analyzed | 9,928 |
| Systems | 16 |
| Distinct parenthetical tokens | 434 |
| Region at index 0 | 9,917 (99.89%) |
| Titles containing parentheses | 11 |
| Already-stacked duplicate tokens | **0** |
| Curation-sensitive files | ~1,069 (10.8%) |

The zero is important: a clean set cannot test the stacking bug. **The dirty half of the corpus must be synthesized.**

---

*Extend these tables from unknown-bucket output. Never from a single filename.*
