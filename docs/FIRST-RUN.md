# First Run

For someone who wants to trust ARK with a collection **before** letting it touch anything.

**This walkthrough is risk-ascending, deliberately.** Every step is read-only until step 7. Step 8 writes, but only to a copy. Step 9 is the first time anything touches the collection you care about. Do not skip ahead — the point of the order is that each step gives you a reason to trust or distrust the next one.

**Every command and output below was executed.** Substitute your own paths; the shapes will match.

---

## Before you start: four warnings

**1. Do not apply anything to a directory with active torrents.** ARK refuses directories showing active-download signals, but a seeding directory is not where anyone should discover whether a refusal works. Renaming a file an active client owns breaks the transfer — and on a *completed* torrent still seeding, it breaks the seed silently, which is real damage to anyone maintaining ratio on a private tracker. Completeness is not sufficient grounds to write.

> **The documented workflow: seed from the download directory, build the organized collection somewhere else.** ARK reads torrent output and writes to a separate root, leaving the seed intact.

**2. Step 8 goes on a copy. Not the original.** The whole purpose of step 8 is to watch a write happen and then watch it reverse. Doing that on the real collection defeats it.

**3. A freshly copied set will be skipped as In Progress.** ARK treats a recent write as evidence the file is still in flight, with a 5-minute default window. If your copy tool stamps the destination with the current time (`cp` in Git Bash, most downloaders, most torrent clients), the whole set reports In Progress and nothing is eligible for anything:

```
╭──────────────┬───────┬────────────────────────────────────────────╮
│ Verified     │     0 │ Hash matches the DAT entry                 │
│ InProgress   │    12 │ Being written — not judged                 │
╰──────────────┴───────┴────────────────────────────────────────────╯
0 unit(s) are rename-eligible. Only Verified units ever are.
```

