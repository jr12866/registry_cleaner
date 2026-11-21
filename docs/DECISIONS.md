# Decision Log

Architecture decision records for Registry Cleaner. Each entry records the context, the decision,
the alternatives that were genuinely considered, and the consequences — including the ones that
cost us something.

Status values: `Accepted` (in force) · `Superseded` (replaced later) · `Revisited` (under review).

| # | Decision | Status |
|---|----------|--------|
| [ADR-001](#adr-001) | Build on .NET 11 / C# | Accepted |
| [ADR-002](#adr-002) | Severity-ordered findings; the user chooses what to clean | Accepted |
| [ADR-003](#adr-003) | Separate risk tier from confidence score | Accepted |
| [ADR-004](#adr-004) | Ship both a CLI and a WPF GUI over one shared core | Accepted |
| [ADR-005](#adr-005) | Rule engine as pluggable `IRegistryRule` implementations | Accepted |
| [ADR-006](#adr-006) | Sort findings by ascending risk, with a descending toggle | Accepted |
| [ADR-007](#adr-007) | Dry run by default; mandatory verified backup before any mutation | Accepted |
| [ADR-008](#adr-008) | Protected-path enforcement lives in code, not in rule discipline | Accepted |
| [ADR-009](#adr-009) | Prove "orphan" with a cross-hive reference index | Accepted |
| [ADR-010](#adr-010) | Debloating and registry compaction are permanently out of scope | Accepted |
| [ADR-011](#adr-011) | Target `net11.0-windows`; Core is a UI-free library | Accepted |
| [ADR-012](#adr-012) | Treat registry view as part of a key's identity | Accepted |
| [ADR-013](#adr-013) | Fully offline, no telemetry, no remote rule updates | Accepted |
| [ADR-014](#adr-014) | Separate `plan` and `apply` commands with an immutable plan file | Accepted |
| [ADR-015](#adr-015) | No finding without evidence | Accepted |
| [ADR-016](#adr-016) | Default selection is the `Safe` tier only, still requiring confirmation | Accepted |
| [ADR-017](#adr-017) | Test every rule against an in-memory fake registry | Accepted |
| [ADR-018](#adr-018) | Audit journal for every action | Accepted |
| [ADR-019](#adr-019) | Publish as a self-contained single-file executable | Accepted |
| [ADR-020](#adr-020) | Do not market disk-space savings | Accepted |
| [ADR-021](#adr-021) | Distribute as a portable EXE, not through the Microsoft Store | Accepted |
| [ADR-022](#adr-022) | Release as open source under the MIT License, in its own repository | Accepted |

---

## ADR-001

**Build on .NET 11 / C#**

**Context.** The tool must read and write the Windows registry natively, ship to end users without
a prerequisite runtime, and present a desktop GUI. Three toolchains were viable in the target
environment: .NET 11 (SDK 11.0.100, WindowsDesktop runtime 11.0.0 present), Python 3.11.14
(conda-forge), and PowerShell 7.

**Decision.** .NET 11 / C# for all projects. `Microsoft.Win32.Registry` is used directly from the
shared framework — no third-party registry wrapper.

**Alternatives considered.**
- *Python* — fastest to iterate, and `winreg` is in the standard library. Rejected because it makes
  the end-user story worse: users would need a Python install, and a cleanup tool asking users to
  install a language runtime before they trust it with their registry is a poor first impression.
- *PowerShell* — natural for registry work and already present, but it is a scripting language, not
  one that produces a maintainable GUI alongside a testable core. Script-based delivery also makes
  "single file with no prerequisite" hard.
- *C++/Win32* — the most direct registry access, but disproportionate development cost for a tool
  whose complexity is in the rule engine and safety model, not in the API calls.

**Consequences.** We get AOT-friendly, strongly typed, testable code, native WPF, and a single-file
publish. We accept a ~64 MB self-contained binary, because WPF cannot be trimmed. We also accept
that the toolchain requires the .NET 11 SDK to be installed for development.

---

## ADR-002

**Severity-ordered findings; the user chooses what to clean**

**Context.** The user set this requirement directly, overriding an offered choice between a
conservative allowlist and aggressive heuristics: *"Let's make it where things are listed in order
of severity and the user chooses what to clean."* The question this decision must answer is
whether severity means *how risky the removal is* or *how valuable the finding is*.

**Decision.** Findings are grouped into risk tiers (S1 Safe → S5 Critical) and presented in
severity order. **Nothing is removed unless the user selects it.** The program proposes; the user
disposes. The risk-tier ladder is defined in `SAFETY.md`.

**Alternatives considered.**
- *Allowlist-only cleaner (originally offered)* — safest, but it hides the decision from the user
  and cannot express "this one is worth a look even though it is risky." The user explicitly
  asked for a tiered, choice-based model.
- *Heuristic cleaner with auto-removal* — rejected outright. Auto-removal is where the industry
  has caused the most damage, and it directly contradicts the requirement.
- *Single flat list sorted by size* — rejected because size is a proxy for *value*, not for *risk*.
  A 40 KB orphaned CLSID can be the most important thing on the list, and a 4 MB cache blob can be
  the least risky. Sorting by size would bury the decisions that matter.

**Consequences.** The program needs a richer data model than a boolean "delete this" list, and the
GUI needs a real review surface. It also means the tool's output is only as good as its severity
assignments, which puts real weight on the rule catalog and the confidence model (ADR-003).

---

## ADR-003

**Separate risk tier from confidence score**

**Context.** "Severity" collapses two different questions: *how bad is it if I am wrong?* and *how
sure am I that this is junk?* A registry entry can be 99% certain to be junk and still be
catastrophic to delete, and it can be 40% certain and entirely harmless.

**Decision.** Two independent fields on every finding:
- **Risk tier** (S1–S5) — the consequence of a wrong call. Set by the rule's inherent risk and
  adjusted by context.
- **Confidence** (0–100) — the strength of the supporting evidence.

**Alternatives considered.**
- *One blended severity score* — simpler to display, but it forces a lossy ranking. A
  "critical but certain" item and a "trivial but uncertain" item land in the same bucket, and the
  user loses the ability to make an informed trade-off.
- *Risk only* — hides the difference between "the file is confirmed missing" and "this pattern
  usually indicates an orphan."
- *Confidence only* — hides the blast radius, which is the thing that actually breaks machines.

**Consequences.** More fields to display, but the UI can then do something genuinely useful:
show risk as the grouping axis and confidence as a per-row qualifier, and apply different
confirmation friction to each risk tier independently. The 40–69 confidence band is suppressed by
default and only visible with `--include-speculative`.

---

## ADR-004

**Ship both a CLI and a WPF GUI over one shared core**

**Context.** The registry-cleaning domain is fundamentally a review-and-choose task. Reading a
table of findings in a terminal is workable but unpleasant for the majority of users, who are not
going to learn flag syntax. Automation and support workflows, however, need a scriptable
interface.

**Decision.** Two front-ends over one Core library:
- `RegistryCleaner.Cli` — the primary, fully-featured interface; also the automation surface.
- `RegistryCleaner.Gui` — WPF/MVVM, presenting the same finding model with filtering, a detail
  pane, and tiered confirmation.

**Alternatives considered.**
- *CLI only* — cheaper to build and scriptable. Rejected because the primary audience for this
  tool is not a terminal power user, and the review experience is the product.
- *GUI only* — rejected because batch support, scheduled reporting, and support diagnostics all
  want a scriptable interface, and a GUI-only tool is much harder to test end to end.
- *Electron/Tauri* — rejected; a native Windows tool should use native Windows UI.

**Consequences.** Two UI surfaces to keep in sync with the core model, mitigated by making the GUI
a pure view over the Core's `Finding` / `ScanResult` types with no rule logic of its own. The CLI
is the reference implementation; the GUI cannot drift in *behavior*, only in presentation.

---

## ADR-005

**Rule engine as pluggable `IRegistryRule` implementations**

**Context.** The detection categories (dead uninstall records, orphaned COM registration, broken
shell extensions, stale startup entries, and more) each have distinct logic, data sources, and
severity. They share a shape: scan the registry, emit findings.

**Decision.** Each category is a class implementing `IRegistryRule`:

```csharp
public interface IRegistryRule
{
    string Id { get; }                    // "R-003"
    string Name { get; }                  // "Broken App Paths"
    string Category { get; }              // "Stale Application Registration"
    RiskTier InherentRisk { get; }        // starting tier, adjusted per finding
    IEnumerable<Finding> Scan(ScanContext context);
}
```

`ScanContext` supplies a cached read-only registry reader, the reference index, a file-existence
probe, and a logger. Rules are discovered by assembly scan and are individually enable/disableable
via configuration.

**Alternatives considered.**
- *One monolithic scanner class* — rejected; it would become a several-thousand-line file with
  entangled severity logic.
- *Script-based rules (PowerShell/YAML)* — attractive for extensibility, rejected because a rule
  that can silently fail or misbehave is exactly what we cannot afford, and because rule
  *severity* needs to be a first-class, reviewable part of the code.
- *External rule packs* — rejected for now (see ADR-013).

**Consequences.** Adding a category is a new class plus a fixture test and no changes to the
engine. Each rule must also supply its own confidence reasoning, so the quality of the tool is the
quality of the weakest rule — mitigated by the per-rule test fixture requirement.

---

## ADR-006

**Sort findings by ascending risk, with a descending toggle**

**Context.** ADR-002 requires "listed in order of severity," but does not settle the direction.
Ascending risk leads with the safest, highest-value items; descending risk leads with the most
consequential ones.

**Decision.** Default ordering is **ascending risk** (S1 first), with reclaimed size descending
within each tier. A toggle switches to descending risk for "review the dangerous items first"
workflows.

**Alternatives considered.**
- *Descending risk by default* — arguably the more literal reading of "in order of severity." It
  was rejected because the top of the list would be full of items the user should probably never
  remove, which in practice buries the obvious wins under a wall of caution.
- *Single ordering, no toggle* — rejected; both workflows are legitimate for different people.

**Consequences.** The ordering choice is visible and reversible, and the GUI ships the toggle from
day one. The default still surfaces dangerous items — they are simply below the fold, clearly
labeled, and never pre-selected.

---

## ADR-007

**Dry run by default; mandatory verified backup before any mutation**

**Context.** Any registry cleaner that can permanently damage a machine has to be reversible, and
reversibility has to be proven *before* the first destructive write, not reconstructed afterwards.

**Decision.**
- Scanning never writes. `apply` is a separate, explicit action.
- Before any removal, the tool writes a complete backup: a regedit-compatible `.reg` file, a
  `manifest.json` describing every change, and a SHA-256 of the backup.
- The backup is **read back and verified** before the apply proceeds. If verification fails, the
  entire apply aborts with zero removals performed.
- `restore <backupId>` reverses a completed apply.

**Alternatives considered.**
- *Rely on System Restore* — rejected. System Restore may be disabled, may not cover all hives
  (it does not cover `HKCU` reliably on all configurations), and its retention window may have
  expired. It is a safety net, not a mechanism.
- *Per-item backup only* — rejected; a partial backup cannot restore a partial apply, and
  failure modes compound.
- *Back up without verifying* — rejected; an unreadable or truncated `.reg` is worse than no
  backup, because the user believes they are safe.

**Consequences.** Every apply costs one write pass and a verification read — negligible next to
the registry operations themselves. The tool can honestly claim reversibility.

---

## ADR-008

**Protected-path enforcement lives in code, not in rule discipline**

**Context.** The most dangerous registry paths (`HKLM\SAM`, `HKLM\SYSTEM\CurrentControlSet\Enum`,
`PendingFileRenameOperations`, Winlogon, the MSI installer cache) will be tempting targets for
future rules and future contributors. Relying on every rule author remembering to skip them is
not a safety model.

**Decision.** A `ProtectedPathGuard` sits in the Core between the rule engine and the writer.
It matches a deny-list of paths, hives, and value-name patterns, and it is **not bypassable by
rules** — there is no API for a rule to opt out. Any finding whose location intersects a protected
path is downgraded to `S5` and marked non-selectable, even if a rule assigned it a lower tier.

**Alternatives considered.**
- *Document the protected paths and rely on review* — rejected; this is precisely how safety
  violations happen in real projects.
- *A rule-level attribute marking each rule as safe* — rejected; it relies on the same discipline.
- *Only protect via the denylist in `SAFETY.md`* — rejected; documentation is not enforcement.

**Consequences.** A rule that legitimately wants to touch a protected path cannot do so — by
design, such findings become report-only. The guard is directly unit-tested with adversarial
inputs, including case variations, trailing slashes, and 8.3 short paths.

---

## ADR-009

**Prove "orphan" with a cross-hive reference index**

**Context.** "This COM registration is orphaned" is the most common claim registry cleaners make,
and the most commonly wrong. A `CLSID` key that looks empty may still be referenced by an
extension handler, a TypeLib, a ProgID, an `AppID`, or a per-user override — and deleting it breaks
the referencing software in ways that are hard to diagnose.

**Decision.** Before scanning, the engine builds a **reference index**: a map of every GUID
referenced across the scanned hives (`HKLM\SOFTWARE\Classes`, `HKCR`, `HKCU\Software\Classes`,
and the relevant `HKLM\SOFTWARE` trees), recording the referencing key for each. A finding is only
classified as an orphan when the index proves no referencing key exists. Absence of a reference is
*required evidence*, not a side effect of not looking.

**Alternatives considered.**
- *Check only the immediate key's contents* — rejected; it is the source of the classic false
  positive where a shared COM object is deleted and three unrelated apps break.
- *Check for loaded modules in running processes* — attractive, but it only sees the current
  session and only for loaded DLLs, and it is a significant amount of native interop. Deferred
  as an additional (not primary) signal.
- *Consult a live COM registry call (`CoGetClassObject`)* — too expensive per candidate and
  requires loading arbitrary code into our process. Rejected.

**Consequences.** More memory during a scan (the index is the dominant memory cost — budgeted at
roughly 150–300 MB on a large machine, with a streaming variant planned if needed). In exchange,
orphan findings become defensible, which is what makes the severity tiers trustworthy.

---

## ADR-010

**Debloating and registry compaction are permanently out of scope**

**Context.** Popular cleaners offer "Windows debloat" (removing system components and features) and
"registry compaction" (physically shrinking hive files to reclaim disk space).

**Decision.** Both are out of scope, permanently, and are listed as such in `PLAN.md` §2.

**Rationale.** Debloating's failure modes are severe and frequently unrecoverable without a system
restore point — the machine can fail to boot or lose a driver the user depends on. Compaction's
benefit is negligible on modern NTFS: even a heavily bloated registry is tens of megabytes, while
the operation requires either `defrag.exe` with undocumented flags or filesystem-level work, and
the hive is rebuilt to the same size on the next significant update. Advertising space recovery
from a registry cleaner is largely a marketing artifact.

**Consequences.** The tool will not be competitive with cleaner suites on headline "space
reclaimed" numbers, and that is a deliberate trade. `PLAN.md` §8 commits to honest framing in the
UI and reports.

---

## ADR-011

**Target `net11.0-windows`; Core is a UI-free library**

**Context.** `Microsoft.Win32.Registry` was verified to be available from the plain `net11.0`
shared framework without any NuGet package. However, targeting a Windows-specific TFM is what
silences the CA1416 platform-compatibility analyzer warnings that a plain `net11.0` target
produces for every registry call.

**Decision.** All projects target `net11.0-windows`. `RegistryCleaner.Core` uses no WPF types, so
it stays testable and could be reused by a future service or a `net11.0` build if the registry
surface is ever abstracted.

**Consequences.** The analyzer runs clean with no `NoWarn` suppressions for registry APIs. The
trade is that Core is Windows-only — acceptable for a Windows-only product, and it means no
`Microsoft.Win32.Registry` NuGet dependency and no version drift.

---

## ADR-012

**Treat registry view as part of a key's identity**

**Context.** On 64-bit Windows, `HKLM\SOFTWARE` has two independent views: the native 64-bit view
and the 32-bit view (`WOW6432Node`). A key path alone does not identify a key — the same path
under a different view is a different key with different content.

**Decision.** The registry abstraction exposes `RegistryView` (`Default`, `Registry64`,
`Registry32`) and treats it as part of a key's identity everywhere: in the reader, in the
`RegistryLocation` model, in backups, and in findings. Every rule declares which views it
inspects.

**Alternatives considered.**
- *Scan only the 64-bit view* — rejected; it would miss the large population of 32-bit
  applications that is exactly where stale registrations accumulate.
- *Scan the default view only* — rejected; on a 64-bit process the default is 64-bit, which is
  the same problem.
- *Use `WOW6432Node` paths manually* — rejected; it does not generalize to `HKCU` and is
  error-prone.

**Consequences.** `RegistryLocation` carries a view field, and the UI must display it or the user
cannot tell two identically-named findings apart. Verified on the target machine: both views are
present and readable.

---

## ADR-013

**Fully offline, no telemetry, no remote rule updates**

**Context.** A tool with registry-write privileges is a security-sensitive artifact. Network
capability inside such a tool — for rule updates, "community" rule packs, or telemetry — creates
an attack surface and a trust problem.

**Decision.** The application makes no network calls. There is no telemetry, no crash reporting, no
rule download, and no phone-home. Rules ship with the binary and are auditable by reading the
source. The only network activity would ever be the user's explicit `dotnet restore` at build time.

**Alternatives considered.**
- *Optional telemetry for crash reports* — rejected. Crash reports from a registry-writing tool
  would contain registry paths and machine details; even opt-in collection is a liability here.
- *Downloadable community rule packs* — attractive for coverage, rejected on the same grounds.
  Rule packs would be unvetted executable logic with write access to the registry.

**Consequences.** Rules cannot grow post-release. Coverage improvements ship with new versions.
This is a deliberate feature removal in exchange for a tool whose entire security story is
"it does not talk to the network."

---

## ADR-014

**Separate `plan` and `apply` commands with an immutable plan file**

**Context.** The GUI and CLI need to agree on what exactly gets deleted, and the user needs an
auditable record of what was approved versus what was executed.

**Decision.** A two-phase workflow:
1. `plan` produces `plan.json` — an immutable, timestamped list of exact targets (key path, value
   name, registry view, expected current state).
2. `apply --plan plan.json` executes exactly those targets and nothing else.

`apply` can also accept ad-hoc selectors (`--severity safe`, `--rule R-003`) for scripting, but it
materializes the same plan internally, so both paths share one execution engine and one journal
format.

**Alternatives considered.**
- *Single `clean` command that scans and deletes* — rejected; it makes review impossible and
  makes an accidental run destructive.
- *Store the plan only in memory* — rejected; then there is no artifact to audit or re-run.

**Consequences.** The GUI writes and reads the same `plan.json`, giving a natural "export this
selection to the CLI" workflow and a support-friendly artifact. `apply` re-verifies each target's
current state against the plan before removing it, so a registry change between scan and apply
causes a skip and a report — never a wrong deletion.

---

## ADR-015

**No finding without evidence**

**Context.** Cleaners routinely report "cleanable items" with no explanation, which makes them
impossible to evaluate. A user cannot make a good decision about a finding they do not understand.

**Decision.** Every `Finding` carries a mandatory `Evidence` string containing the concrete fact
that triggered it — the resolved path that does not exist, the GUID with no referencing key, the
value name that failed to parse. A rule that cannot produce evidence for a candidate may not
produce a finding.

**Alternatives considered.**
- *Human-readable reason only* ("orphaned CLSID") — rejected; it states a conclusion without the
  observation that supports it, so the user cannot judge it.
- *Optional evidence* — rejected; optional means absent in exactly the cases where it matters most.

**Consequences.** Rules must do real verification work — resolving paths, expanding environment
variables, checking both registry views, and consulting the reference index. This is the main
driver of scan duration, and it is worth it: evidence is what makes the severity tiers
actionable.

---

## ADR-016

**Default selection is the `Safe` tier only, still requiring confirmation**

**Context.** "Nothing selected" forces a tedious empty-state click-through on every run.
"Everything selected" is the behavior that makes cleaners dangerous.

**Decision.** On load, the GUI pre-selects `S1 Safe` findings only. `S2`–`S4` are unselected.
`S5` is not selectable. Even with a selection, the user must press Clean and confirm.

**Alternatives considered.**
- *Select nothing by default* — safest, but frustrating and it under-uses the tier structure.
- *Select everything up to `S3`* — rejected; `S3` items can break applications that are currently
  working.
- *Remember the previous session's selection* — deferred to a later milestone, and only if it can
  be done without silently re-selecting risky items.

**Consequences.** The common case (run, clear the safe stuff, done) is one click plus one
confirmation. Riskier items always require deliberate action.

---

## ADR-017

**Test every rule against an in-memory fake registry**

**Context.** Rules are the risk surface. Testing them against a live registry is non-deterministic
and would require a dirty machine to produce the interesting cases.

**Decision.** `IRegistryReader` is an interface. The test harness provides an in-memory
implementation that loads fixture trees describing "before" states, including deliberately broken
ones. Each rule ships with a fixture pair — one tree that must produce exactly N findings, and one
tree that must produce zero.

**Alternatives considered.**
- *Integration tests only, against the real registry* — rejected; non-deterministic, and cannot
  assert on specific findings reliably.
- *Snapshot testing of the whole machine* — rejected; too brittle across Windows updates.
- *Record/replay of a real registry* — attractive, rejected initially as complex; the synthetic
  fixture approach is simpler and covers the cases that matter.

**Consequences.** Rules are written against an abstraction, which improves their design. The
fixtures become a readable specification of what counts as junk. The harness is reusable for the
backup/restore round-trip test.

---

## ADR-018

**Audit journal for every action**

**Context.** When a user reports "the tool broke something," the first question is what it did and
why. Without a record, the answer is guesswork.

**Decision.** Every scan, plan, apply, and restore writes a structured journal entry: timestamp,
action, target location, rule id, evidence, risk tier, outcome, and error details where relevant.
Journals are stored alongside backups in `%ProgramData%\RegistryCleaner\Journals\`.

**Alternatives considered.**
- *Console output only* — rejected; it does not survive the session and cannot answer "what did
  the tool do last Tuesday".
- *Windows Event Log* — attractive for central collection; rejected as the primary store because
  it is easy to clear and hard to read. Considered as a supplementary sink in a later milestone.

**Consequences.** Journals grow slowly and are easy to archive. They are the primary diagnostic
artifact for support, and the backup manifest is derived from the same data.

---

## ADR-019

**Publish as a self-contained single-file executable**

**Context.** Measured on the target machine: a WPF app published self-contained as a single file
is **138.4 MB** uncompressed and **64.4 MB** with `EnableCompressionInSingleFile=true`. A
framework-dependent single-file build is roughly 1 MB but requires the .NET 11 Desktop Runtime.

**Decision.** Default distribution is the **self-contained, compressed** single-file executable
(64.4 MB). The framework-dependent build is published as an additional option for managed
environments. WPF is not trimmable, so the size floor is fixed.

**Alternatives considered.**
- *Framework-dependent only* — rejected as the default: it asks non-technical users to install a
  runtime before running a cleanup tool.
- *An installer (MSIX/WiX) instead of a portable exe* — not rejected, but deferred; it does not
  change the size problem and adds a signing dependency. Listed as an open item in `PLAN.md` §10.
- *A console-only build to avoid WPF weight* — rejected; the GUI is a core requirement.

**Consequences.** A 64 MB download, with the tradeoff that no runtime prerequisite exists. The
`.pdb` is excluded from release artifacts. Code signing is an open item (see `PLAN.md` §10),
because unsigned binaries attract AV false positives.

---

## ADR-020

**Do not market disk-space savings**

**Context.** Cleaner suites lead with "space recovered." In practice, registry cleaning frees
kilobytes to a few megabytes at most, and the number is frequently inflated by including
filesystem temp files that are not the registry's business.

**Decision.** The GUI and reports show **entry counts** and **estimated registry bytes** as the
primary metrics. "Space reclaimed" language is not used. The value proposition is stated in terms
of correctness: removing entries that point at things that no longer exist.

**Alternatives considered.**
- *Keep the marketing framing for competitive parity* — rejected; it would be misleading, and a
  user who discovers the claim was inflated has been lied to by the tool they trusted with their
  registry.

**Consequences.** The tool does not compete on a number it cannot honestly produce. The honest
framing is written into `PLAN.md` §8 and into the GUI's summary panel.

---

## ADR-021

**Distribute as a portable EXE, not through the Microsoft Store**

**Context.** The Microsoft Store offers free code signing — Microsoft re-signs MSIX packages after
certification, so no certificate is needed at all. That made it the leading candidate for a
zero-cost signing route. The project is solo-developed, so minimizing cost mattered.

**Decision.** We do **not** publish through the Microsoft Store. We distribute a portable,
self-contained, Authenticode-signed EXE through GitHub Releases and `winget`.

**Rationale.** Microsoft states:

> "Your application needs to work while running as the interactive user. Users who install your
> application may not be system administrators, so requiring your application to run elevated means
> that it won't run correctly for standard users. **If you plan on publishing your app to the
> Microsoft Store, apps that require elevation for any part of their functionality won't be
> accepted into the Store.**"
> — [Prepare to package a desktop application (MSIX)](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)

A registry cleaner writes `HKLM`. There is no non-elevated version of this product — the
requirement is definitional, not a design choice. The `allowElevation` restricted capability
exists but is reserved for Microsoft partners and enterprise organizations, is subject to strict
review, and a Microsoft Q&A response states it is "generally unlikely that an app forcing elevation
on launch will pass the standard certification."

**Alternatives considered.**
- *MSIX with a user-mode UI plus an elevated helper process over a named pipe* — technically
  possible, and Microsoft's own suggested pattern. Rejected because it is a large architectural
  change to a plan that does not need it, it introduces IPC authentication and process-lifecycle
  risk, and certification would still be reviewed case by case under Store Policy 10.2 (Security)
  with no guarantee of approval.
- *Publish both an MSIX (Store) build and a portable EXE* — rejected as unnecessary complexity
  for the project's actual audience.
- *Store as a later experiment* — not ruled out permanently, but not planned.

**Consequences.** We lose the $0 Store signing route and must obtain a certificate ourselves
(~€49/yr via Certum Open Source, or $119.88/yr via Azure Artifact Signing — see `SIGNING.md`).
In exchange we keep the simple distribution model the plan already assumes, we can ship a single
portable EXE that runs on any Windows 11 machine without installation, and we are not subject to
Store certification timelines or policy review. `winget` manifests and GitHub Releases are the
distribution channels.

---

## ADR-022

**Release as open source under the MIT License, in its own repository**

**Context.** The project is developed by a single individual as a hobby project. The distribution
question was "how do users get this without a scary warning," and the answer changed once open
source was chosen. It also raised a second question: where the repository lives and under what
terms.

**Decision.** The project is released as **open source under the MIT License**, in a **dedicated
Git repository** at `C:\git\registry_cleaner` (initialized independently of the surrounding
`C:\git` workspace, which holds unrelated projects).

**Rationale for MIT.** It is OSI-approved, short, unambiguous, and maximally permissive — which
matters concretely here:
- **Certum Open Source Code Signing** (€49/yr) requires an OSI-licensed project, and the
  certificate subject renders as "Open Source Developer, *name*". MIT qualifies.
- **SignPath Foundation** (free) requires an OSI license with no proprietary components. MIT
  qualifies.
- Permissive licensing maximizes the pool of contributors and downstream packagers, which is also
  the "public signs of trust" that SignPath weighs when deciding on an application.

**Alternatives considered.**
- *Apache-2.0* — also permissive and OSI-approved, adds an express patent grant and a NOTICE
  file requirement. Marginally more machinery for a project with no patent surface.
- *AGPL-3.0* — copyleft, prevents someone running a closed hosted version. Considered because it
  protects against the most plausible hostile outcome for a tool like this. Rejected because it
  adds friction for corporate contributors and downstream packagers, and because the project's
  defense is already transparency: a user can read the rules, the evidence, and the denylist, and
  the tool never phones home. A copyleft license protects the code; it does not protect the user.
- *GPL-3.0* — copyleft on distributed binaries with permissive linking. A middle ground, but with
  no clear advantage over MIT here.
- *Keeping the project inside the surrounding `C:\git` repository* — rejected. That workspace holds
  unrelated projects with no commits, and an independent repository gives the project its own
  history, CI, releases, and a clean path to GitHub.

**Consequences.**
- The repository ships `LICENSE`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, and a
  `.gitignore` that excludes signing material and registry backups.
- The SignPath-required acknowledgement — "Free code signing provided by SignPath.io, certificate
  by SignPath Foundation" — is placed in `CONTRIBUTING.md`.
- Open source imposes requirements the plan must now meet: a verifiable build origin, CI, a
  documented release process, and a security contact. These are added to the milestones in
  `PLAN.md`.
- The "no network calls, no telemetry" property (ADR-013) becomes externally auditable rather
  than merely stated — a contributor can verify it.
- `LICENSE` must carry the maintainer's full legal name, matching the name on the signing
  certificate.
