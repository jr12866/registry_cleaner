# Architecture

## 1. Solution layout

```
C:\git\registry_cleaner\
├── RegistryCleaner.sln
├── Directory.Build.props          # shared TFM, nullable, analyzers, version stamping
├── Directory.Packages.props        # central package management
├── README.md
├── docs\
│   ├── PLAN.md
│   ├── DECISIONS.md
│   ├── HOW-IT-WORKS.md
│   ├── ARCHITECTURE.md
│   ├── SAFETY.md
│   ├── RULES.md
│   └── UI.md
├── src\
│   ├── RegistryCleaner.Core\        # net11.0-windows, library — no UI, no direct registry writes
│   │   ├── Registry\
│   │   │   ├── IRegistryReader.cs
│   │   │   ├── WindowsRegistryReader.cs
│   │   │   ├── RegistryLocation.cs
│   │   │   └── RegistryViewScope.cs
│   │   ├── Analysis\
│   │   │   ├── ReferenceIndex.cs
│   │   │   ├── ReferenceIndexBuilder.cs
│   │   │   ├── ConfidenceScorer.cs
│   │   │   └── RiskTierer.cs
│   │   ├── Findings\
│   │   │   ├── Finding.cs
│   │   │   ├── RiskTier.cs
│   │   │   ├── FindingKind.cs
│   │   │   └── FindingCategory.cs
│   │   ├── Rules\
│   │   │   ├── IRegistryRule.cs
│   │   │   ├── ScanContext.cs
│   │   │   ├── RuleCatalog.cs
│   │   │   └── Catalog\
│   │   │       ├── UninstallRules.cs          # R-001 .. R-003
│   │   │       ├── ComRules.cs                # R-004 .. R-007
│   │   │       ├── ShellIntegrationRules.cs   # R-008 .. R-011
│   │   │       ├── StartupRules.cs            # R-012
│   │   │       ├── SystemResidueRules.cs      # R-013 .. R-015
│   │   │       └── ClutterRules.cs            # R-016 .. R-020
│   │   ├── Safety\
│   │   │   ├── ProtectedPathGuard.cs
│   │   │   ├── ProtectedPaths.cs
│   │   │   ├── ElevationGuard.cs
│   │   │   ├── SystemStateGuard.cs
│   │   │   └── ConfirmationPolicy.cs
│   │   ├── Remediation\
│   │   │   ├── IRegistryWriter.cs
│   │   │   ├── RegistryWriter.cs
│   │   │   ├── BackupWriter.cs
│   │   │   ├── BackupManifest.cs
│   │   │   ├── RegFileExporter.cs
│   │   │   ├── RestoreService.cs
│   │   │   └── ApplyEngine.cs
│   │   ├── Reporting\
│   │   │   ├── JsonReportWriter.cs
│   │   │   └── HtmlReportWriter.cs
│   │   ├── Journaling\
│   │   │   ├── AuditJournal.cs
│   │   │   └── JournalEntry.cs
│   │   └── Configuration\
│   │       └── CleanerOptions.cs
│   ├── RegistryCleaner.Cli\         # net11.0-windows, exe — System.CommandLine
│   │   ├── Program.cs
│   │   ├── Commands\{Scan,Plan,Apply,Restore,Verify,Rules,Export}.cs
│   │   └── Formatting\{ConsoleTable,ColorOutput}.cs
│   └── RegistryCleaner.Gui\         # net11.0-windows, WinExe, UseWPF
│       ├── App.xaml / App.xaml.cs
│       ├── app.manifest               # requireAdministrator
│       ├── ViewModels\
│       │   ├── MainViewModel.cs
│       │   ├── FindingsViewModel.cs
│       │   ├── FindingDetailViewModel.cs
│       │   └── RelayCommand.cs
│       ├── Views\
│       │   ├── MainWindow.xaml
│       │   ├── FindingDetailPane.xaml
│       │   ├── ConfirmationDialog.xaml
│       │   └── BackupHistoryWindow.xaml
│       └── Controls\
│           ├── SeverityBadge.xaml
│           └── ConfidenceBar.xaml
└── tests\
    ├── RegistryCleaner.TestHarness\  # in-memory registry + fixture loader
    │   ├── FakeRegistryReader.cs
    │   ├── FakeFileSystemProbe.cs
    │   └── Fixtures\
    └── RegistryCleaner.Core.Tests\    # xUnit
```

