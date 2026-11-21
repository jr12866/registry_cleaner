# Security Policy

Registry Cleaner runs with administrative privileges and writes to `HKLM`. It is a
security-sensitive tool, and we treat it that way.

---

## Reporting a vulnerability

**Please do not open a public GitHub issue for a security problem.**

Report it privately via **GitHub Security Advisories** on this repository
("Security" → "Report a vulnerability"), or by email to the maintainer.

Include:

- a description of the issue and its impact;
- the rule ID (`R-0xx`) involved, if you can identify it;
- the finding's **evidence string** and the **journal entry** from `%ProgramData%\RegistryCleaner\Journals\`;
- reproduction steps.

We aim to acknowledge a report within **7 days** and to ship a fix or a mitigation plan within
**30 days** of confirmation. Please give us that window before disclosing publicly.

## What counts as a security issue in this project

This tool's entire value proposition is that it will not remove the wrong thing. Accordingly:

| Counts as a vulnerability | Does not count |
|--------------------------|-----------------|
| A rule that removes a registry entry that is still in use, without evidence | A finding the user can see and chose not to select |
| A rule that deletes a protected path listed in `docs/SAFETY.md` §4.1 | A finding with an incorrect *explanation* but correct behavior |
| A bypass of `ProtectedPathGuard` (case, separators, 8.3 paths, `..` segments) | A low-confidence finding the user opted into with `--include-speculative` |
| The backup failing to restore a removed entry, so `restore` does not work | A partially failed apply that was correctly reported and remains restorable |
| An apply that proceeds when the backup verification failed | A finding in a tier the user was told not to touch |
| The tool making network requests it should not make | A SmartScreen warning or an AV heuristic detection |
| Sensitive data leaking into journals, reports, or evidence strings | The tool doing exactly what the user selected and confirmed |

## The security model, in brief

The full specification is in [`docs/SAFETY.md`](docs/SAFETY.md). The invariants:

1. A scan never writes to the registry.
2. No removal happens without a complete, verified backup on disk.
3. Protected paths cannot be removed by any rule, under any configuration.
4. Nothing is removed that the user did not explicitly select.
5. Every action is journalled.

If you find a way to violate any of these, that is a vulnerability. Please report it.

## Data handling

- The tool makes **no network requests**. There is no telemetry, no crash reporting, and no rule
  download. This is a deliberate architectural property, enforced by having no networking code in
  the application.
- Backups and journals are written to `%ProgramData%\RegistryCleaner\`. They contain registry paths
  and values from the machine the tool ran on. **Treat them as sensitive** — they may contain
  usernames, installed software names, and paths.
- Journal entries deliberately exclude values whose names suggest credentials (`Password`, `Token`,
  `Secret`, `LicenseKey`).
- Uninstalling the tool does not delete existing backups or journals. Remove
  `%ProgramData%\RegistryCleaner\` manually if you want them gone.

## Known limitations

- **SmartScreen and AV heuristics.** A signed binary shows a publisher name, but a tool that
  elevates and rewrites the registry can still be flagged on behavior. Signing is not a security
  boundary.
- **Trust in the rule catalog.** The tool's safety depends on its rules being correct. The
  reference index (`docs/RULES.md`) is the main defense against orphaned-COM false positives, but
  it is not a proof.
- **Restore is best-effort for keys deleted while another process held them open.** The journal
  records failures rather than silently dropping them.
- **No registry transaction support.** Windows offers none. A partially failed apply is reported
  with exit code 6 and remains restorable, but it is not atomic.

## Supported versions

| Version | Supported |
|---------|-----------|
| Latest release on `main` | ✅ |
| Previous release | ✅ (security fixes only) |
| Pre-release / `develop` | ❌ |
| Any version before the first tagged release | ❌ |

## Security-relevant defaults

- Scanning never requires elevation and never writes.
- Applying requires elevation **and** explicit confirmation, escalating by severity tier.
- The default selection is the `Safe` tier only, and even that requires a click.
- The tool refuses to apply in Safe Mode, with a pending reboot, or during an update.
