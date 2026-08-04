# Documentation + First Run

**Brief for Claude Code.** `CLAUDE.md` is the authority. Read it first.

**No new features.** This phase writes documentation, cleans the repository, and pushes the branch. Phase 11 is the GUI and does not start here.

---

## The one rule for every document

**Every command shown must be executed, and its real output pasted.**

No invented examples, no plausible-looking flags, no output written from memory. Three separate defects in this build came from an artifact that looked correct while the mechanism underneath it wasn't — a scan exclusion matching leaf names, a sync catch filter listing five exception types, a package advisory with no patched version. Documentation fails the same way and nobody notices, because a wrong flag in a README is only discovered by a stranger who then leaves.

Start by enumerating the actual surface (`ark --help`, and `--help` on every verb). Document what exists, not what the phase briefs described.

---

## 1. `README.md` — rewrite for v2

The current one describes v1. Replace it.

Cover, in this order:

- **What it is.** One paragraph. Universal ROM management: identify, verify, dedupe, curate, report, rename, organize — for cartridge and disc systems.
- **The safety model, prominently.** This is the differentiator and it belongs above the feature list:
  - DRY-RUN is the default for every operation that writes
  - Nothing is ever deleted — removals go to quarantine with a manifest
  - Every write is journaled and reversed exactly by `ark undo`
  - Verification gates renaming: only hash-verified files receive canonical names
  - ARK never invents a name — canonical names come from the DAT entry the hash matched
- **Requirements and install.** .NET 8, portable, no installer.
- **Quickstart.** Five commands, real output, from zero to a first report.
- **Bring your own DATs and ROMs.** ARK ships neither. `ark dat sync` for Redump; No-Intro is a manual download imported with `ark dat import`, and say why (Datomatic needs a browser session, so scraping it would mean bundling one).
- **Status.** v2 is a ground-up rebuild. State what is supported (cartridge and disc resolution, verification, dedup, curation, reports, rename) and what is not yet (CHD conversion, ripping, GUI). Honest scope beats a feature list that overpromises.

---

## 2. `docs/USAGE.md` — full reference

Every verb, every flag, with real invocations and real output. Grouped by workflow rather than alphabetically:

**Setup** — `medical-bay`, `config`, `dat import`, `dat sync`, `dat list`
**Inspect** — `scan`, `parse`, `verify`
**Curate** — `dedupe`, the variant/curation verbs, the report verbs
**Act** — rename, organize
**Recover** — `journal list`, `journal show`, `undo`

For each: what it does, whether it writes, and what the exit codes mean.

Include a short section on **reading the states**, since they carry the design: Verified, In Progress, Mismatched, Unrecognized, Excluded — and what a user should actually do about each. In Progress especially: it means *skipped, not judged*, and a set copied minutes ago will be skipped wholesale until it settles.

---

## 3. `docs/FIRST-RUN.md` — the owner's walkthrough

The most important document here. Its audience is someone who wants to trust the tool with a collection before letting it touch anything.

**Structure it as risk-ascending**, and say so: read-only first, then DRY-RUN, then apply on a copy, then apply for real.

| Step | Command | Risk | What to look for |
|---|---|---|---|
| 1 | `medical-bay` | none | Tools found, DAT coverage per system |
| 2 | `dat sync` / `dat import` | none to collection | How many DATs resolved to a system |
| 3 | `scan <root>` | read-only | Do the ROM-set directories match what you'd call ROM sets? |
| 4 | `verify <set>` | read-only, slow | Does the verified count match what you'd expect of that set? |
| 5 | collection report | read-only | **Does the missing list contain anything you're certain you own?** |
| 6 | `dedupe` report-only | read-only | Do the duplicate groups look like duplicates to you? |
| 7 | rename DRY-RUN on a clean set | read-only | **Zero changes is the correct answer** |
| 8 | apply + undo **on a copy** | writes | Byte-identical afterwards |
| 9 | apply for real | writes | Journal recorded; `undo` available |

**Write the "what to look for" column as judgments, not instructions.** The reader is checking the tool against knowledge only they have. Step 5 is the sharpest: a title in the missing list that they know they own means identification has a gap, and that is exactly the kind of finding this walkthrough exists to produce.

Include explicit warnings:

- **Do not apply anything to a directory with active torrents.** ARK refuses them, but a seeding directory is not where anyone should discover whether a refusal works. Renaming a file an active client owns breaks the transfer, and on a completed torrent still seeding it breaks the seed silently.
- **Step 8 goes on a copy.** Not the original.
- **A freshly copied set will be skipped as In Progress.** Wait, or the run will look broken.
- **A first pass over a large set is slow** — verification reads every byte. The second is nearly free.

Show how to check the copy is byte-identical after step 8, with a real command.

---

## 4. `AGENTS.md` — replace

Still the v1-era file written for a different tool and a different assistant. Either replace it with a pointer to `CLAUDE.md` or delete it. Do not leave two documents claiming to be the authority.

---

## 5. Repository hygiene

- **`.gitattributes`** — the CRLF warnings have been accumulating since Phase 1:
  ```
  * text=auto eol=lf
  *.sln text eol=crlf
  ```
- **`.gitignore`** — `dat-list.txt` in the root is a stray output capture. Ignore it and remove it from the working tree.
- Confirm nothing else untracked belongs in the repo.

---

## 6. Push — and where to stop

```
git add -A
git commit -m "docs: v2 documentation, usage guide, first-run walkthrough"
git push -u origin v2
```

**Push the `v2` branch. Do not merge to `main`. Do not tag a release.**

`main` stays at v1.1.0 so current downloads keep working. The ship decision belongs to the owner after the first-run walkthrough, not to this phase.

---

## Gate

1. `dotnet build` succeeds with zero warnings; `dotnet test` succeeds
2. **Every command in every document was executed and its real output pasted**
3. Documented flags match `--help` exactly — no invented or renamed flags
4. `README.md` describes v2, leads with the safety model, and states honest scope including what is not yet supported
5. `docs/USAGE.md` covers every verb and flag, grouped by workflow, marking which ones write
6. `docs/FIRST-RUN.md` is risk-ascending, and its "what to look for" column states judgments the owner can make
7. The active-torrent, use-a-copy, freshly-copied, and first-pass-is-slow warnings are all present
8. `AGENTS.md` is replaced or removed — one authority, not two
9. `.gitattributes` and `.gitignore` added; `dat-list.txt` untracked
10. `v2` pushed; `main` untouched; no release tagged
11. All prior phase gates still pass

Then stop and report — including the exact commands the owner should run for the first-run walkthrough, so they can start from the report itself.