`Core` has **no reference to WPF or the CLI**, and contains **all** rule logic, scoring, safety
enforcement, and remediation. Both front-ends are thin.

## 2. Core types

### RegistryLocation

```csharp
public enum RegistryHive { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER, HKEY_CLASSES_ROOT,
                            HKEY_USERS, HKEY_CURRENT_CONFIG }

public enum RegistryViewScope { Default, Registry64, Registry32 }

public readonly record struct RegistryLocation(
    RegistryHive Hive,
    string KeyPath,
    string? ValueName,          // null = the key itself
    RegistryViewScope View,
    FindingKind Kind)
{
    public string DisplayPath => $"{Hive}\\{KeyPath}" +
        (ValueName is null ? "" : $" :: {ValueName}") +
        (View == RegistryViewScope.Default ? "" : $" [{View}]");
}
```

`ValueName` being `null` means the target is the *key*; a non-null name means the target is that
*value* inside the key. This distinction matters for deletion: deleting a value must not delete
the key, and vice versa.

### Finding and RiskTier

```csharp
public enum RiskTier { Safe = 1, Low = 2, Moderate = 3, High = 4, Critical = 5 }

public enum FindingKind { Key, Value }

public sealed record Finding(
    string Id, string RuleId, RegistryLocation Location, FindingKind Kind,
    string Summary, string Explanation, string Evidence,
    RiskTier Risk, int Confidence, string? SoftwareName,
    long EstimatedBytes, bool Selectable)
{
    public string SoftwareNameOrLocation => SoftwareName ?? Location.DisplayPath;
}
```

`Finding` is immutable. Nothing downstream mutates a finding; corrections are new findings.

### IRegistryReader

```csharp
public interface IRegistryReader
{
    bool KeyExists(RegistryLocation location);
    IReadOnlyList<RegistryLocation> GetSubKeyNames(RegistryLocation key);
    IReadOnlyList<RegistryValue> GetValueNames(RegistryLocation key);
    RegistryValue? GetValue(RegistryLocation location);
    IReadOnlyList<RegistryLocation> EnumerateSubKeys(RegistryLocation root);
}

public readonly record struct RegistryValue(string Name, RegistryValueKind Kind, object Data,
                                            long ByteSize);
```

`RegistryValueKind` maps 1:1 onto the native `REG_*` types, including `REG_EXPAND_SZ` and
`REG_MULTI_SZ` so the backup writer can restore them exactly.

### IRegistryRule

```csharp
public interface IRegistryRule
{
    string Id { get; }                 // "R-001"
    string Name { get; }               // "Stale uninstall record (64-bit)"
    string Description { get; }        // shown in the rule reference and in the finding detail
    FindingCategory Category { get; }
    RiskTier InherentRisk { get; }     // baseline; may be adjusted per finding
    IEnumerable<Finding> Scan(ScanContext context);
}
```

`ScanContext` provides:

```csharp
public sealed class ScanContext
{
    public IRegistryReader Reader { get; }
    public ReferenceIndex References { get; }
    public IFileSystemProbe FileSystem { get; }
    public IClock Clock { get; }
    public ILogger Logger { get; }
    public CancellationToken Cancellation { get; }
}
```

`IFileSystemProbe` is an interface precisely so rules are unit-testable without touching the disk:

```csharp
public interface IFileSystemProbe
{
    FileProbeResult Probe(string path);   // Exists | Missing | Unverifiable
    bool TryResolveEnvironment(string rawPath, out string expanded);
}

public enum FileProbeResult { Exists, Missing, Unverifiable }
```

`Unverifiable` (disconnected network share, access denied, malformed path) is a first-class
outcome. A rule that receives `Unverifiable` must lower confidence and must not assert the file is
missing — this is the mechanism that prevents the classic network-drive false positive.

## 3. Scan pipeline

