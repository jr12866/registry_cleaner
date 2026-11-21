# Contributing to Registry Cleaner

Thanks for helping. This document explains how to build the project, how the code is structured,
and — importantly — the rules a new detection rule has to follow before it can be merged.

---

## Code signing policy

> Free code signing provided by [SignPath.io](https://about.signpath.io/product/open-source),
> certificate by SignPath Foundation.

*(This note is required by the SignPath Foundation's terms. Keep it in place.)*

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | **11.0** or later |
| Windows | Windows 11 (the project targets `net11.0-windows`) |
| IDE | Rider, Visual Studio 2022 17.13+, or VS Code with the C# extension |

## Build and test

```powershell
git clone https://github.com/<your-username>/registry-cleaner.git
cd registry-cleaner

dotnet build RegistryCleaner.sln -c Release
dotnet test  RegistryCleaner.sln -c Release
```

There is no solution file yet — it is created in milestone M0. Until then, see `docs/PLAN.md`.

## Project layout

| Path | Contents |
|------|----------|
| `src/RegistryCleaner.Core` | All rule logic, scoring, safety enforcement, and remediation. No UI dependencies. |
| `src/RegistryCleaner.Cli` | The `rc` command-line interface |
| `src/RegistryCleaner.Gui` | The WPF desktop application |
| `tests/RegistryCleaner.TestHarness` | In-memory fake registry and rule fixtures |
| `tests/RegistryCleaner.Core.Tests` | xUnit test suite |
| `docs/` | The design documents — **read `docs/SAFETY.md` before touching anything that writes** |

**The single most important structural rule:** `RegistryCleaner.Core` must never reference the
CLI or the GUI, and rule logic must never live in a front-end. If a rule needs behavior, it goes in
`Core`.

## Adding a detection rule

Rules are the risk surface of this project. A rule that removes something it should not is a
security bug, not a bug. Before opening a pull request:

1. **Read `docs/SAFETY.md` first**, especially §2 (risk tiers), §3 (confidence), and §4
   (protected paths).
2. **Register the rule** in `RuleCatalog` with a stable ID, name, category, and inherent risk tier.
   IDs are never reused — `R-005` means `R-005` forever, so retired rules leave a gap.
3. **Produce evidence.** Every finding must carry a concrete, verifiable fact — the missing path,
   the unreferenced GUID. A finding without evidence will be rejected in review.
4. **Use `IFileSystemProbe`** and treat `Unverifiable` as a distinct outcome from `Missing`. A
   disconnected network share is *not* a missing file. This is the single most common source of
   false positives in registry cleaners.
5. **Consult the reference index** before making any "orphan" claim. Absence of a reference must be
   *proven*, not assumed.
6. **Never target a protected path.** `ProtectedPathGuard` will block it, but a rule that tries is a
   rejected pull request.
7. **Write both fixtures.** A "must find" tree that produces exactly N findings, and a "must not
   find" tree that produces zero. See the fixtures in the test harness.
8. **Update `docs/RULES.md`** with the new rule's tier, confidence baseline, and detection logic.

### Risk tier guidance

| Tier | Use it for |
|------|------------|
| S1 Safe | Shell residue that cannot affect running software |
| S2 Low | Administrative residue of a program that is no longer installed |
| S3 Moderate | Registration that could affect an application that is still installed |
| S4 High | Anything touching system, driver, or shell behavior |
| S5 Critical | Report-only. Never selectable. |

When in doubt, choose the higher tier. Users can always select a riskier item deliberately; they
cannot un-break their machine.

## Commit and pull request conventions

- **One logical change per pull request.** A new rule plus its fixtures and docs is one PR. A
  refactor plus a new rule is two.
- **Commit messages** use the conventional prefix: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`,
  `chore:`.
- **Write the commit body for the "why."** The diff already shows the "what."
- **Reference the issue** the change addresses, and reference the rule ID in the body when a rule
  is added or changed.
- **Run the tests** before pushing. `dotnet test -c Release` must be clean.
- **Do not include binaries, signing material, or `.reg` backups** in a commit. `.gitignore`
  already excludes them, but check before pushing.

## Adding a dependency

- It must be **MIT / Apache-2.0 / BSD / MS-PL compatible** with our MIT license.
- It must not introduce **network access** into the tool. The application makes no network calls —
  that is a deliberate security property, not an oversight. See `DECISIONS.md` ADR-013.
- It must not add **telemetry, crash reporting, or usage analytics**.
- Prefer the .NET base class library over a third-party package where it is a reasonable fit.

## Reporting a bug

Open a GitHub issue. Please include:

- your Windows version and whether you are running in Safe Mode;
- the exact rule ID involved, if you know it;
- the **evidence string** from the finding (the GUI's detail pane shows it);
- the relevant journal entry if the tool already removed something.

**If the bug caused damage**, follow `SECURITY.md` instead — do not open a public issue.

## License

Contributions are accepted under the [MIT License](LICENSE). By submitting a pull request you
confirm that you have the right to contribute the code and that it is licensed under MIT.
