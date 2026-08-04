# ARK Retro Forge — Usage

Every verb, every flag, with real invocations and real output. Grouped by workflow, not alphabetically.

**Every command and every output block on this page was executed.** Where output is long it is truncated with `...`, never paraphrased.

---

## Conventions

**Does it write?** Each verb is marked:

| Marker | Meaning |
|---|---|
| **read-only** | Cannot modify the collection under any flag |
| **writes with `--apply`** | DRY-RUN by default; `--apply` is a separate, explicit command |
| **writes immediately** | Writes on invocation — only `config set`, and only to ARK's own settings |

**Exit codes.** `0` on success. `1` on a user-fixable error — a missing directory, a malformed settings file, a name that could not be tokenized, an undo whose preconditions failed. Nothing returns anything else.

**Global surface.**

```
PS> ark --help
Description:
  ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.

Usage:
  ark [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  medical-bay        Report tool, DAT, and instance status.
  config             View and change ARK settings.
  dat                Import, sync, and inspect DAT catalogs.
  parse <name>       Show the token breakdown for one filename.
  scan <root>        Inventory a directory tree: identified, candidate, and excluded.
  verify <root>      Hash a set and sort it into the five verification states.
  dedupe <root>      Find byte-identical game units and quarantine the redundant copies.
  curate <root>      Group release variants and apply a keep policy.
  report <root>      Compare a collection against the target set your policy declares.
  rename <root>      Give units their canonical names.
  organize <root>    File units into directories named after their DAT.
  journal            Inspect executed sessions.
  undo <session-id>  Reverse a journaled session.
```

There is **no `--instance` flag.** All state lives under `instances/default/` beside the executable.

---

# Setup

## `medical-bay` — read-only

Reports instance state, external tool availability, and DAT coverage per system.

```
Options:
  --json          Emit the report as JSON to stdout instead of rendering tables.
  -?, -h, --help  Show help and usage information
```

```
PS> ark medical-bay
┌─Medical Bay───────┐
│ Instance  default │
│ ROM Root  Not set │
│ System    Not set │
└───────────────────┘
                                     Tools
╭──────────────┬─────────┬─────────┬─────────┬───────────┬─────────────────────╮
│ Tool         │ Status  │ Version │ Minimum │ Meets min │ Location / Notes    │
├──────────────┼─────────┼─────────┼─────────┼───────────┼─────────────────────┤
│ chdman       │ Missing │ n/a     │ 0.261   │ no        │ 'chdman.exe' not    │
│              │         │         │         │           │ found in tools/ or  │
│              │         │         │         │           │ on PATH             │
│ maxcso       │ Missing │ n/a     │ -       │ no        │ 'maxcso.exe' not    │
│              │         │         │         │           │ found in tools/ or  │
│              │         │         │         │           │ on PATH             │
...
```

**Missing tools are not an error.** Nothing in the current feature set launches an external process; they are listed for the conversion work that has not landed. The full run is 992 lines — most of it a per-DAT coverage table with one row per indexed DAT.

## `config` — `show` read-only, `set` **writes immediately**

```
PS> ark config --help
Commands:
  show  Show the current settings.
  set   Change a setting.

PS> ark config set --help
Commands:
  rom-root <path>  Set the rom root.
  system <code>    Set the active system.
```

```
PS> ark config show
┌─Settings────────────────┐
│ Schema version  1       │
│ ROM root        Not set │
│ Active system   Not set │
└─────────────────────────┘

PS> ark config set rom-root "F:\"
[16:05:14 INF] Applied CreateDirectory: C:\tmp\ark-bin\instances\default -> null
[16:05:14 INF] Applied WriteText: C:\tmp\ark-bin\instances\default\settings.json -> null
ROM root set to: F:\

PS> ark config show
┌─Settings────────────────┐
│ Schema version  1       │
│ ROM root        F:\     │
│ Active system   Not set │
└─────────────────────────┘
```

**`config set` is the one deliberate exception to DRY-RUN.** It writes the value you just typed. The write still goes through the planner and is still journaled — you can see both actions above — but it does not require `--apply`. The DRY-RUN default exists to protect ROM collections; previewing a setting you just typed is ceremony, not safety. The exemption does not extend to anything touching user data.