```
 ┌──────────────────────────────────────────────────────────────────┐
 │ 1. RegistrySnapshot                                             │
 │    WindowsRegistryReader with a per-key-path cache              │
 │    Reads: HKLM, HKCU, HKCR (merged), both 64-bit and 32-bit    │
 └───────────────────────────────┬──────────────────────────────────┘
                                 ▼
 ┌──────────────────────────────────────────────────────────────────┐
 │ 2. ReferenceIndex                                               │
 │    Walks HKCR + HKLM\SOFTWARE\Classes + HKCU\Software\Classes  │
 │    Records every GUID reference and its referencing location     │
 │    (extension → ProgID → CLSID → server, TypeLib, AppID, …)      │
 └───────────────────────────────┬──────────────────────────────────┘
                                 ▼
 ┌──────────────────────────────────────────────────────────────────┐
 │ 3. Rule execution (parallel, read-only, shared cache)           │
 │    Each rule yields raw findings with evidence                   │
 └───────────────────────────────┬──────────────────────────────────┘
                                 ▼
 ┌──────────────────────────────────────────────────────────────────┐
 │ 4. Scoring                                                      │
 │    RiskTierer:   context-aware tier adjustment (path-based)     │
 │    ConfidenceScorer: evidence quality → 0..100                   │
 │    ProtectedPathGuard: downgrades anything intersecting deny-list │
 └───────────────────────────────┬──────────────────────────────────┘
                                 ▼
 ┌──────────────────────────────────────────────────────────────────┐
 │ 5. ScanResult { Findings, ScanStartedAt, RuleVersions, Stats }  │
 └──────────────────────────────────────────────────────────────────┘
```

### Parallelism

Rules run in parallel over a shared, thread-safe, read-only cache. The cache is keyed by
`(hive, keyPath, view)`. Because rules only read, there is no locking on the registry itself, and
the scan is CPU-bound on enumeration and file probing rather than on registry I/O.

A scan is **point-in-time per hive**, not globally atomic. The scan timestamp is recorded, and
`apply` re-verifies each target (ADR-014), so a mid-scan change is caught rather than acted upon.

### Performance budget

| Phase | Target | Notes |
|-------|--------|-------|
| Snapshot | ≤ 8 s | Cached enumeration; skips values under protected paths where rules never look |
| Reference index | ≤ 12 s | Dominant cost — full walk of the Classes trees |
| Rule execution | ≤ 20 s | Parallel; dominated by file-existence probes, which are cached by path |
| Scoring | < 1 s | In-memory |
| **Total** | **≤ 40 s** | Re-run incrementally in the GUI via rule-level caching |

If the full index proves too slow on large machines, the fallback is a **lazy reference index**:
build the index only for GUIDs that candidate rules actually produce, on demand, then cache. This
trades a second pass for a much smaller first pass.

## 4. Apply pipeline

```
 plan.json (or ad-hoc selectors)
        │
        ▼
 ┌────────────────────────────────────────────┐
 │ ElevationGuard — refuse if not elevated   │
 │ SystemStateGuard — safe mode? pending     │
 │   reboot? update in progress? → refuse    │
 └────────────────────┬───────────────────────┘
                      ▼
 ┌────────────────────────────────────────────┐
 │ Pre-flight: re-read each target, compare  │
 │   to the expected state in the plan       │
 │   → mismatch ⇒ skip + report, never delete│
 └────────────────────┬───────────────────────┘
                      ▼
 ┌────────────────────────────────────────────┐
 │ BackupWriter                               │
 │   backup.reg  (regedit-compatible)        │
 │   manifest.json (targets, types, evidence) │
 │   sha256 recorded                          │
 └────────────────────┬───────────────────────┘
                      ▼
 ┌────────────────────────────────────────────┐
 │ VERIFY the backup — read it back, compare │
 │   target count and byte hashes             │
 │   failure ⇒ ABORT, zero removals          │
 └────────────────────┬───────────────────────┘
                      ▼
 ┌────────────────────────────────────────────┐
 │ ApplyEngine — one target at a time        │
 │   ProtectedPathGuard check (again)        │
 │   journal entry written BEFORE and AFTER  │
 │   item failure ⇒ record, continue         │
 └────────────────────┬───────────────────────┘
                      ▼
 ┌────────────────────────────────────────────┐
 │ Summary: removed / skipped / failed       │
 │ Undo hint: restore command with backup id │
 └────────────────────────────────────────────┘
```

