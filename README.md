# ARK Retro Forge

Universal ROM management for cartridge and disc systems. One tool, one pipeline: **identify → verify → dedupe → curate → report → rename → organize.** It reads your collection, compares it against DAT catalogs from No-Intro and Redump, tells you what you have, what is corrupt, what is missing, and what is redundant — and only then, if you ask it twice, moves anything.

v2 is a ground-up rebuild. v1 shipped in a week, reached ~42k impressions, and then broke on real collections; it is preserved under `legacy/` and mined for data, not code.

---

## The safety model

This is the part that matters, so it comes first. Every claim below is enforced structurally — by architecture tests over the source, not by discipline.

**DRY-RUN is the default for every operation that writes.** `ark rename <root>` builds a plan and prints it. `ark rename <root> --apply` is a different command. There is no config flag that removes the distinction.

**Nothing is ever deleted.** Removals move to `.ark-quarantine/<session-id>/` inside the root, with a manifest that explains itself without ARK present:

```json
{
  "SessionId": "dedup-20260804200603344",
  "CreatedUtc": "2026-08-04T20:06:03.3465022+00:00",
  "Policy": "KeepNewest",
  "Root": "C:\\tmp\\ark-demo",
  "Units": [
    {
      "OriginalPath": "C:\\tmp\\ark-demo\\Nintendo - Game Boy\\Aerostar (USA, Europe).zip",
      "QuarantinePath": "C:\\tmp\\ark-demo\\.ark-quarantine\\dedup-20260804200603344\\Nintendo - Game Boy\\Aerostar (USA, Europe).zip",
      "RomCrc32": "f6fd275e",
      "RomSha1": "2cb231e616008f2c809e0b2caf40f477689e6d07",
      "RomSize": 131072,
      "KeptPath": "C:\\tmp\\ark-demo\\Nintendo - Game Boy\\Aerostar (USA,Europe).zip",
      "KeptReason": "Duplicate of Aerostar (USA,Europe).zip (CRC32 f6fd275e); kept: newest copy"
    }
  ],
  "TotalBytes": 131072
}
```

**Every write is journaled and reversed exactly by `ark undo`.** The plan that was executed is the plan that gets inverted. A real cycle, checked byte-for-byte:

```
PS> ark undo rename-20260804200233646 --apply
Reversed 2 action(s). Journal:
C:\tmp\ark-bin\instances\default\journal\undo-rename-20260804200233646-20260804200341626.json

PS> Compare-Object (Get-Content C:\tmp\before.txt) (Get-Content C:\tmp\after.txt)
IDENTICAL - no differences
```

**Verification gates renaming.** Only units whose hash matched a DAT entry are given canonical names. A corrupt file wearing its canonical name looks verified forever after — worse than damage that announces itself.

**ARK never invents a name.** Canonical names come from the DAT entry the hash confirmed. No DAT match means no known name: refused and reported, never guessed.

```
Refused
  NoDatMatch — 1: no DAT entry matched, so there is no known canonical name —
one is never invented from the filename
```

**Operations take whole game units.** A disc unit is a CUE plus every BIN it names; a multi-disc set is removed whole or not at all. Quarantining Disc 2 of a three-disc set leaves a broken game and a user who does not know it.

**Ambiguity is reported, never resolved by coin flip.**

```
╭──────────┬────────┬──────────┬──────┬────────────────────────────────────────╮
│ CRC32    │ Copies │ ROM size │ Keep │ Decision                               │
├──────────┼────────┼──────────┼──────┼────────────────────────────────────────┤
│ f6fd275e │      2 │   128 KB │ —    │ tie — every copy is equally nested;    │
│          │        │          │      │ reported and skipped                   │
╰──────────┴────────┴──────────┴──────┴────────────────────────────────────────╯
1 group(s) the policy could not decide — reported and skipped, never resolved
arbitrarily.
```

---

## Requirements and install

- **.NET 8 runtime.** Nothing else.
- Portable. No installer, no registry, no service.

```
dotnet publish src/ARK.Cli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o <dir>
```

That produces `ark.exe` plus `config/`. Put the directory on your PATH. ARK keeps its database, DATs, logs, journal, and quarantine under `instances/default/` beside the executable — delete that folder and you are back to a clean slate.

```
PS> ark --version
2.0.0-alpha.16+52dc4072e59aa10a1f27ec61a2a50dc27c226003
```

---

## Quickstart

Five commands, from zero to a first report. Real output from a real 592-file Game Boy set.

**1. Check the environment.**

```
PS> ark medical-bay
┌─Medical Bay───────┐
│ Instance  default │
│ ROM Root  Not set │
│ System    Not set │
└───────────────────┘
```

Followed by a tools table and a per-DAT coverage table. External tools are all optional right now — nothing in the current feature set launches one.

**2. Get DATs.** Redump has direct URLs, so it syncs:

```
PS> ark dat sync
Sync complete: 52 fetched, 4 cached, 23 failed.
```

No-Intro is a manual download (see below), imported with:

```
PS> ark dat import "C:\Users\CJ\Downloads\No-Intro Love Pack (DAT) (2026-07-30).zip"
Imported 334 DAT(s), 1526595 entries indexed.
```

**3. Inventory the tree.** Read-only.

