# .ark/ — Mission Tracking Directory

This directory contains mission lifecycle records for ARK Retro Forge.

## Structure

```
.ark/
├── missions/
│   ├── active/        # Currently in-flight missions
│   └── completed/     # Archived mission reports
└── state/
    └── current-mission.md  # Pointer to the active mission (if any)
```

## Protocol

See `CLAUDE.md` Governance Protocol section for the full mission lifecycle.

Brief summary:
1. Captain dispatches orders via chat
2. Commander creates `.ark/missions/active/YYYY-MM-DD-NAME.md` from template
3. Commander executes mission
4. Commander reports completion to Captain
5. On success: move file to `.ark/missions/completed/` with completion report appended
6. Update CHANGELOG.md and bump version

## Template

See `MISSION-TEMPLATE.md` in repo root for the template to copy when starting a mission.

---

*The ARK is meant for all.*