## `dat import <path>` — writes to the catalog, not to your collection

Ingests a `.dat`, an archive of DATs (a Datomatic "Daily" pack), or a directory of them.

```
Arguments:
  <path>  A .dat file, an archive of DATs (Daily pack), or a directory of DATs.
```

```
PS> ark dat import "C:\Users\CJ\Downloads\No-Intro Love Pack (DAT) (2026-07-30).zip"
...
│ Unofficial - Video Game Scans (RAW)            │ (unrecognized)    │ 2,293   │
╰────────────────────────────────────────────────┴───────────────────┴─────────╯
Imported 334 DAT(s), 1526595 entries indexed.
```

DATs whose names do not resolve to a defined system code are indexed anyway, under `(unrecognized)`. They still work: directories are matched against DAT names before system codes.

## `dat sync` — writes to the catalog

Fetches sources that expose a direct URL. Redump qualifies; No-Intro does not.

```
Options:
  --system <system>  Limit to sources for one system code.
  --force            Refetch even if already cached.
```

```
PS> ark dat sync
...
│ No-Intro - Game Boy Advance      │ Failed  │ download failed: Response       │
│                                  │         │ status code does not indicate   │
│                                  │         │ success: 503 (Service           │
│                                  │         │ Unavailable).                   │
╰──────────────────────────────────┴─────────┴─────────────────────────────────╯
Sync complete: 52 fetched, 4 cached, 23 failed.
```

**Failures are expected and individually explained.** Of 79 shipped sources, 21 are Redump systems that publish no DAT at all — Redump serves its ordinary web page, which ARK detects before parsing:

```
│ Redump - Xbox One                │ Failed  │ the response is an HTML page,   │
│                                  │         │ not a DAT — the source most     │
│                                  │         │ likely publishes no DAT for     │
│                                  │         │ this system                     │
```

A second run serves from cache. `--force` refetches. `--system` narrows the run — note it matches the source's **system code**, and codes that no source declares return nothing:

```
PS> ark dat sync --system gb
            Sync
╭────────┬─────────┬────────╮
│ Source │ Outcome │ Detail │
╰────────┴─────────┴────────╯
Sync complete: 0 fetched, 0 cached, 0 failed.
```

## `dat list` — read-only

```
Options:
  --system <system>  Limit to one system code.
  --recognized       Only DATs that resolved to a system.
  --unrecognized     Only DATs that did not resolve.
  --verbose          Include author lists.
```

```
PS> ark dat list --recognized
...
│ n64 (BigEndian)   │ Nintendo - Nintendo 64   │   1,157 │ 20260729-132206     │
│ n64 (ByteSwapped) │ Nintendo - Nintendo 64   │   1,157 │ 20260729-132206     │
│ nes (Headered)    │ Nintendo - Nintendo      │   4,505 │ 20260721-133303     │
│ nes (Headerless)  │ Nintendo - Nintendo      │   4,509 │ 20260721-133303     │
│ snes              │ Nintendo - Super         │   4,128 │ 20260729-015337     │
```

**A system can hold several DATs keyed by format qualifier.** `nes (Headered)` and `nes (Headerless)` are two distinct hash sets over the same games — a headered ROM will never match a headerless DAT. Verification targets the variant the ROM actually is.

```
PS> ark dat list --system gb
No catalogs match.
```

That is not a missing DAT. `gb` is not a defined system code, so the Game Boy DAT is indexed as `(unrecognized)` — and identification still works, because directory names are matched against DAT names first.

---

# Inspect

## `parse <name>` — read-only

The debugging surface for everything downstream. Tokenizes one filename and shows the classification.

```
Arguments:
  <name>  Filename to tokenize. The extension, if any, is ignored.
```

```
PS> ark parse "Legend of Zelda, The - Link's Awakening (USA, Europe) (Rev 2).zip"
Title  Legend of Zelda, The - Link's Awakening
╭──────────┬─────────────╮
│ Category │ Token       │
├──────────┼─────────────┤
│ Region   │ USA, Europe │
│ Revision │ Rev 2       │
╰──────────┴─────────────╯
Canonical  Legend of Zelda, The - Link's Awakening (USA, Europe) (Rev 2)
Match key   THE LEGEND OF ZELDA - LINK'S AWAKENING|1:USA, EUROPE|6:REV 2
```

