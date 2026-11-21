# Registry Cleaner — Master Plan

**Status:** Planning (no code written yet)
**Target:** Windows 11 (Home and Pro), 64-bit
**Stack:** .NET 11 / C#
**Interfaces:** CLI (primary) + WPF desktop GUI
**Last updated:** 2026-11-20

---

## 1. What this program is

A registry cleanup tool for Windows 11 that **scans** the registry for stale, orphaned, and
leftover entries, **ranks every finding by severity**, and lets the **user choose** exactly what
gets removed.

It is not a "one-click deep clean" tool. The central design commitment is:

> The program never decides what is safe to delete. It produces an evidence-backed, severity-ordered
> list, and the user picks.

Three properties make that commitment real:

1. **Every finding carries evidence** — the missing file path, the missing GUID reference, the
   specific string that failed to resolve. No finding exists without a stated reason.
2. **Every removal is reversible** — a full backup is written and verified *before* the first
   byte of the registry is touched, and `restore` puts it back.
3. **The dangerous tiers are hard to reach** — high-risk categories are collapsed, never
   pre-selected, and require escalating confirmation.

## 2. Goals and non-goals

### Goals

| # | Goal | How it is measured |
|---|------|--------------------|
| G1 | Detect genuinely stale registry entries across the common categories (dead uninstall records, orphaned COM registration, broken shell integrations, stale startup entries) | Each category has a rule with a fixture test proving a known-stale key is found and a known-good key is not |
| G2 | Present findings in severity order with plain-English reasoning | Every finding has `Risk`, `Confidence`, `Reason`, and `Evidence`; the GUI shows all four |
| G3 | Make the user the decision-maker | Zero items are removed without explicit selection; the default selection set is empty except for the `Safe` tier, and even that requires a confirm click |
| G4 | Make every change reversible | A backup exists for 100% of applied removals; `restore` round-trips a fixture registry exactly |
| G5 | Ship as a single file with no runtime prerequisite | `dotnet publish` produces one `.exe`; verified by a release script. Measured: 64.3 MB self-contained + compressed, 138.4 MB uncompressed |
| G6 | Never damage the machine | The protected-path denylist is enforced in code, not just in rule authoring, and is covered by tests that try to bypass it |

### Non-goals (explicitly out of scope)

- **Debloating Windows.** Removing system components and features is categorically more dangerous
  than removing stale records, and the failure modes are unrecoverable without a restore point. Out
  of scope permanently.
- **Registry compaction / "reclaim disk space".** The popular claim that cleaners free gigabytes
  comes from hive compaction after deletion. That requires either `defrag.exe` with special flags
  or filesystem-level work, and the benefit is negligible on modern NTFS. The UI will not
  promise disk space; see §8.
- **Anything under `HKLM\SYSTEM` write-wise**, except a narrowly scoped, heavily gated printer
  cleanup (see `RULES.md`).
- **Deleting user data** — documents, credentials, browser profiles, MRU history beyond shell
  navigation residue.
- **Network calls, telemetry, or "community rules" download.** Fully offline and auditable.
- **Scheduled/unattended cleaning.** Cleaning without a human in the loop contradicts G3.

## 3. Severity model (the core of the user's requirement)

Every finding carries two independent scores, because "how bad is it" and "how sure am I" are
different questions and conflating them is how cleaners cause damage.

**Risk tier** — the consequence if the judgment turns out to be wrong:

| Tier | Name | Meaning | Example |
|------|------|---------|---------|
| S1 | Safe | Removing it cannot plausibly affect running software | Shell navigation residue pointing at a deleted file |
| S2 | Low | Cosmetic or administrative residue | A `Run` entry whose target executable no longer exists |
| S3 | Moderate | Could affect an application that is still installed but partially broken | An orphaned `CLSID` registration whose server DLL is gone |
| S4 | High | Could affect system or driver behavior | A stale printer driver registration under `HKLM\SYSTEM` |
| S5 | Critical | Not offered for removal at all — reported for awareness only | `PendingFileRenameOperations`, `HKLM\SAM`, boot configuration |

