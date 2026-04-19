# ARK Retro Forge — Strategic Roadmap

*Maintained by Captain Claude — Last updated Stardate 2026.04.19*

This document describes the strategic direction of ARK Retro Forge.
Tactical missions are dispatched separately through chat and logged in `.ark/missions/`.

---

## Current Phase — PSX Sector Hardening

**Status:** Active
**Target:** v1.2.0

The PlayStation sector is the foundation for all future systems.
All work in this phase hardens that foundation before expansion.

### Phase Goals

- [x] Fix critical PSX command bugs (P1–P4)
- [x] Establish architectural laws and governance
- [x] Pre-flight validation system
- [x] Scan performance (pure directory discovery)
- [x] Cache ownership clarified (scan writes, others read)
- [x] Convert purity enforced (format conversion only)
- [ ] Parallel workers for convert (4× HDD speed target)
- [ ] Medical Bay tool auto-updater
- [ ] Collection report (PSX)
- [ ] Shared service extraction (CueFileBuilder, DiscSetDetector, TrackDetector)
- [ ] P5 clean psx CUE generation verification
- [ ] Menu UX overhaul with guided workflow wizard
- [ ] Rename robustness stress tests (18-track, C&C SKUs, qualifiers)

---

## Next Phase — PS2 Sector Foundation

**Status:** Planned
**Target:** v1.3.0

Once PSX is bulletproof, PS2 implementation begins.
PS2 is simpler in many ways but introduces new considerations.

### Phase Goals

- [ ] Copy PSX sector as template for PS2
- [ ] DVD-aware chdman operations (`createdvd` instead of `createcd`)
- [ ] PS2 serial probe (SYSTEM.CNF `BOOT2=` path)
- [ ] PS2 DAT catalog integration
- [ ] Multi-disc PS2 titles (rare but exist)
- [ ] Verify all shared services (Mission 4 from PSX phase) work for PS2

---

## Future Phases

### Nintendo 64 Sector
- Z64 / V64 / N64 byte-order handling
- No multi-track concerns
- DAT via Redump and No-Intro
- Verify integrity via CRC32

### SEGA Sectors
- **Dreamcast** — GDI (GD-ROM format) and CDI support
- **Saturn** — CD-based, similar to PSX
- **Genesis / Mega Drive** — cartridge-based, simplest system

### Microsoft Xbox
- Original Xbox (DVD ISO, XISO format)
- Xbox 360 (later consideration)

### PSP Sector
- CSO compression via maxcso (tool already tracked)
- PBP format handling
- Game directory vs ISO

---

## Cross-Cutting Initiatives

### Tool Management
- [x] `ToolManager` walk-up resolution
- [ ] `ToolUpdater` auto-download from GitHub
- [ ] Checksum verification on tool updates
- [ ] Tool version compatibility matrix

### DAT Intelligence
- [x] Redump/No-Intro DAT parsing
- [x] Serial-based reverse lookup
- [ ] DAT verification with confidence scoring after rename
- [ ] Disambiguation chooser for ambiguous matches
- [ ] Support for custom user-supplied DATs

### Menu & UX
- [ ] Guided workflow wizard for new Admirals
- [ ] APPLY/DRY-RUN toggle always visible at top
- [ ] Workflow order matches canonical operations
- [ ] System switcher prominent in header
- [ ] Settings broken into clear categories
- [ ] Theme support (ARK cosmic, minimal, high-contrast)

### Collection Intelligence
- [ ] Collection report (per-system)
- [ ] Cross-format duplicate detection
- [ ] Completeness reporting vs DAT
- [ ] Export to JSON and HTML
- [ ] Watch mode for new downloads

### Preservation Features
- [ ] Archive-grade verification mode
- [ ] Batch profile system for repeatable workflows
- [ ] Conversion history log
- [ ] Emulator launch integration

---

## Governance Maturity

### Current (Tier 1 — Established)
- [x] CLAUDE.md constitution
- [x] CHANGELOG.md per mission
- [x] UPDATE.md per release
- [x] Mission logs under `.ark/missions/`
- [x] Semantic versioning discipline
- [x] Commit message conventions

### Near-term (Tier 2)
- [ ] Architecture Decision Records (`docs/adr/`)
- [ ] CONTRIBUTING.md for fleet growth
- [ ] PR template for when contributors join
- [ ] README badges (build, tests, version, license)

### Long-term (Tier 3)
- [ ] Commit message linting automation
- [ ] Automated release notes generation
- [ ] Code coverage reporting
- [ ] Documentation site (beyond markdown)

---

## Release Cadence

- **Dev builds** — continuous on `dev` branch push
- **RC builds** — on demand from `rc` branch when phase goals stable
- **Stable** — when RC validated in field by Admiral
- **Target:** one stable minor release per completed phase

---

## Fleet Growth Indicators

Metrics that indicate the ARK is serving its mission:

- GitHub stars (currently 27, climbing)
- Forks and PRs from community
- Issues filed (engagement signal)
- Diverse Admiral profiles represented (A through H)
- Systems supported (currently 1, expanding to 5+)
- Languages supported in UI (future — currently English only)

---

*Prime Directive: "The ARK is meant for all."*
*The roadmap serves the fleet. The fleet serves preservation.*