The **match key** is what ARK actually compares on — a token *set*, not a string. Note `Legend of Zelda, The` folds to `THE LEGEND OF ZELDA`, so both spellings group as one game.

Unknown tokens are preserved and surfaced, never dropped:

```
PS> ark parse "Tekken 3 (USA) (En,Fr,De) (Wobble Edition)"
Title  Tekken 3
╭──────────┬────────────────╮
│ Category │ Token          │
├──────────┼────────────────┤
│ Region   │ USA            │
│ Language │ En,Fr,De       │
│ Unknown  │ Wobble Edition │
╰──────────┴────────────────╯
Unknown bucket: Wobble Edition
Preserved and surfaced. Unknown tokens are how the vocabulary tables grow.
Canonical  Tekken 3 (USA) (En,Fr,De) (Wobble Edition)
Match key   TEKKEN 3|0:WOBBLE EDITION|1:USA|2:EN,FR,DE
```

`(En,Fr,De)` classifies as **Language**, not Region. Getting that wrong is what corrupted correct files in v1.

**Exit code 1** when a name cannot be tokenized:

```
PS> ark parse "(((("
Flagged: NoRegion
((((
No recognized region token, so the title/metadata boundary is undefined. Not
guessed.

EXIT=1
```

## `scan <root>` — read-only

Inventories a tree into three buckets. Classifies **directories**, not files.

```
Arguments:
  <root>  Directory tree to scan. Read only — nothing is written or moved.

Options:
  --all           List every excluded directory, not just a summary.
```

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

Excluded (1 directories, 0
           files)
╭───────────┬──────┬───────╮
│ Reason    │ Dirs │ Files │
├───────────┼──────┼───────┤
│ Container │    1 │     0 │
╰───────────┴──────┴───────╯
GB — contains only subdirectories
```

**"Compared against" is the important column.** A ROM-set directory is identified against exactly one DAT, never the whole catalog. A directory that resolves to no DAT yields only candidates, and says so:

```
│ Sony - PlayStation │  1765 │ 100 % .zip │ 100 % │ no DAT — nothing
│                    │       │            │       │ identified
```

On a disc set, scan also reports which resolver claimed what:

```
1765 unit(s): 3 cartridge, 1762 disc (198 carrying a disc number).
```

## `verify <root>` — read-only

Hashes every unit and sorts it into five states. **Always hashes** — size and presence prove nothing.

```
Options:
  --json          Emit the report as JSON to stdout instead of rendering tables.
  --all           List every unit in every state, not just a summary.
```

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

                                   By DAT
╭─────────────────────┬──────────┬────────────┬─────────────┬──────────────╮
│ DAT / folder        │ Verified │ Mismatched │ In progress │ Unrecognized │
├─────────────────────┼──────────┼────────────┼─────────────┼──────────────┤
│ Nintendo - Game Boy │      591 │          0 │           0 │            1 │
╰─────────────────────┴──────────┴────────────┴─────────────┴──────────────╯
```

**The cache makes the second run nearly free.** Keyed on path + size + mtime, so any write invalidates it. Cold then warm, on a 12-file copy:

```
12 unit(s) — read-only, nothing written. 12 hashed, 0 from cache, 1.7 MB read in
00:00:00 (9.1 MB/s).
```

On disc sets, verify also reports multi-disc completeness:

```
Multi-disc sets — 0 complete, 1 incomplete
  Final Fantasy VII — 1 of 3 discs mismatched — the set is incomplete until
every disc verifies
A set is complete only when every disc in it verifies.
```

### Reading the states

| State | What it means | What to do |
|---|---|---|
| **Verified** | Hash matches a DAT entry. This file is provably the release it claims to be. | Nothing. Only these are eligible for renaming. |
| **In Progress** | Actively being written, or inside a declared incomplete-download directory. **Not judged** — ARK declines to have an opinion. | Wait for the transfer to settle, then re-run. Never treat this as a problem report. |
| **Mismatched** | The name matches a DAT entry and the hash does not. Diagnosable, not unknown. | Investigate this specific title: incomplete download, bit rot, or a different dump. The report is exportable as a re-acquisition queue. |
| **Unrecognized** | No match by hash or by name. | Investigate. Bad dump, hack, homebrew, translation, or a DAT you have not imported. Not necessarily wrong. |
| **Excluded** | Not ROM content, with a stated reason. | Nothing, unless the reason surprises you. |