**Confidence** — 0–100, how strongly the evidence supports the finding:

| Range | Meaning |
|-------|---------|
| 90–100 | Verified: the referenced file is confirmed absent on disk, and cross-reference proves no other key depends on it |
| 70–89 | Strong: the referenced file is absent, but one supporting signal is missing |
| 40–69 | Speculative: pattern-based inference. Shown only with `--include-speculative`, never pre-selected, requires the highest confirmation level |
| 0–39 | Suppressed entirely — below the value of showing a user |

**Ordering.** Findings are grouped by risk tier in **ascending** risk order (safest first) and
sorted by reclaimed size descending within each tier. Rationale: the top of the list is the
obvious win set, and the riskier tiers stay visible but visually distinct and never pre-selected.
A sort toggle switches to descending risk for "audit the dangerous stuff first" workflows. See
`DECISIONS.md` ADR-006.

**Selection defaults.** On launch, only `S1` findings are pre-selected, and even those require an
explicit confirm click. `S2`–`S4` are unselected by default. `S5` is not selectable at all.

## 4. Architecture at a glance

```
                  ┌──────────────────────────┐
                  │   RegistryCleaner.Gui    │  WPF / MVVM
                  │   (WinExe, admin)        │
                  └────────────┬─────────────┘
                               │
                  ┌────────────▼─────────────┐
                  │   RegistryCleaner.Core   │  net11.0-windows library
                  │                           │
                  │  Rules  ──►  Analysis     │  reference index, confidence,
                  │              (confidence, │  risk tiering
                  │               risk tier)  │
                  │  Safety ──►  Denylist,    │  protected paths, elevation
                  │               Elevation   │
                  │  Remediation ─► Backup,  │  .reg + manifest, restore
                  │               Journal,    │
                  │               Apply       │
                  └────────────┬─────────────┘
                               │
                  ┌────────────▼─────────────┐
                  │   RegistryCleaner.Cli    │  System.CommandLine
                  │   scan / plan / apply /  │
                  │   restore / export       │
                  └──────────────────────────┘
```

The Core library has **no UI dependencies** and no direct registry writes outside the
remediation layer. This is what makes the rule engine unit-testable against a fake registry.

Detailed design: `ARCHITECTURE.md`. Safety model: `SAFETY.md`. Rules: `RULES.md`. Interfaces:
`UI.md`.

## 5. Execution flow

```
scan
 ├─ Read registry through a cached, read-only provider (both 64-bit and WOW6432Node views)
 ├─ Build the reference index (CLSID / ProgID / AppID / TypeLib GUID usage across scanned hives)
 ├─ Run every enabled rule → findings
 ├─ Score each finding: risk tier, confidence, evidence, estimated size
 └─ Emit: console table | JSON | HTML report

plan
 ├─ Select findings (by id, tier, rule, or search)
 └─ Write plan.json (immutable list of exact targets)

apply
 ├─ Verify elevation, refuse otherwise
 ├─ Re-verify each target still matches the expected state (no TOCTOU surprises)
 ├─ Write backup: backup.reg (regedit-compatible) + manifest.json + SHA-256
 ├─ Verify the backup is readable and complete
 ├─ Apply removals one at a time, journalling each outcome
 └─ Print undo instructions

restore
 ├─ Read manifest.json
 ├─ Recreate keys/values exactly (original types, binary data, multi-string ordering)
 └─ Offer to archive the restore point rather than delete it
```

The `plan` → `apply` split is deliberate: it lets the GUI and CLI share one execution engine, and
it makes an applied change always traceable to a reviewed, hash-verified plan file.

## 6. Safety model — summary

The full specification is in `SAFETY.md`. The non-negotiable rules:

1. **Dry run is the default.** `apply` requires an explicit `--apply` / confirm dialog.
2. **Backup before mutation, always.** No removal happens without a verified backup on disk.
3. **Protected paths are enforced in code.** A `ProtectedPathGuard` sits between the rule engine
   and the writer. Rules cannot opt out of it; it is not a rule-authoring convention.