`ProtectedPathGuard` is invoked a second time at apply time, even though findings were already
filtered at scan time. This is belt-and-braces: the guard is the last thing between a rule bug and
a broken machine.

## 5. CLI surface

Full reference in `UI.md`. Summary of commands and their safety defaults:

| Command | Writes registry? | Default behavior |
|---------|------------------|------------------|
| `scan` | No | Read-only; prints findings |
| `plan` | No | Writes `plan.json` only |
| `apply --plan <file>` | **Yes** | Requires elevation + confirmation |
| `apply --severity <tier>` | **Yes** | Requires elevation + confirmation |
| `restore <backupId>` | **Yes** | Requires elevation + confirmation |
| `verify` | No | Integrity check of backups and journals |
| `rules` | No | Lists the rule catalog |
| `export` | No | Writes an HTML/JSON report |

Exit codes:

| Code | Meaning |
|------|---------|
| 0 | Success, nothing to do |
| 1 | Scan completed, findings exist |
| 2 | Error |
| 3 | Elevation required |
| 4 | Nothing selected / empty plan |
| 5 | System state prevents apply (safe mode, pending reboot, update in progress) |
| 6 | Apply partially completed — see journal and restore instructions |

## 6. GUI architecture

MVVM with `CommunityToolkit.Mvvm` (source generators, no runtime reflection cost).

```
MainWindow
 ├── ViewModel: MainViewModel
 │     ├── ObservableCollection<FindingNode> FilteredFindings
 │     ├── ScanCommand / RescanCommand / CleanCommand / ExportCommand / RestoreCommand
 │     └── SelectionSummary
 ├── Left pane:  Severity → Category → Rule tree with counts
 ├── Center:      DataGrid of findings (sortable, filterable, searchable)
 ├── Right pane:  FindingDetailViewModel (explanation, evidence, impact, restore note)
 └── Bottom bar:  counts per tier, selected count, buttons
```

The GUI **never** runs a rule. It consumes `ScanResult` from the Core and calls `ApplyEngine` with
a plan it constructs from the user's selection. Both front-ends therefore have identical cleanup
behavior — the GUI is a different view, not a different implementation.

## 7. Testing strategy

| Layer | Tool | What it covers |
|-------|------|----------------|
| Rule unit tests | xUnit + `FakeRegistryReader` | Each rule: a "must find" fixture and a "must not find" fixture |
| Path resolution | xUnit + `FakeFileSystemProbe` | Env-var expansion, quotes, arguments, network paths → `Unverifiable` |
| Reference index | xUnit | GUID chains; shared-COM false-positive regression fixture |
| Scoring | xUnit | Tier boundaries, confidence clamping, protected-path downgrade |
| Guard | xUnit | Adversarial bypass attempts: case variants, trailing `\` and `/`, 8.3 paths, `..` segments |
| Backup/restore | xUnit round-trip | Synthetic tree → backup → mutate → restore → deep-equal |
| Apply engine | xUnit | TOCTOU skip behavior, partial-failure journaling, fail-closed on backup error |
| CLI | xUnit + `System.CommandLine` test harness | Exit codes, argument parsing, output formats |
| Integration | Manual + scripted | Live read-only scan on the target machine; snapshot-compare across runs |

The **round-trip test** is the single most important test in the project: it proves that
scan → backup → delete → restore returns the registry to a byte-identical state.

## 8. Repository notes

The Git repository root is `C:\git` (the parent of this directory), and it currently has no
commits. `C:\git\registry_cleaner` is the project directory. Two options for the first milestone:

1. Keep the project as a subdirectory of the existing `C:\git` repo (which currently holds several
   unrelated projects, including some not yet committed).
2. Initialize `C:\git\registry_cleaner` as its own repository.

Option 2 is cleaner for an independent project and is the recommendation in `PLAN.md` §10 — this
awaits the user's decision before any git operation is performed.