**In Progress is not a softer Mismatched.** A file mid-download fails hash comparison — technically true and uselessly reported. Sixty "corrupt" files that are merely still downloading is how a report stops being read. Detection is layered cheapest-first: incomplete-download extensions (`.part`, `.!ut`, `.!qB`, `.crdownload`), an exclusive-open refusal, a declared incomplete directory, a recent write, then hash.

**A freshly written set is skipped wholesale.** The recent-write window defaults to 5 minutes:

```json
"recentWriteWindowMinutes": 5,
```

Whether this fires depends on how the files got there. PowerShell's `Copy-Item` preserves the source timestamp, so a copy verifies immediately. A tool that stamps the destination with the current time — `cp` in Git Bash, most downloaders — trips the window:

```
PS> ark verify "C:\tmp\ark-demo"
12 unit(s) — read-only, nothing written. 0 hashed, 0 from cache, 0 B read in
00:00:00 (0 B/s).
╭──────────────┬───────┬────────────────────────────────────────────╮
│ Verified     │     0 │ Hash matches the DAT entry                 │
│ InProgress   │    12 │ Being written — not judged                 │
╰──────────────┴───────┴────────────────────────────────────────────╯
0 unit(s) are rename-eligible. Only Verified units ever are.
```

That is correct behaviour, not a failure. Wait five minutes, or lower `recentWriteWindowMinutes` in `config/scan/scan-rules.json`.

---

# Curate

## `dedupe <root>` — writes with `--apply`

Finds **byte-identical** game units. Revisions are not duplicates — they have different hashes.

```
Options:
  --apply                                                                      Quarantine the redundant copies. Without this, nothing is moved.
  --policy <KeepIdentified|KeepNewest|KeepOldest|KeepShortestPath|ReportOnly>  Which copy to keep. Defaults to report-only, which decides nothing. [default: ReportOnly]
  --all                                                                        List every group, not just a summary.
```

Default is report-only — a first run decides nothing and moves nothing, twice over:

```
PS> ark dedupe "F:\GB"
Dedupe F:\GB
Policy: ReportOnly. 1 duplicate group(s), 2 unit(s) hashed this run.

                                Duplicate groups
╭──────────┬────────┬──────────┬──────┬────────────────────────────────────────╮
│ CRC32    │ Copies │ ROM size │ Keep │ Decision                               │
├──────────┼────────┼──────────┼──────┼────────────────────────────────────────┤
│ a45ef889 │      2 │     1 MB │ —    │ report only — nothing is chosen and    │
│          │        │          │      │ nothing moves                          │
╰──────────┴────────┴──────────┴──────┴────────────────────────────────────────╯
0 group(s) resolved, 0 redundant copy(ies), 0 B reclaimable.
1 group(s) the policy could not decide — reported and skipped, never resolved
arbitrarily.

Not considered
  UniqueSize — 3: unique ROM size — cannot have a byte-identical twin, so it was
never hashed
```

**Hashing is tiered.** Group by size (free), CRC32 the survivors, SHA1 only to settle a CRC collision. A unique size is never opened.

**A tie is reported, never broken.** A policy that cannot separate two copies skips the group whole:

```
PS> ark dedupe "C:\tmp\ark-demo" --policy KeepShortestPath
│ f6fd275e │      2 │   128 KB │ —    │ tie — every copy is equally nested;    │
│          │        │          │      │ reported and skipped                   │

PS> ark dedupe "C:\tmp\ark-demo" --policy KeepIdentified
│ f6fd275e │      2 │   128 KB │ —    │ tie — every copy is equally            │
│          │        │          │      │ identified; reported and skipped       │
```

A policy that *can* decide, applied:

```
PS> ark dedupe "C:\tmp\ark-demo" --policy KeepNewest --apply
╭──────────┬────────┬──────────┬───────────────────────────┬───────────────────╮
│ CRC32    │ Copies │ ROM size │ Keep                      │ Decision          │
├──────────┼────────┼──────────┼───────────────────────────┼───────────────────┤
│ f6fd275e │      2 │   128 KB │ Aerostar (USA,Europe).zip │ kept: newest copy │
╰──────────┴────────┴──────────┴───────────────────────────┴───────────────────╯
1 group(s) resolved, 1 redundant copy(ies), 62.3 KB reclaimable.

[16:06:03 INF] Applied CreateDirectory: C:\tmp\ark-demo\.ark-quarantine -> null
[16:06:03 INF] Applied CreateDirectory: C:\tmp\ark-demo\.ark-quarantine\dedup-20260804200603344 -> null
[16:06:03 INF] Applied CreateDirectory: C:\tmp\ark-demo\.ark-quarantine\dedup-20260804200603344\Nintendo - Game Boy -> null
[16:06:03 INF] Applied Quarantine: C:\tmp\ark-demo\Nintendo - Game Boy\Aerostar (USA, Europe).zip -> C:\tmp\ark-demo\.ark-quarantine\dedup-20260804200603344\Nintendo - Game Boy\Aerostar (USA, Europe).zip
[16:06:03 INF] Applied WriteText: C:\tmp\ark-demo\.ark-quarantine\dedup-20260804200603344\manifest.json -> null
Quarantined 1 unit(s). Manifest and journal written; reverse with ark undo
dedup-20260804200603344.
```

Nothing was deleted. Quarantine lands inside the root, on the same volume — a cross-volume move is a copy, and is refused rather than silently performed.

## `curate <root>` — writes with `--apply`

Groups release variants and applies a keep policy. **Distinct from dedupe:** every removal here is a real loss of a real release, recoverable from nothing.

```
Options:
  --policy <policy>        Preset to apply: report-only, everything, retail-only, latest-revision, 1g1r. Defaults to the configured policy.
  --apply                  Carry the plan out. Without this, nothing moves.
  --sort                   Move non-kept variants into a subfolder instead of quarantining them. Organization, not removal.
  --subfolder <subfolder>  Subfolder for --sort. Defaults to 'variants'.
  --all                    List every group, not just a summary.
```

```
PS> ark curate "F:\GB" --policy retail-only
Curate F:\GB
Policy: retail-only
                                      Axes
╭──────────┬─────────────────────────────────┬─────────────────────────────────╮
│ Role     │ Axes                            │ Meaning                         │
├──────────┼─────────────────────────────────┼─────────────────────────────────┤
│ Identity │ Revision, Version, Region,      │ Differing here means a          │
│          │ Licensing, Distribution,        │ different game — never compared │
│          │ Language                        │                                 │
│ Variance │ DevStatus                       │ Differing here means variants   │
│          │                                 │ of one game — ranked            │
│ Ignored  │ —                               │ Left entirely alone             │
╰──────────┴─────────────────────────────────┴─────────────────────────────────╯
Hardware flags never participate: they describe cartridge capability, not
release lineage.

                               Variant groups (6)
╭─────────────────────────┬──────────┬──────┬────────┬─────────────────────────╮
│ Title                   │ Releases │ Keep │ Remove │ Decision                │
├─────────────────────────┼──────────┼──────┼────────┼─────────────────────────┤
│ Mick & Mack as the      │        4 │    4 │      — │ policy removes nothing  │
│ Global Gladiators       │          │      │        │ from this group         │
│ Asterix                 │        3 │    3 │      — │ reported and skipped —  │
│                         │          │      │        │ an axis could not be    │
│                         │          │      │        │ ordered                 │
│ Hook                    │        2 │    1 │      1 │ DevStatus: kept         │
│                         │          │      │        │ (untagged)              │
...
```

**Read the Axes table first.** It states which reading produced the decisions. With region as identity, `Game (USA)` and `Game (Japan)` are different games; with region as variance they are two variants of one. The same collection curates differently under each, so the report says which was used.

**A group whose axis cannot be ordered is skipped whole** — pre-release builds have no consistent cross-publisher timeline, so Alpha/Beta/Proto are never auto-ordered against each other.

**`--sort` is organization, not removal.** It moves non-kept variants into a subfolder instead of quarantining them. Different risk profile, never conflated in the report.