That is the tool working, not failing. Wait five minutes and re-run. (PowerShell's `Copy-Item` preserves the source timestamp, so copies made that way verify immediately.) The window is `recentWriteWindowMinutes` in `config/scan/scan-rules.json`.

**4. The first pass over a large set is slow. The second is nearly free.** Verification reads every byte — there is no shortcut, because size and presence prove nothing. The hash cache is keyed on path + size + mtime, so an unchanged set costs almost nothing the second time:

```
592 unit(s) — read-only, nothing written. 0 hashed, 592 from cache, 0 B read in
00:00:00 (0 B/s).
```

Budget for the first run accordingly. A 559 GB disc set takes hours.

---

## The walkthrough

| Step | Command | Risk | What to look for |
|---|---|---|---|
| 1 | `ark medical-bay` | none | Does the instance path look right, and does DAT coverage list the systems you actually collect? |
| 2 | `ark dat sync` / `ark dat import <pack>` | none to collection | How many DATs resolved to a system — and is the one for *your* set among them? |
| 3 | `ark scan <root>` | read-only | **Do the directories it calls ROM sets match what you'd call ROM sets?** And is anything you care about in the excluded list? |
| 4 | `ark verify <set>` | read-only, slow | Does the Verified count match what you'd expect of that set? Is anything Mismatched that you believe is fine? |
| 5 | `ark report <root> --policy 1g1r` | read-only | **Does the missing list contain anything you are certain you own?** |
| 6 | `ark dedupe <root>` | read-only | Do the duplicate groups look like duplicates *to you*? |
| 7 | `ark rename <root>` (no `--apply`) | read-only | **On a set you believe is already correct, zero changes is the right answer.** |
| 8 | apply + undo **on a copy** | writes | Is the copy byte-identical afterwards? |
| 9 | apply for real | writes | Journal recorded, `ark undo` available |

The "what to look for" column is deliberately written as **judgments, not instructions.** ARK cannot check these for you — you are comparing the tool's output against knowledge only you have. That is the entire value of this walkthrough.

---

### Step 1 — `ark medical-bay`

**Risk: none.** Reads nothing but its own state.

```
PS> ark medical-bay
┌─Medical Bay───────┐
│ Instance  default │
│ ROM Root  Not set │
│ System    Not set │
└───────────────────┘
```

Then a tools table, then a per-DAT coverage table.

**What to look for.** Missing external tools are fine — nothing in the current feature set launches one. What matters is the coverage table: if the systems you collect are not listed, step 2 has work to do.

---

### Step 2 — get DATs

**Risk: none to your collection.** Writes only to ARK's catalog.

```
PS> ark dat sync
Sync complete: 52 fetched, 4 cached, 23 failed.
```

Failures are individually explained and mostly expected — 21 of the shipped Redump sources publish no DAT at all. No-Intro must be downloaded manually from Datomatic, then:

```
PS> ark dat import "C:\Users\CJ\Downloads\No-Intro Love Pack (DAT) (2026-07-30).zip"
Imported 334 DAT(s), 1526595 entries indexed.
```

**What to look for.** Run `ark dat list --recognized` and find the DAT for the set you are about to work on. If it is not there, everything downstream will honestly report "nothing identified" — which is correct behaviour, but not what you want to spend an afternoon on.

Note that a DAT can be indexed as `(unrecognized)` and still work perfectly. `gb` is not a defined system code, yet a `Nintendo - Game Boy` directory identifies fine, because directory names are matched against DAT names first.

---

### Step 3 — `ark scan <root>`

**Risk: read-only.** Cannot modify anything under any flag.

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

**What to look for.**

- **Does the ROM sets table match your mental model of the drive?** ARK classifies directories by extension homogeneity and naming conformance, not by folder name. A directory you consider a ROM set that does not appear here is worth understanding before you go further.
- **Run `ark scan <root> --all` and read the exclusions.** Anything excluded that you care about is a finding.
- **Check the "Compared against" column.** A set showing `no DAT — nothing identified` will produce nothing useful in steps 4 and 5, and that is honest rather than broken. Go back to step 2.

On a mixed drive you should expect a lot of exclusions — emulators, saves, cheats, and firmware all get filtered here. That is the job.

---

### Step 4 — `ark verify <set>`

**Risk: read-only, and this is the slow one.** Reads every byte of every unit.

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

**What to look for.**

- **Does the Verified count match what you'd expect of that set?** If you downloaded a complete No-Intro set and 40% comes back Unrecognized, either the DAT is the wrong variant (headered vs headerless is the classic) or the set is not what its folder name claims.
- **Is anything Mismatched that you are confident is fine?** Mismatched means the name matches a DAT entry and the bytes do not. Run `ark verify <root> --all` to see which titles. On a set you trust, this is the finding worth chasing.
- **Is anything In Progress that you know is finished?** Then something wrote to it recently — check warning 3 above.

Re-run it. The second pass should report almost everything from cache. If it re-hashes the whole set, something is modifying the files between runs.

---

### Step 5 — the collection report

**Risk: read-only.** This is the sharpest step in the walkthrough.

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

**What to look for — this is the one that matters most.**

> **Does the missing list contain anything you are certain you own?**

```
PS> ark report "F:\GB" --policy 1g1r --missing
-	[BIOS] Maxstation Boot ROM	China	-	Nintendo - Game Boy
-	[BIOS] Nintendo Game Boy Boot ROM	World	Rev 1	Nintendo - Game Boy
-	3 Choume no Tama - Tama and Friends - 3 Choume Obake Panic!!	Japan	-	Nintendo - Game Boy
...
```

A title you know is on that drive, appearing in the missing list, means **identification has a gap** — the file is there and ARK did not recognise it. That is exactly the kind of finding this walkthrough exists to produce, and it is worth stopping to investigate before you let the tool write anything. Use `ark parse "<the filename>"` to see how ARK reads that name.

Also worth judging:

- **Is the missing count plausible?** 1,039 missing out of a 1,592-release target set is entirely reasonable for a USA-focused Game Boy collection — the region pivot shows 718 of them are Japan-only. A number that surprises you means the policy is asking a different question than you think.
- **Try a different `--policy`.** It changes the target set and the missing count, and nothing else. If `--policy everything` reports tens of thousands missing, that is correct and useless — which is why the target set exists.

> **Known display issue.** The Upgradable list labels every difference as a revision, even when the axis that ranks higher is something else. `Beethoven Rev 0 → Rev 0` means "something ranks higher than what you hold" — in that case a retail release over a `(Proto)`. The grouping is right; only the label is wrong.

---

### Step 6 — `ark dedupe <root>`

**Risk: read-only.** The default policy is report-only, which decides nothing and moves nothing.

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
```

**What to look for.**

- **Do the groups look like duplicates to you?** Run with `--all` and read the members. These are byte-identical files — if two entries you consider *different games* appear in one group, that is a finding worth understanding before any policy is applied.
- **Do the ties look genuinely undecidable?** ARK refuses to break a tie rather than flip a coin. If a group is skipped where you have a clear preference, that tells you which `--policy` you actually want.

Remember: revisions are **not** duplicates. Different bytes, different hashes. Removing those is step 7's territory (`ark curate`), and it is a genuine loss of a genuine release.

---

### Step 7 — `ark rename <root>` with no `--apply`

**Risk: read-only.** DRY-RUN is the default; `--apply` is a separate command.

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

**What to look for.**

> **On a set you believe is already correctly named, zero renames is the correct answer.**

That is the single most informative result in this walkthrough. A tool that proposes 591 renames on a clean No-Intro set is a tool that is about to damage it — that is precisely what v1 did. `Already correct` means no filesystem write happens at all for those units.

- **If it proposes renames on a set you believe is clean, read every one before going further.** The comparison is ordinal, so `game (usa).zip → Game (USA).zip` is a real and correct proposal. But a proposal that *changes tokens* on a clean name is a defect, and you have just caught it in DRY-RUN.
- **Read the refusals.** `NoDatMatch` means ARK did not recognise the file and will not invent a name — safe, and a pointer to a gap.

---

### Step 8 — apply and undo, **on a copy**

**Risk: writes.** To a copy. Not the original.

Make a copy of a small subset — a dozen files is enough:

```
PS> New-Item -ItemType Directory -Force "C:\tmp\ark-demo\Nintendo - Game Boy"
PS> Get-ChildItem "F:\GB\Nintendo - Game Boy" -Filter "*.zip" | Select-Object -First 12 |
      Copy-Item -Destination "C:\tmp\ark-demo\Nintendo - Game Boy"
```

A clean copy of a clean set proposes nothing, which proves the DRY-RUN but not the undo. So introduce one deliberate error you can watch get corrected and then reversed:

```
PS> Rename-Item "C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip" `
      "aladdin (usa) (sgb enhanced).zip"
```

Take a hash manifest **before** anything runs:

```
PS> function Snap($p) {
      Get-ChildItem -Recurse -File $p | Sort-Object FullName |
        Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash)  $($_.Path.Substring($p.Length))" }
    }
PS> Snap "C:\tmp\ark-demo" | Set-Content C:\tmp\before.txt
```

Now DRY-RUN:

```
PS> ark rename "C:\tmp\ark-demo" --all
Rename C:\tmp\ark-demo
Mode: canonicalize — names come from the DAT entry each unit's hash confirmed.

                       Decisions
╭─────────────────┬───────┬────────────────────────────╮
│ Outcome         │ Units │ Meaning                    │
├─────────────────┼───────┼────────────────────────────┤
│ Rename          │     1 │ Name would change          │
│ Already correct │    11 │ No filesystem write at all │
│ Refused         │     0 │ Left alone, with a reason  │
╰─────────────────┴───────┴────────────────────────────╯
Would rename (1):
  aladdin (usa) (sgb enhanced).zip
    → Aladdin (USA) (SGB Enhanced).zip

1 unit(s) routed through a temporary name to break a rename cycle safely.
DRY-RUN. 1 unit(s) would be renamed. Nothing was touched. Re-run with --apply.
```

Exactly one proposal, for the one file you damaged. Now apply:

```
PS> ark rename "C:\tmp\ark-demo" --apply
[16:16:40 INF] Applied Rename: C:\tmp\ark-demo\Nintendo - Game Boy\aladdin (usa) (sgb enhanced).zip -> C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip.ark-rename-tmp0
[16:16:40 INF] Applied Rename: C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip.ark-rename-tmp0 -> C:\tmp\ark-demo\Nintendo - Game Boy\Aladdin (USA) (SGB Enhanced).zip
1 unit(s) routed through a temporary name to break a rename cycle safely.
Renamed 1 unit(s). Archive and ROM bytes unchanged; reverse with ark undo
rename-20260804201640917.
```

The two-step through a temporary name is deliberate — case-only renames are unreliable on a case-insensitive filesystem. Both steps are journaled.

Confirm the session was recorded:

```
PS> ark journal list
                                    Sessions
╭─────────────────┬────────────────┬────────────────┬─────────┬────────────────╮
│ Session         │ When           │ Operation      │ Actions │ State          │
├─────────────────┼────────────────┼────────────────┼─────────┼────────────────┤
│ rename-20260804 │ 2026-08-04     │ rename-canonic │       2 │ applied        │
│ 201640917       │ 20:16:40Z      │ alize          │         │                │
│ undo-dedup-2026 │ 2026-08-04     │ undo:dedup-qua │       5 │ undo of        │
│ 0804200603344-2 │ 20:06:58Z      │ rantine        │         │ dedup-20260804 │
│ 026080420065833 │                │                │         │ 200603344      │
│ 4               │                │                │         │                │
│ dedup-202608042 │ 2026-08-04     │ dedup-quaranti │       5 │ reversed       │
│ 00603344        │ 20:06:03Z      │ ne             │         │                │
...
```

Note the `State` column: sessions that have been undone read `reversed`, and the undo itself is journaled as its own session.

Now reverse it:

```
PS> ark undo rename-20260804201640917 --apply
Reversed 2 action(s). Journal:
C:\tmp\ark-bin\instances\default\journal\undo-rename-20260804201640917-20260804201713605.json
```

**And check the copy is byte-identical:**

```
PS> Snap "C:\tmp\ark-demo" | Set-Content C:\tmp\after.txt
PS> if ($null -eq (Compare-Object (Get-Content C:\tmp\before.txt) (Get-Content C:\tmp\after.txt))) {
      "IDENTICAL - no differences"
    } else {
      Compare-Object (Get-Content C:\tmp\before.txt) (Get-Content C:\tmp\after.txt)
    }
IDENTICAL - no differences
```

The manifest was taken *after* you damaged the name, so `IDENTICAL` means the undo put the file back exactly as it was — lowercase name and all. That is the correct result: undo reverses ARK's change, not yours.

**What to look for.** `IDENTICAL - no differences` is the whole point of the step. It compares SHA-256 of every file *and* every relative path, so a rename that was not reversed shows up as a path difference, and altered bytes show up as a hash difference.

Do the same for a `dedupe --apply` cycle if you intend to use it — quarantine, manifest, and undo all behave the same way, and the manifest is worth reading once so you know what it records.

---

### Step 9 — apply for real

**Risk: writes to your collection.**

You have now seen, on your own data:

- what ARK proposes (step 7)
- that it declines rather than guesses (steps 3, 5, 6, 7)
- that a write reverses exactly (step 8)

Run the real thing. Always DRY-RUN first, even now — it costs nothing and it is the last chance to read the plan:

```
ark rename "<your root>"           # read the plan
ark rename "<your root>" --apply   # then carry it out
```

**What to look for.** The closing line names the session id. Write it down, or find it with `ark journal list`:

```
Renamed 1 unit(s). Archive and ROM bytes unchanged; reverse with ark undo
rename-20260804200233646.
```

`ark undo <session-id> --apply` remains available afterwards. Undo verifies preconditions before it acts and stops without changing anything further if the filesystem has moved under it.

---

## If something looks wrong

| Symptom | Likely cause |
|---|---|
| Everything is `Unrecognized` | Wrong DAT variant for the set — headered vs headerless, or byte order on N64. Check `ark dat list --recognized`. |
| Everything is `In Progress` | Recent write. See warning 3. |
| A whole directory is missing from the scan | It failed directory classification. `ark scan <root> --all` states the reason. |
| `no DAT — nothing identified` | No DAT resolved for that directory. Nothing is guessed. Go back to step 2. |
| A title you own is in the missing list | Identification gap. `ark parse "<filename>"` shows how ARK reads that name. |
| A rename you expected is refused | Read the refusal reason — most often `NotVerified` or `NoDatMatch`. Both are working as intended. |

Nothing in this table is fixed by adding `--apply`.