```
PS> ark scan "F:\GB"
Scan F:\GB
2 directories, 592 files — read-only, nothing written.

                              Buckets
╭────────────┬───────┬─────────────────────────────────────────────╮
│ Bucket     │ Files │ Meaning                                     │
├────────────┼───────┼─────────────────────────────────────────────┤
│ Identified │   591 │ Name matches a DAT entry                    │
│ Candidate  │     1 │ In a ROM set, no DAT match — never acted on │
│ Excluded   │     0 │ Not ROM content                             │
╰────────────┴───────┴─────────────────────────────────────────────╯
Identification is by name only. Hash verification is a later phase.

                               ROM sets (1)
╭─────────────────────┬───────┬────────────┬───────┬─────────────────────╮
│ Directory           │ Files │        Ext │ Named │ Compared against    │
├─────────────────────┼───────┼────────────┼───────┼─────────────────────┤
│ Nintendo - Game Boy │   592 │ 100 % .zip │ 100 % │ Nintendo - Game Boy │
╰─────────────────────┴───────┴────────────┴───────┴─────────────────────╯
```

**4. Hash it.** Read-only, and the slow step — it reads every byte.

```
PS> ark verify "F:\GB"
Verify F:\GB
592 unit(s) — read-only, nothing written. 0 hashed, 592 from cache, 0 B read in
00:00:00 (0 B/s).

                               States
╭──────────────┬───────┬────────────────────────────────────────────╮
│ State        │ Units │ Meaning                                    │
├──────────────┼───────┼────────────────────────────────────────────┤
│ Verified     │   591 │ Hash matches the DAT entry                 │
│ InProgress   │     0 │ Being written — not judged                 │
│ Mismatched   │     0 │ Claims to be a release; the bytes disagree │
│ Unrecognized │     1 │ No match by hash or by name                │
│ Excluded     │     0 │ Not ROM content                            │
╰──────────────┴───────┴────────────────────────────────────────────╯
591 unit(s) are rename-eligible. Only Verified units ever are.
```

**5. Ask what you are missing.** Read-only.

```
PS> ark report "F:\GB" --policy 1g1r
Report F:\GB
Target set: 1592 release(s) from policy '1g1r' over 1 DAT(s).
  Nintendo - Game Boy

                                   Collection
╭──────────────┬────────┬──────────────────────────────────────────────────────╮
│ State        │ Titles │ What to do                                           │
├──────────────┼────────┼──────────────────────────────────────────────────────┤
│ Present      │    591 │ Nothing                                              │
│ Missing      │   1039 │ Acquire                                              │
│ Damaged      │      0 │ Re-acquire this specific title                       │
│ Unrecognized │      1 │ Investigate — bad dump, hack, homebrew, or from      │
│              │        │ elsewhere                                            │
╰──────────────┴────────┴──────────────────────────────────────────────────────╯
37.1 % of the target set present and verified.
```

Nothing above wrote a single byte to the collection.

Before letting ARK modify anything, work through **[docs/FIRST-RUN.md](docs/FIRST-RUN.md)** — a risk-ascending walkthrough that ends with an apply-and-undo on a copy. Full command reference is **[docs/USAGE.md](docs/USAGE.md)**.

---

## Bring your own DATs and ROMs

**ARK ships neither.** It is a management tool, not a source.

**Redump** publishes DAT files at direct URLs, so `ark dat sync` fetches them. A live run over the shipped source list gets 52 of 79; the rest either publish no DAT for that system (Redump serves an ordinary HTML page — reported as such, not as an error) or serve a format the parser does not yet read.

**No-Intro is import-only, and that is deliberate.** Datomatic requires a session-cookie form flow ending in a "Prepare" step. The established tool for automating it drives Firefox through geckodriver — bundling a headless browser would contradict the portable single-executable promise, and a scraper that silently breaks is worse than none. Datomatic offers a "Daily" full pack; download it and run:

```
ark dat import "<path to the pack>"
```

A DAT whose name does not resolve to a known system code is still indexed and still usable — directories are matched against DAT names first, which is why a `Nintendo - Game Boy` folder identifies correctly even though `gb` is not a defined system code.

---

## Status

**Supported today**

- Cartridge and disc game-unit resolution, including multi-track discs, multi-disc sets, bare ISOs, and loose CUE+BIN layouts
- Filename tokenizing and canonical reassembly against a vocabulary, with an unknown-token bucket that is preserved rather than dropped
- DAT import (No-Intro packs), DAT sync (Redump), catalog queries by name and hash
- Directory classification on a mixed-use drive — validated against a 22,050-file reference listing
- Hash verification into five states, with a cache that makes the second pass nearly free
- Deduplication of byte-identical units, with selectable keep policies
- Multi-axis variant curation with selectable presets, and 1G1R collection reports
- Rename (canonicalize from DAT, or normalize as repair) and organize
- Full journal, and `ark undo` for every write

**Not supported yet**

- **CHD conversion and any other format conversion.** Requires orchestrating external processes, which sits outside the current executor guarantee.
- **Ripping.** Out of scope.
- **GUI.** An Avalonia front end over the same Core is planned; nothing exists yet.
- **clrmamepro-format DATs.** Two Redump sources (GameCube BIOS, PS2 BIOS) serve raw clrmamepro text instead of zipped Logiqx XML. They fail cleanly and are excluded from coverage.
- **Per-instance isolation from the command line.** Paths resolve per instance internally, but there is no `--instance` flag yet; everything uses `default`.
- **Content verification for conformant non-game archives.** A directory of flawlessly-named archives containing disc keys rather than games still classifies as a ROM set.

---

## Documentation

| Document | Purpose |
|---|---|
| [docs/FIRST-RUN.md](docs/FIRST-RUN.md) | Risk-ascending walkthrough — start here before applying anything |
| [docs/USAGE.md](docs/USAGE.md) | Every verb and flag, grouped by workflow |
| [CLAUDE.md](CLAUDE.md) | Design authority: architecture, prohibitions, phase history |
| [ARK-FILENAME-VOCABULARY.md](ARK-FILENAME-VOCABULARY.md) | Token vocabulary the parser is built on |
| [SECURITY.md](SECURITY.md) | Reporting policy |

---

## License

See [LICENSE](LICENSE).