## `report <root>` — read-only

The join of catalog, scan, verification, and policy. Compares your collection against the target set your policy declares.

```
Options:
  --policy <policy>             Policy defining the target set: report-only, everything, retail-only, latest-revision, 1g1r. Defaults to the configured policy.
  --pivot <Both|Region|System>  Pivot the counts by system, region, or both. [default: System]
  --json                        Emit the report as JSON instead of tables.
  --missing                     Emit only the missing list, one line per title, as a re-acquisition queue.
  --all                         List every row, not just a summary.
```

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

**Completeness is measured against a declared target, never the whole DAT.** Changing `--policy` changes the missing count and nothing else — `everything` compares against every region, revision, proto, beta, sample and aftermarket release, which is true and useless.

Pivot by region:

```
PS> ark report "F:\GB" --policy 1g1r --pivot Region
   Missing by region
╭───────────┬─────────╮
│ Region    │ Missing │
├───────────┼─────────┤
│ Japan     │     718 │
│ Europe    │     170 │
│ World     │      79 │
│ USA       │      21 │
│ Taiwan    │      19 │
...
```

`--missing` emits a machine-readable work queue — tab-separated, one line per title:

```
PS> ark report "F:\GB" --policy 1g1r --missing
-	[BIOS] Maxstation Boot ROM	China	-	Nintendo - Game Boy
-	[BIOS] Nintendo Game Boy Boot ROM	World	Rev 1	Nintendo - Game Boy
-	[BIOS] Nintendo Game Boy Pocket Boot ROM	World	-	Nintendo - Game Boy
-	3 Choume no Tama - Tama and Friends - 3 Choume Obake Panic!!	Japan	-	Nintendo - Game Boy
-	3-pun Yosou - Umaban Club	Japan	-	Nintendo - Game Boy
-	4 in 1	Europe	-	Nintendo - Game Boy
...
```

> **Known display issue.** The **Upgradable** list always renders the difference as a revision, even when the axis that actually ranks higher is something else. On the set above it prints `Beethoven Rev 0 → Rev 0`, because the held copy is `Beethoven (USA) (Proto) (SGB Enhanced).zip` and the target ranks the retail release — a development-status difference, not a revision one. The grouping and ranking are correct; only the label is wrong. Read those rows as "something ranks higher", then check the file.

---

# Act

## `rename <root>` — writes with `--apply`

Gives units their canonical names.

```
Options:
  --apply         Carry the plan out. Without this, nothing is renamed.
  --normalize     Reformat each unit's own name instead of taking it from the DAT. Repair, not identification.
  --all           List every decision, not just a summary.
```

**Two modes, never confused.** *Canonicalize* (the default, no flag) takes the name from the DAT entry the unit's hash confirmed. *Normalize* (`--normalize`) reformats the unit's own existing name to repair prior damage — it claims only that the result is well-formed, never that it is correct.

On an already-conformant set, the correct answer is zero changes and zero writes:

```
PS> ark rename "F:\GB"
Rename F:\GB
Mode: canonicalize — names come from the DAT entry each unit's hash confirmed.

                       Decisions
╭─────────────────┬───────┬────────────────────────────╮
│ Outcome         │ Units │ Meaning                    │
├─────────────────┼───────┼────────────────────────────┤
│ Rename          │     0 │ Name would change          │
│ Already correct │   591 │ No filesystem write at all │
│ Refused         │     1 │ Left alone, with a reason  │
╰─────────────────┴───────┴────────────────────────────╯
Refused
  NoDatMatch — 1: no DAT entry matched, so there is no known canonical name —
one is never invented from the filename
```

On a set with damage, DRY-RUN first:

```
PS> ark rename "C:\tmp\ark-demo" --all
Rename C:\tmp\ark-demo
Mode: canonicalize — names come from the DAT entry each unit's hash confirmed.

                       Decisions
╭─────────────────┬───────┬────────────────────────────╮
│ Outcome         │ Units │ Meaning                    │
├─────────────────┼───────┼────────────────────────────┤
│ Rename          │     1 │ Name would change          │
│ Already correct │    10 │ No filesystem write at all │
│ Refused         │     1 │ Left alone, with a reason  │
╰─────────────────┴───────┴────────────────────────────╯
Would rename (1):
  aladdin (usa) (sgb enhanced).zip
    → Aladdin (USA) (SGB Enhanced).zip

Refused
  NoDatMatch — 1: no DAT entry matched, so there is no known canonical name —
one is never invented from the filename

1 unit(s) routed through a temporary name to break a rename cycle safely.
DRY-RUN. 1 unit(s) would be renamed. Nothing was touched. Re-run with --apply.
```