4. **Reference counting, not guessing.** "Orphan" is only claimed when a cross-hive reference
   index proves nothing else points at the target.
5. **Escalating confirmation.** S1–S2: single confirm. S3: checkbox acknowledgment. S4: typed
   confirmation phrase.
6. **Everything is journalled.** Every action writes an audit entry: what, why, when, by which
   rule, with the evidence string.
7. **Fail closed.** Any error during backup or verification aborts the entire apply. A partial
   apply is never an acceptable outcome.

## 7. Milestones

Estimates assume one developer with AI assistance and a Windows 11 test machine.

| Milestone | Contents | Estimate |
|-----------|----------|----------|
| **M0 — Foundation** | Solution scaffold (`Core`, `Cli`, `Gui`, `Core.Tests`, `TestHarness`), CI, `IRegistryReader` abstraction + in-memory fake, protected-path guard with tests, decision log frozen | 1 day |
| **M1 — Read + first rules** | Real registry provider with caching and dual-view support, reference index, CLI `scan` with table + JSON output, rules R-001…R-005 | 2 days |
| **M2 — Rule catalog** | Full catalog through R-023, confidence scoring, risk tiering, evidence strings, HTML export | 3 days |
| **M3 — Apply + restore** | Backup writer (`.reg` + manifest), verification, journal, apply engine, `restore` command, round-trip fixture test | 3 days |
| **M4 — WPF GUI** | Scan → filter → review → select → clean → undo flow, detail pane, confirmation dialogs, elevation handling | 3 days |
| **M5 — Hardening + ship** | Test coverage, performance pass on large hives, single-file publish profiles, installer/bundle, user documentation | 2 days |

Total ≈ **2 weeks**. Milestones M0–M2 deliver a fully usable CLI. The GUI is the last piece, so the
product is valuable well before the schedule ends.

### Open source release engineering (spans M0–M5)

Because the project ships as open source under MIT (ADR-022), the following are requirements, not
extras:

| Requirement | Why | Milestone |
|-------------|-----|-----------|
| **GitHub Actions CI** — build + test on every push and PR | SignPath requires a *verifiable build origin*; a broken main branch is a broken project | M0 |
| **Reproducible release build** — pinned SDK via `global.json`, locked NuGet versions | A release that cannot be rebuilt from source cannot be verified | M0 |
| **Signed commits or at least signed release tags** | Lets users confirm the release came from this repository | M5 |
| **Release artifacts with published SHA-256 checksums** | Independent of any certificate; the strongest trust signal for a tool that asks for admin rights | M5 |
| **`SECURITY.md` with a private reporting channel** | Required by the SignPath Foundation's terms, and correct regardless | M0 (done) |
| **Documentation of every rule with its risk tier** | Lets users audit what the tool will do before running it | M2 (done — `RULES.md`) |
| **`winget` manifest** | The natural discovery channel for a developer/power-user Windows utility | M5 |

### Publish profiles

Both profiles produce one file; they differ in what the user must already have installed.

| Profile | Command | Size | Requirement on the target machine |
|---------|---------|------|-----------------------------------|
| Self-contained + compressed | `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` | **64.3 MB** (measured) | None |
| Self-contained, uncompressed | same without `EnableCompressionInSingleFile` | **138.4 MB** (measured) | None |
| Framework-dependent | `dotnet publish -r win-x64 --self-contained false -p:PublishSingleFile=true` | ~1 MB | .NET 11 Desktop Runtime |

The self-contained compressed profile is the default for distribution. The framework-dependent
profile is offered for managed corporate environments that already standardize on the runtime.
WPF is not trimmable, so the self-contained floor is high — a framework constraint, not a project
choice.

## 8. Honest framing of the value proposition

Registry cleaners have a deserved reputation for risk and a partly deserved reputation for
uselessness. This project takes a clear position:

- **What it actually does:** removes entries that point at things that no longer exist. That
  genuinely helps — phantom uninstall entries clutter Apps & Features, orphaned COM registrations
  slow Settings and Explorer, stale startup entries produce error dialogs.
