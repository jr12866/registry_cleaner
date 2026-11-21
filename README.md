# Registry Cleaner

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Windows](https://img.shields.io/badge/platform-Windows%2011-0078D4?logo=windows)](https://learn.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-11-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

A registry cleanup tool for **Windows 11** that scans the registry for stale, orphaned, and
leftover entries, **ranks every finding by severity**, and lets **the user choose** exactly what
gets removed.

> **Status: milestone M0 complete.** The solution scaffold, the registry abstraction layer,
> the protected-path safety guard, the test harness, and the CI workflow are in place and
> building clean. The first detection rules arrive in M1.

---

## The idea

Most registry cleaners make a decision for you and hope it is the right one. That is how machines
get broken. This tool makes a different bargain:

- Every finding carries **evidence** — the missing path, the unreferenced GUID, the exact string
  that failed to resolve.
- Every finding carries a **severity tier** — how much damage a wrong call would cause.
- **Nothing is removed that you did not select.**
- Every removal is **reversible** — a verified backup is written before the first byte changes, and
  `restore` puts it back.

## Documentation

| Document | What it covers |
|----------|----------------|
| [`docs/HOW-IT-WORKS.md`](docs/HOW-IT-WORKS.md) | **Start here.** How the registry works, why stale entries accumulate, the three detection primitives, and what separates a good cleaner from a dangerous one |
| [`docs/PLAN.md`](docs/PLAN.md) | Goals, non-goals, the severity model, milestones, risks |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | 20 architecture decision records with rationale and rejected alternatives |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Solution layout, core types, scan and apply pipelines, testing strategy |
| [`docs/SAFETY.md`](docs/SAFETY.md) | Risk tiers, confidence bands, protected paths, backup/restore format, verification checklist |
| [`docs/RULES.md`](docs/RULES.md) | The 23-rule catalog with detection logic, tiers, and confidence baselines |
| [`docs/UI.md`](docs/UI.md) | CLI command reference and the WPF GUI specification |
| [`docs/SIGNING.md`](docs/SIGNING.md) | Code signing options, current costs, and the recommendation for this project |

## Decisions already made

| Decision | Choice |
|----------|--------|
| Stack | .NET 11 / C#, self-contained single-file executable |
| Cleaning model | Findings listed in severity order; **the user selects what to clean** |
| Interfaces | CLI (primary) + WPF desktop GUI over one shared core |
| Safety | Dry run by default, mandatory verified backup, protected paths enforced in code, escalating confirmation by tier |
| Scope | No debloating, no registry compaction, no network calls, no telemetry |
| Distribution | Portable signed EXE via GitHub Releases + `winget`. **Not** the Microsoft Store — elevation is disqualifying ([ADR-021](docs/DECISIONS.md#adr-021)) |
| License | **MIT**, open source, in a dedicated repository ([ADR-022](docs/DECISIONS.md#adr-022)) |

## Repository layout

```
registry-cleaner/
├── LICENSE                  MIT
├── CONTRIBUTING.md          how to build, and the rules a new detection rule must follow
├── SECURITY.md              vulnerability reporting, and what counts as one here
├── CODE_OF_CONDUCT.md
├── RegistryCleaner.slnx     the solution
├── global.json              pinned .NET 11 SDK
├── Directory.Build.props    shared build settings
├── Directory.Packages.props central package versions
├── .github/workflows/       CI: build, test, publish, reproducible-build check
├── docs/                    the design documents
├── src/
│   ├── RegistryCleaner.Core/    rules, scoring, safety, remediation — no UI types
│   ├── RegistryCleaner.Cli/     the `rc` command-line interface
│   └── RegistryCleaner.Gui/     the WPF desktop application
└── tests/
    ├── RegistryCleaner.TestHarness/  in-memory registry + .reg-style fixtures
    └── RegistryCleaner.Core.Tests/   the xUnit suite
```

## Planned capabilities

- **23 rules** across stale application registrations, orphaned COM registration, broken shell
  integration, stale startup entries, orphaned installer records, stale fonts/printers/DSNs, and
  shell navigation residue.
- **Five risk tiers** (Safe → Critical) and an independent **0–100 confidence** score, so "how
  risky" and "how sure" are never conflated.
- **Severity-ordered listing** with per-finding plain-English explanation and raw evidence.
- **Backup and restore**: a regedit-compatible `.reg` file plus a machine-readable manifest with
  SHA-256 verification.
- **CLI**: `scan`, `plan`, `apply`, `restore`, `backups`, `verify`, `rules`, `export`.
- **WPF GUI**: severity tree with counts, sortable findings grid, detail pane with evidence,
  tiered confirmation dialogs, backup history.

## What it will not do

- **Debloat Windows.** Out of scope permanently.
- **Reclaim disk space.** Registry cleaning frees kilobytes to a few megabytes at most. The tool
  reports entry counts, not inflated space savings.
- **Touch anything it cannot justify.** No finding without evidence; no removal without a backup;
  no write to protected paths under any circumstance.

## Build

Requires the **.NET 11 SDK** (pinned in `global.json`).

```powershell
git clone https://github.com/jr12866/registry_cleaner.git
cd registry-cleaner

dotnet build RegistryCleaner.slnx -c Release
dotnet test  RegistryCleaner.slnx -c Release

# Run the CLI
src\RegistryCleaner.Cli\bin\Release\net11.0-windows\rc.exe --help

# Produce the portable single-file GUI executable
dotnet publish src\RegistryCleaner.Gui\RegistryCleaner.Gui.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Measured on the target machine: **64.4 MB** self-contained compressed single-file executable
(138.4 MB uncompressed; WPF cannot be trimmed).

> The solution uses the `.slnx` XML format, which needs the .NET 9.0.200 SDK or later and
> Visual Studio 17.13 or later. The project already requires the .NET 11 SDK, so this costs
> nothing.

## Environment verified on 2026-11-20

| Item | Status |
|------|--------|
| OS | Windows 11 Home, 10.0.26300 |
| .NET SDK | 11.0.100 with WindowsDesktop runtime 11.0.0 |
| `Microsoft.Win32.Registry` | Available from the shared framework; `net11.0-windows` target silences CA1416 |
| Registry views | Both 64-bit and `WOW6432Node` present and readable |
| WPF | `dotnet new wpf` + Release build succeed |
| Single-file publish | Verified; sizes above |
| Elevation | The development session is **not** elevated — the tool must handle UAC relaunch |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and — importantly — the checklist a
new detection rule has to pass before it can be merged. If you find a bug that caused damage, read
[SECURITY.md](SECURITY.md) first instead of opening an issue.

## License

[MIT](LICENSE) © 2026 Joe
