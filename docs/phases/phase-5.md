# Phase 5 — Hashing + Verification

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first, particularly the Phase 5 section — the five verification states, active-download detection, and the seeding hazard are specified there.

**Scope discipline:** implement Phase 5 and stop. No renaming, no dedup, no quarantine, no policy.

**Still read-only.** Phase 5 hashes and reports. Nothing is modified. The first phase that writes to user files is Phase 7, and Phase 6 (undo) lands before it.

---

## Part A — Hashing

### Hash the ROM, not the archive

The reference collection is **100% `.zip`**. DAT hashes describe the ROM inside; zip compression is not deterministic, so hashing the archive can never match.

Open the archive, stream the single entry, hash the stream. **Never extract** — Prohibition 11, already enforced by architecture test.

An archive holding more than one ROM is an anomaly (Phase 4 already reports it). Do not guess which entry is the game.

### Which hash, and why it differs by purpose

| Purpose | Method | Reason |
|---|---|---|
| **Verification** — does this file match its known DAT entry? | CRC32 + size | Comparing against one expected value. Collision risk negligible. |
| **Identification** — which DAT entry is this, out of 1.5M? | CRC32 + size, confirmed by SHA1 on collision | 1.5M entries in a 32-bit space carries real birthday-collision probability. Size is a strong disambiguator; SHA1 settles ties. |
| **Deduplication** — are these two files identical? | size → CRC32 → SHA1 on collision | Unique sizes cannot be duplicates and are never hashed. |

Deduplication's tiering is an optimization over a *set*. Verification has no such shortcut — it always reads the file.

### Cache

SQLite, per instance, keyed **path + size + mtime**. A second run over an unchanged set performs approximately zero hashing.

This also self-protects against a known hazard: a file hashed mid-download invalidates on the next write, because mtime changes.

### Scale is a first-class requirement

The reference drive is hundreds of gigabytes. A full hash pass takes hours.

- **Progress reporting is mandatory.** `dat import` shipped without it and was indistinguishable from hung for eight minutes. Spectre.Console is already in the CLI layer.
- **Interruptible and resumable.** Cancelling mid-run must not discard completed work — cache entries are committed as they are produced, not batched at the end.
- Report throughput and remaining estimate.

---

## Part B — Format variants

**Prefer matching the right DAT variant over transforming the file.**

Phase 2.1 keyed the catalog by (system, qualifier). Phase 4 records the qualifier on each unit from its folder name. These meet on the same string:

```
folder  N64\No-Intro\Nintendo - Nintendo 64 (BigEndian)
DAT     Nintendo - Nintendo 64 (BigEndian)

folder  NES\NES Roms\Nintendo - Nintendo Entertainment System (Headered)
DAT     Nintendo - Nintendo Entertainment System (Headered)
```

So a headered NES ROM matches the **Headered** DAT directly. No stripping, no conversion, no transformation of user data. The qualifier selects the DAT; it does not command a rewrite.

**Transformation is a fallback, used only when no DAT variant matches the file's format.** In that case, hash the transformed bytes **in memory** — never write a modified file.

| System | Variant | Detection |
|---|---|---|
| SNES | Copier header | 512 bytes prepended; `size % 1024 == 512` |
| N64 | `.z64` big-endian | Magic `80 37 12 40` |
| N64 | `.v64` byteswapped | Magic `37 80 40 12` |
| N64 | `.n64` little-endian | Magic `40 12 37 80` |
| NES | iNES 1.0 / NES 2.0 | 16-byte header; version in byte 7 |

Detect from **content**, not extension. Extensions lie; magic bytes do not.

When a file's detected format contradicts its folder qualifier, **report it** — do not silently trust either. That is a real finding about the user's collection.

---

## Part C — Verification

`ark verify` sorts units into the five states specified in `CLAUDE.md`:

**Verified** · **In Progress** · **Mismatched** · **Unrecognized** · **Excluded**

