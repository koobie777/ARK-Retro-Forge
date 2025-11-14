# Pull Request

## Description

<!-- Provide a brief description of the changes in this PR -->

## Related Issues

<!-- Link to any related issues using #issue_number -->

Fixes #

## Type of Change

<!-- Check all that apply -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Code refactoring
- [ ] Test improvements
- [ ] CI/CD changes

## Checklist

### Testing

- [ ] I have run the affected commands with `--dry-run --verbose` and verified the output
- [ ] I have included the dry-run logs in the PR description or as a comment
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] All new and existing tests pass locally
- [ ] I have tested with the `--apply` flag (if applicable)

### Code Quality

- [ ] My code follows the project's .editorconfig style guidelines
- [ ] I have run `dotnet format --verify-no-changes` and there are no issues
- [ ] I have performed a self-review of my own code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] My changes generate no new warnings or errors
- [ ] I have enabled and addressed all nullable reference type warnings

### Security & Policy Compliance

- [ ] **NO-ROM POLICY**: This PR does NOT include any ROM files, BIOS files, encryption keys, or copyrighted game content
- [ ] **NO-ROM POLICY**: This PR does NOT add functionality to download ROMs, BIOS, or copyrighted content
- [ ] **NO-ROM POLICY**: This PR does NOT circumvent DRM or copy protection
- [ ] I have reviewed the SECURITY.md file and my changes comply with all policies
- [ ] If adding external tool integration, I have ensured it only looks in `.\tools\` directory
- [ ] If adding dependencies, I have run `dotnet restore` and verified no security vulnerabilities

### Documentation

- [ ] I have updated the relevant documentation (README.md, UPDATE.md, code comments)
- [ ] I have updated the help text for any CLI commands I modified
- [ ] If adding a new command, I have documented it in UPDATE.md

### Build & Release

- [ ] My changes work in a single-file published executable
- [ ] I have verified the build is deterministic (multiple builds produce identical output)
- [ ] If changing dependencies, the published EXE remains under 25 MB

## Dry-Run Output

<!-- 
REQUIRED for changes affecting CLI commands:
Paste the output from running your command with --dry-run --verbose 
Use code blocks with triple backticks
-->

```
<!-- Paste dry-run output here -->
```

## Test Coverage

<!-- Describe the tests you added or modified -->

- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Golden file tests added/updated (for rename functionality)
- [ ] Hash stream tests added/updated (for verify functionality)

## Screenshots (if applicable)

<!-- Add screenshots or terminal output to help explain your changes -->

## Breaking Changes

<!-- If this is a breaking change, describe what breaks and the migration path -->

## Additional Notes

<!-- Any additional information that reviewers should know -->

## Reviewer Notes

<!-- For maintainers: anything specific to look for during review? -->