- **What it does not do:** meaningfully free disk space. A few megabytes of registry data at most.
  The GUI and reports will not use "space recovered" as a headline metric; they show entry counts
  and estimated registry bytes instead.
- **How it avoids the usual failure:** by refusing to guess. A conservative cleaner with evidence,
  severity tiers, and a working undo is genuinely useful. An aggressive one is a liability.

## 9. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| A rule removes something an application still needs | Application breaks; user trust lost | Reference index + evidence + severity tiers + mandatory backup + tested restore |
| TOCTOU: registry changes between scan and apply | Wrong thing deleted | Apply re-verifies every target's current value against the scan snapshot; mismatch → skip and report |
| User runs the tool on a machine that is already unstable | Confounded diagnosis | Detect pending reboots, Safe Mode, and in-progress updates; refuse to apply and explain why |
| Accidentally deleting `PendingFileRenameOperations` | Breaks queued boot-time file operations | `S5`, structurally non-selectable |
| Windows Update mid-scan | Inconsistent snapshot | Snapshot is point-in-time per hive with a scan timestamp; apply re-verifies |
| AV flags the single-file exe | Users cannot run it | Signed binary before release; document the signing roadmap in M5 |
| 32-bit apps on 64-bit Windows | Missed or double-counted entries | Every rule explicitly declares which registry views it inspects; the reader exposes view as part of the key identity |
| Perf on huge hives | Scan takes minutes | Per-rule lazy evaluation, cached reads, parallel rule execution with a shared read-only cache |
| Scope creep toward debloating | Catastrophic failure potential | Non-goals in §2 are binding; debloat rules are explicitly refused in code review |

## 10. Open items for the user

1. **Signing certificate** — resolved in principle; the purchase itself is deferred to M5.
   Now that the project is open source under MIT (ADR-022), the cheapest route is **Certum Open
   Source Code Signing at €49/yr** (~$58 via their USD store), which issues to an individual and
   renders the publisher as "Open Source Developer, *name*". **SignPath Foundation** ($0) is the
   better long-term option once the repository has public traction, but the publisher shows
   "SignPath Foundation" rather than you. **EV is not worth buying** — Microsoft states it has had
   no SmartScreen advantage over OV since 2024. The **Microsoft Store route is closed to us**
   (ADR-021): the Store does not accept apps that require elevation, and this tool does by
   definition. **Recommendation: buy nothing yet** and revisit at M5, when there is an actual
   release to distribute.
2. **Installer vs. portable** — resolved by ADR-021: a **portable, self-contained EXE**, plus
   `winget` manifests for discovery. No installer.
3. **Scope of the first release** — the plan implements S1–S4 rules and keeps S5 report-only. If
   there is a specific stale category you have personally been bitten by, it should be prioritized
   into M2.
4. **Git repository** — resolved. `C:\git\registry_cleaner` is now its own repository on branch
   `main`, initialized independently of the surrounding `C:\git` workspace. The parent directory
   still holds unrelated projects and no commits; it was left untouched.
5. **Public or closed source** — resolved: public, MIT (ADR-022).
6. **GitHub organization / repository name** — still to decide before the first push. `README.md`
   and `CONTRIBUTING.md` currently use the placeholder `https://github.com/<your-username>/registry-cleaner`.

## 11. Companion documents

| Document | Purpose |
|----------|---------|
| `HOW-IT-WORKS.md` | Primer: registry mechanics, why junk accumulates, the three detection primitives, how apply and restore work |
| `DECISIONS.md` | Every design decision with rationale, alternatives considered, and consequences |
| `ARCHITECTURE.md` | Project layout, core types, rule API, scan and apply pipelines, CLI surface |
| `SAFETY.md` | Risk tiers, confidence bands, protected paths, backup/restore format, journal |
| `RULES.md` | The planned rule catalog with severity and confidence strategy |
| `UI.md` | CLI command reference and WPF GUI specification |
| `SIGNING.md` | Code signing options, current costs, and the recommendation for this project |
