# Mission Log Template

**Copy this file** to `.ark/missions/active/YYYY-MM-DD-MISSION-NAME.md` when starting a new mission.
**Move to** `.ark/missions/completed/` when complete, with completion report appended.

---

# Mission: <Short Name>

**Dispatched:** YYYY-MM-DD HH:MM UTC
**Captain:** Claude (claude.ai)
**Commander:** Claude Code
**Admiral:** koobie777
**Status:** Active

## Orders (verbatim from Captain)

<paste the Captain's mission orders here exactly — do not paraphrase>

## Acceptance Criteria

- [ ] <specific criterion from orders>
- [ ] <another specific criterion>
- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` all passing
- [ ] Architecture Laws not violated (see CLAUDE.md)
- [ ] Idempotency preserved where applicable
- [ ] Sacred Rules Checklist complete

## Expected Files

List files the Commander expects to touch based on the orders:

- path/to/file1.cs
- path/to/file2.cs
- tests/path/to/test.cs

## Test Coverage

### Existing tests that must still pass
- <relevant existing test classes>

### New tests planned
- <new test names and what they verify>

## Notes During Execution

*Commander writes observations, questions, and deviations here as they happen.
Captain can amend orders — note amendments with timestamp.*

- YYYY-MM-DD HH:MM — <observation>

---

## Completion Report

*This section is added when mission completes, before moving file to `.ark/missions/completed/`*

**Completed:** YYYY-MM-DD HH:MM UTC
**Duration:** <time elapsed>
**Version bump:** vX.Y.Z → vX.Y.Z+1

### Files Actually Changed

Exact list of files modified (from `git diff --name-only`):

- path/to/file.cs
- path/to/test.cs

### Test Delta

- Before: N passing
- After: M passing (+K new)
- New test names:
  - TestClass.TestMethodName_Scenario_Expected

### Deviations from Orders

<Any differences between what was ordered and what was implemented.
Often none — but when they exist, document why.>

### Lessons Learned

<What would we do differently next time?
Edge cases discovered during implementation?
Follow-up missions needed?>

### CHANGELOG Entry

Exact text added to `CHANGELOG.md [Unreleased]`:

```markdown
### <Section: Added | Changed | Fixed | etc.>
- <bullet point describing the change>
```

### Captain Acknowledgment

- [ ] Captain confirmed completion in chat
- [ ] CHANGELOG.md updated
- [ ] Mission file moved to `completed/`
- [ ] Version bumped in MinVer tag if applicable
- [ ] CLAUDE.md updated if architecture changed