Then apply:

```
PS> ark rename "C:\tmp\ark-demo" --apply
[16:02:33 INF] Applied Rename: C:\tmp\ark-demo\Nintendo - Game Boy\aladdin (usa) (sgb enhanced).zip -> C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip.ark-rename-tmp0
[16:02:33 INF] Applied Rename: C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip.ark-rename-tmp0 -> C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip
1 unit(s) routed through a temporary name to break a rename cycle safely.
Renamed 1 unit(s). Archive and ROM bytes unchanged; reverse with ark undo
rename-20260804200233646.
```

**The two-step through `.ark-rename-tmp0` is deliberate.** A case-only rename is unreliable on a case-insensitive filesystem, as are swaps and cycles; staging through a temporary name makes them safe. Both steps are journaled.

**Archives are renamed, never rewritten.** The entry inside keeps its name — rewriting means recompressing, which touches ROM data for a cosmetic gain DAT verification does not care about.

**Refusals you will see:**

| Refusal | Meaning |
|---|---|
| `NoDatMatch` | No DAT entry matched. There is no known canonical name and one is never invented. |
| `NotVerified` | The unit is not Verified. A corrupt file wearing its canonical name looks verified forever. |
| `Unparseable` | The name could not be tokenized, so normalizing it is not possible. |
| `Collision` | Two units want the same name. Neither is overwritten and neither is suffixed — a disambiguating suffix is a fabricated name. |
| `ActiveDownload` | Inside a directory showing active-download signals. |
| `LooseDiscUnit` | A loose CUE plus track files. Renaming the tracks would require rewriting the CUE, which ARK never does. |

## `organize <root>` — writes with `--apply`

Files units into directories named after their DAT.

```
Options:
  --apply         Carry the plan out. Without this, nothing moves.
```

```
PS> ark organize "C:\tmp\ark-demo"
Organize
0 to file, 12 already in place, 0 left alone.
Nothing to organize.
```

Distinct from rename, separately reported, and not a removal.

---

# Recover

## `journal list` — read-only

```
PS> ark journal list
                                    Sessions
╭───────────────────┬───────────────────┬──────────────────┬─────────┬─────────╮
│ Session           │ When              │ Operation        │ Actions │ State   │
├───────────────────┼───────────────────┼──────────────────┼─────────┼─────────┤
│ rename-2026080420 │ 2026-08-04        │ rename-canonical │       2 │ applied │
│ 0233646           │ 20:02:33Z         │ ize              │         │         │
╰───────────────────┴───────────────────┴──────────────────┴─────────┴─────────╯
```

## `journal show <session-id>` — read-only

```
PS> ark journal show rename-20260804200233646
rename-20260804200233646 — rename-canonicalize (schema v1)
╭───┬────────┬─────────────────────┬─────────────────────┬─────────────────────╮
│ # │ Kind   │ Source              │ Destination         │ Reason              │
├───┼────────┼─────────────────────┼─────────────────────┼─────────────────────┤
│ 1 │ Rename │ C:\tmp\ark-demo\Nin │ C:\tmp\ark-demo\Nin │ DAT entry in        │
│   │        │ tendo - Game        │ tendo - Game        │ 'Nintendo - Game    │
│   │        │ Boy\aladdin (usa)   │ Boy\Aladdin (USA)   │ Boy': aladdin (usa) │
│   │        │ (sgb enhanced).zip  │ (SGB                │ (sgb enhanced).zip  │
│   │        │                     │ Enhanced).zip.ark-r │ -> Aladdin (USA)    │
│   │        │                     │ ename-tmp0          │ (SGB Enhanced).zip  │
│ 2 │ Rename │ C:\tmp\ark-demo\Nin │ C:\tmp\ark-demo\Nin │ DAT entry in        │
│   │        │ tendo - Game        │ tendo - Game        │ 'Nintendo - Game    │
│   │        │ Boy\Aladdin (USA)   │ Boy\Aladdin (USA)   │ Boy': aladdin (usa) │
│   │        │ (SGB                │ (SGB Enhanced).zip  │ (sgb enhanced).zip  │
│   │        │ Enhanced).zip.ark-r │                     │ -> Aladdin (USA)    │
│   │        │ ename-tmp0          │                     │ (SGB Enhanced).zip  │
╰───┴────────┴─────────────────────┴─────────────────────┴─────────────────────╯
```

