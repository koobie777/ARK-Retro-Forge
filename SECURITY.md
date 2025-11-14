# Security Policy

## Supported Versions

We release patches for security vulnerabilities. Which versions are eligible for receiving such patches depends on the CVSS v3.0 Rating:

| Version | Supported          |
| ------- | ------------------ |
| Latest  | :white_check_mark: |

## Reporting a Vulnerability

Please report security vulnerabilities by emailing the project maintainers. Do not create public GitHub issues for security vulnerabilities.

## Security Guidelines

### What ARK-Retro-Forge Does NOT Include

**This software does NOT and will NEVER include:**

- ROM files (game images)
- BIOS files
- Encryption keys
- Copyrighted game content
- Tools that circumvent DRM or copy protection

### What This Software Does

ARK-Retro-Forge is a **management tool** for legally obtained ROM files. It provides:

- File organization and renaming
- Hash verification against DAT files
- Format conversion using external tools (CHD, CSO, RVZ)
- Emulator launcher functionality

### User Responsibility

Users are solely responsible for:

- Obtaining ROM files legally
- Complying with copyright laws in their jurisdiction
- Ensuring they have the right to use any ROM files
- Providing their own external tools (chdman, maxcso, etc.)

### External Tools

ARK-Retro-Forge does NOT download or bundle external tools. Users must:

- Download tools from official sources
- Place them in the `.\tools\` directory
- Verify tool integrity themselves
- Comply with each tool's license

### Telemetry

- Telemetry is **OFF by default**
- No data is collected without explicit user consent
- No personally identifiable information is collected
- No ROM file information leaves the user's machine

## Best Practices

1. Keep the software updated to the latest version
2. Only use tools from trusted, official sources
3. Scan external tools with antivirus software
4. Review the source code (it's open source!)
5. Use the `--dry-run` mode first before applying changes
6. Keep backups of your ROM collection

## Code Security

- All dependencies are regularly updated
- The build process is deterministic and reproducible
- CI/CD pipeline includes security scanning
- SBOM (Software Bill of Materials) is generated for all releases