Rules that matter:

- **In Progress is checked before Mismatched.** A file mid-download fails hash comparison, which is true and useless. Sixty false corruption reports and the user stops reading the report.
- **Mismatched is diagnosable, not unknown.** "This claims to be *X* and the hash disagrees" — never a shrug into a mystery pile.
- **Nothing is deleted or quarantined.** Report only. Quarantine arrives with Phase 7, after undo exists.
- **Verification gates renaming.** Only Verified units may later receive canonical names. Enforce the flag now even though rename does not exist — a corrupt file renamed to its canonical name looks verified forever after.

Detection layers for In Progress, cheapest first: incomplete-file extensions (`.!qB`, `.part`, `.!ut`, `.bc!`, `.crdownload`, `.aria2`); a declared incomplete-download directory from settings; recent mtime.

### A file that changes while being read is In Progress

Observed on the reference drive: one archive classified as `unreadable-archive` in one scan and `empty-archive` in the next. Not non-determinism — the file is a PS3 title and that torrent was actively downloading. The bytes genuinely changed between passes.

**Rule:** capture mtime before hashing and re-check after. If it changed, the file was being written during the read. Classify **In Progress** and discard the hash — never cache it, never report Mismatched.

### Unreadable archives split two ways

The reference drive holds 29 archives that cannot be opened. Two samples carry a valid `PK\x03\x04` header with no End of Central Directory record — the signature of a truncated write. .NET's own zip reader fails identically, so this is real damage, not a library limitation.

But a **truncated abandoned download** and an **actively growing one** are structurally identical. Only mtime and download state separate them.

Cross-reference every unreadable archive against the In Progress signals before reporting it as damaged. An unfinished download reported as corruption sends the user chasing a problem that resolves itself.

**These are optimizations, not proof.** A real 0.99 GB file missing 16 MB was observed with no incomplete extension and full pre-allocated size — it passes both cheap checks. Only the hash catches it.

### Report output

Counts per state, per system, per format variant. Mismatched entries name the DAT entry they failed against. Machine-readable export (`--json`) alongside the rendered view, with the field-parity discipline Phase 1 established.

---

## Non-goals

- No renaming, moving, deleting, or quarantining
- No qBittorrent Web API integration — recorded in `CLAUDE.md` as a future option, not this phase
- No missing/upgradable reporting — Phase 8.5, needs the policy engine
- No writing of transformed files, ever

---

## Gate

1. `dotnet build` succeeds with zero warnings
2. `dotnet test` succeeds
3. Hashing reads the ROM inside a `.zip`, not the archive; `WriteToDirectory` architecture test still passes
4. Cache: second run over an unchanged set performs approximately zero hashing
5. Cache invalidates on mtime change
6. Cancelling mid-run preserves completed cache entries
7. Progress is reported during a long run
8. A headered NES ROM matches the **Headered** DAT variant with no transformation
9. An N64 BigEndian ROM matches the **BigEndian** variant with no transformation
10. Format detected from magic bytes, not extension; a mislabelled extension is detected correctly
11. A file whose detected format contradicts its folder qualifier is **reported**, not silently accepted
12. In-memory transformation fallback produces a correct hash and **writes nothing**
13. Five states assigned correctly; In Progress is evaluated before Mismatched
14. A file with correct name and size but corrupted contents reports **Mismatched**, not Verified and not Unrecognized
15. A file with an incomplete-download extension reports **In Progress**
15a. A file whose mtime changes during hashing reports **In Progress**; its hash is discarded and never cached
15b. An unreadable archive is cross-referenced against In Progress signals before being reported as damaged
16. Only Verified units carry the rename-eligible flag
17. `ark verify` is read-only — a full run leaves the tree byte-identical and writes no journal
18. Human and `--json` output carry the same fields
19. All prior phase gates still pass

Then stop and report. Phase 6 is `ark undo`.