Every enum is written by name, never by ordinal. A journal carrying a name ARK does not recognize is refused by undo rather than guessed at — an `ActionKind` shifting by one would otherwise replay a `Move` as a `Quarantine`.

## `undo <session-id>` — writes with `--apply`

```
Arguments:
  <session-id>  Session to reverse.

Options:
  --apply         Actually reverse the session. Without this, nothing is touched.
  --force         Allow a restore to overwrite a file that has appeared at its destination.
```

Preview first — DRY-RUN here too:

```
PS> ark undo dedup-20260804200603344
Undo dedup-20260804200603344
                                Inverse actions
╭───┬─────────────────┬────────────────────────┬───────────────────────┬───────╮
│ # │ Kind            │ From                   │ To                    │ State │
├───┼─────────────────┼────────────────────────┼───────────────────────┼───────┤
│ 1 │ DeleteFile      │ C:\tmp\ark-demo\.ark-q │ -                     │ Ready │
│   │                 │ uarantine\dedup-202608 │                       │       │
│   │                 │ 04200603344\manifest.j │                       │       │
│   │                 │ son                    │                       │       │
│ 2 │ Move            │ C:\tmp\ark-demo\.ark-q │ C:\tmp\ark-demo\Ninte │ Ready │
│   │                 │ uarantine\dedup-202608 │ ndo - Game            │       │
│   │                 │ 04200603344\Nintendo - │ Boy\Aerostar (USA,    │       │
│   │                 │ Game Boy\Aerostar      │ Europe).zip           │       │
│   │                 │ (USA, Europe).zip      │                       │       │
│ 3 │ RemoveDirectory │ C:\tmp\ark-demo\.ark-q │ -                     │ Ready │
│   │                 │ uarantine\dedup-202608 │                       │       │
│   │                 │ 04200603344\Nintendo - │                       │       │
│   │                 │ Game Boy               │                       │       │
│ 4 │ RemoveDirectory │ C:\tmp\ark-demo\.ark-q │ -                     │ Ready │
│   │                 │ uarantine\dedup-202608 │                       │       │
│   │                 │ 04200603344            │                       │       │
│ 5 │ RemoveDirectory │ C:\tmp\ark-demo\.ark-q │ -                     │ Ready │
│   │                 │ uarantine              │                       │       │
╰───┴─────────────────┴────────────────────────┴───────────────────────┴───────╯
```

Then apply:

```
PS> ark undo dedup-20260804200603344 --apply
Reversed 5 action(s). Journal:
C:\tmp\ark-bin\instances\default\journal\undo-dedup-20260804200603344-20260804200658334.json
```

**Undo verifies before it acts**, against a projection of the filesystem as each earlier inverse leaves it — not against the world as it stands when the run starts. A precondition failure stops the run, names the action, and changes nothing further. Restoring over a file that has appeared since is refused without `--force`.

**The undo is itself journaled.** You can see the new journal path in the output above.

---

## Where ARK keeps things

All under `instances/default/`, beside the executable:

| Path | Contents |
|---|---|
| `db/` | Hash cache and DAT catalog (SQLite) |
| `dat/` | Downloaded DAT files |
| `journal/` | One JSON file per executed session |
| `logs/` | Serilog output |
| `quarantine/` | Instance-level quarantine (operation quarantine lands under the collection root instead) |
| `settings.json` | What `config set` writes |

Configuration that ships with the binary lives in `config/` — `scan/scan-rules.json` holds the directory-classification thresholds, exclusion rules, and the in-progress detection settings; `naming/` holds the token vocabulary.
