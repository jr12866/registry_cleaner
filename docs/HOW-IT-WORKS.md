# How a Registry Cleaner Works

A walkthrough of the mechanics, written so that every design decision in `PLAN.md`,
`ARCHITECTURE.md`, and `SAFETY.md` is traceable to something real about the Windows registry.

---

## 1. What the registry actually is

The Windows registry is a **hierarchical database of configuration** that the operating system and
every installed application consult at startup, on demand, and during normal operation. It is
loaded into memory at boot from a set of files called *hives* and kept in a coherent state by the
Configuration Manager (`HKLM\SYSTEM\CurrentControlSet\Control\SetControl`).

### The object model

Three kinds of object, and confusing them is the source of most registry bug reports:

| Object | Description | Has a "file" behind it? |
|--------|-------------|--------------------------|
| **Hive** | A top-level tree loaded as a unit. `HKEY_LOCAL_MACHINE`, `HKEY_CURRENT_USER`, `HKEY_CLASSES_ROOT`, `HKEY_USERS`, `HKEY_CURRENT_CONFIG`. | Yes — one file per hive |
| **key** | A folder-like node with a name and a path | No |
| **value** | A typed name→data pair inside a key | No |

A **value** is what people usually mean by "registry entry". Each value has:

- a **name** (may be empty, meaning the *default* value of the key)
- a **type**: `REG_SZ` (text), `REG_EXPAND_SZ` (text with `%VAR%` references), `REG_MULTI_SZ`
  (string list), `REG_DWORD` (32-bit integer), `REG_QWORD` (64-bit integer), `REG_BINARY`
  (arbitrary bytes)
- **data**: the payload

This is the part that matters for a cleaner: **registry values are not files.** They are typed
structured data with no independent existence. A registry full of values is not "using disk
space" in any meaningful sense. When a cleaner advertises recovered gigabytes, it is really
talking about deleting filesystem temp files, not registry data.

### The two critical structural facts

**Fact 1 — the registry is a graph, not a tree.** Keys can reference each other. A `CLSID` key
points at a DLL; a file association points at a `ProgID`; a `ProgID` points at a `CLSID`; an
`AppID` groups a set of `CLSID`s that must be registered and unregistered together. Deleting a
node that looks isolated can break a chain elsewhere. This is the single most important thing to
understand before writing a cleaner, and it is why this project builds a reference index
(ADR-009) rather than pattern-matching keys in isolation.

**Fact 2 — on 64-bit Windows there are two registries.** `HKLM\SOFTWARE` exists twice: the native
64-bit view and the 32-bit view (`WOW6432Node`). `HKCR` is a merged view of
`HKLM\SOFTWARE\Classes` and `HKCU\Software\Classes`. A key path does not identify a key without
also stating the view (ADR-012).

### Where the dangerous stuff lives

| Area | What it controls | Blast radius |
|------|------------------|--------------|
| `HKLM\SYSTEM\CurrentControlSet\Services` | Every service, driver, and service dependency | Boot failures |
| `HKLM\SYSTEM\CurrentControlSet\Enum` | Enumerated devices and drivers | Hardware not working |
| `HKLM\SYSTEM\...\Session Manager\PendingFileRenameOperations` | Files queued for rename/delete at next boot | Corruption of queued operations |
| `HKLM\SAM`, `HKLM\SECURITY` | Local accounts, credentials, LSA secrets | Cannot be recovered |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Cache` | Cached MSI/MSP payloads used for repair and uninstall | Programs that can no longer be repaired or uninstalled |
| `HKLM\...\Winlogon` | Shell, Userinit, notifications | Login loop |
| `HKLM\...\AppInit_DLLs` | DLLs injected into every process | System-wide instability |
| `...\Image File Execution Options` | Per-executable debugging/redirects | Silent behavior changes |
| `HKLM\...\Policies` | Group Policy, including logon scripts | Domain policy bypass or breakage |
| `HKCU\...\Explorer\Shell Folders` | Desktop, Documents, AppData locations | User's files appear to vanish |

None of these are "junk" and none will appear in a cleaner's default scope. They appear in this
document because the cleanup tool's job is to know where the edge of the cliff is. `SAFETY.md`
turns this into a hard-coded deny-list.

---

## 2. Why stale entries accumulate in the first place

Junk is not a mystery; it is a predictable consequence of how software is installed:

1. **Uninstallers are incomplete.** Many applications register COM objects, file associations, shell
   extensions, and background services during install but do not deregister all of them on removal.
   Microsoft's own guidance to vendors is explicit about removing registrations.
2. **Installs that fail partway.** An installer that aborts after writing registry entries but
   before completing leaves registrations behind with no corresponding files.
3. **Programs that are moved or upgraded in place.** A new version installs to a new path. If the
   old path's registrations were not removed, they point at a directory that no longer exists.
4. **Portable and no-install software.** It writes settings under `HKCU\Software\...` and never
   removes them. Uninstalling by deleting the folder leaves the settings behind forever.
5. **Hardware changes.** A USB docking station, printer, or GPU is removed. The driver
   registration sometimes survives.
6. **Crashes and power loss during an MSI transaction.** The Windows Installer keeps state under
   `HKLM\SOFTWARE\Classes\Installer\Products` and `...\Installer\UserData`, and interrupted
   transactions can leave rows with no corresponding package.
7. **Abandoned trials and updaters.** Update frameworks commonly leave registration behind.

The general pattern: **the registry is the record of intent, and the filesystem is the evidence of
reality.** When they disagree, the registry is usually stale — but not always.

---

## 3. The three detection primitives

Every rule in a cleaner reduces to one of three checks. This is the whole intellectual content of
the program.

### Primitive A — Dangling path

> A registry value contains a filesystem path. The path does not exist. Therefore the registration
> is stale.

Example:
```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7A3B...}\InstallLocation
    = "C:\Program Files\Contoso Widget Suite\"
    → directory does not exist on disk
```

**Why it works:** the installer wrote a path that was true at install time; the program is gone and
the path with it.

**How to get it right — this is where naive cleaners break:**
- Expand `%SystemRoot%`, `%ProgramFiles%`, `%LOCALAPPDATA%` and every other environment variable
  in `REG_EXPAND_SZ` values before testing existence.
- Resolve the path relative to the registry view. A 32-bit app's path may be under
  `C:\Program Files (x86)`, and the same logical path can exist in both.
- `File.Exists` on a **network drive or UNC path** returns false when the share is disconnected.
  This is the single largest source of false positives in real cleaners. The correct behavior is
  to classify the finding as *unverifiable* and lower its confidence, not to assert the file is
  missing.
- A path can be missing because the app lives on removable media that is not currently inserted.
- The program may still be installed but "moved" by the user without updating its registration.
- `UninstallString` may contain arguments (`"C:\...\unins000.exe" /SILENT`) and quotes; strip them
  before testing.

### Primitive B — Dangling reference

> A registry key declares an identifier (a GUID, a ProgID, an AppID). Nothing else in the registry
> points to it. Therefore it is orphaned.

Example:
```
HKLM\SOFTWARE\Classes\CLSID\{0F1E2D3C-...}
    → no InprocServer32, no LocalServer32, no TreatAs
    → and the reference index shows zero keys referencing {0F1E2D3C-...}
```

**Why it works:** COM registration is a two-sided contract. The `CLSID` side says "here is the
code that implements this class"; the consumer side says "when I want this class, use this
`CLSID`". With no consumer side, the implementation side is dead weight.

**How to get it right:**
- Build the reference index **across all scanned hives**, not just the one being cleaned. A
  `CLSID` referenced only from `HKCU\Software\Classes` is still referenced.
- Check the "consumer" side exhaustively: file extensions (`.docx` → `Word.Document.12`),
  `ProgID` → `CLSID`, `VersionIndependentProgID` → `CLSID`, `TypeLib` → `CLSID`, `AppID` →
  `CLSID`, `TreatAs` → `CLSID`, shell extension handlers, and `HKCR` merged-view references.
- Absence of `InprocServer32` is **not** proof of orphanhood. Some COM classes are implemented
  entirely in managed code and registered with `InprocServer32` pointing to a CLR surrogate; some
  are `LocalServer32`; some are registered only under a `ProgID`. Never classify on "the key looks
  empty" alone.
- **Shared COM objects are the killer case.** One `CLSID` can back a dozen applications. Delete it
  and you break all of them. This is why the reference index exists and why the `S3` tier demands
  acknowledgment rather than a single click.

### Primitive C — Structural residue

> A key exists in a position that only a specific kind of install writes, and the surrounding
> evidence of that install is gone.

Example:
```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\<packed-guid>
    → the MSI product record references a LocalPackage that no longer exists
```

**Why it works:** these tables have rigid schemas. The Windows Installer writes a row, and the row
points at a `.msi` file. If the file is gone, the row is orphaned.

**How to get it right:** these tables use **packed GUIDs** (a byte-swapped encoding of the
product code) and are **internally cross-referenced**. A `Components` entry may be referenced by
many `Features`, which are referenced by `Products`. Deleting one row in isolation can corrupt the
installer database for an application that is still installed and functional. Structural residue
rules therefore require whole-graph analysis and land in `S3`/`S4`, never in `S1`.

---

## 4. From primitive to finding: the scoring step

A raw observation ("this path is missing") is not yet a finding. It needs four more things:

1. **Verification** — confirm the path is genuinely absent, including the network-share and
   removable-media cases above. Unverifiable → lower confidence, never assert absence.
2. **Reference check** — confirm nothing else depends on the target.
3. **Context** — determine the risk tier from *what* the key controls, not just from the rule.
   A missing path under `HKLM\...\Uninstall` is low-risk; the same missing path under
   `HKLM\SYSTEM\...\Services` is high-risk. Some rules therefore re-tier per finding based on the
   location.
4. **Explanation** — a sentence the user can act on, plus the raw evidence string.

That produces the `Finding` record:

```csharp
record Finding(
    string Id,               // stable id: "F-20261120-0031"
    string RuleId,           // "R-001"
    RegistryLocation Location,
    FindingKind Kind,        // Key | Value
    string Summary,          // "Contoso Widget Suite is still registered in Apps & Features"
    string Explanation,      // plain English: what it is, why it is stale, what removing it does
    string Evidence,         // "InstallLocation 'C:\Program Files\Contoso Widget Suite\' does not exist"
    RiskTier Risk,           // S1..S5
    int Confidence,          // 0..100
    string? SoftwareName,    // best-effort friendly name
    long EstimatedBytes,     // registry bytes reclaimed
    bool Selectable          // false for S5
);
```

The separation of `Summary` / `Explanation` / `Evidence` is deliberate. `Summary` is the label.
`Explanation` is for the user. `Evidence` is the raw fact, so a skeptical user or a support
engineer can verify the claim independently (ADR-015).

---

## 5. The apply stage

Detection is the easy half. Removal is where machines break.

### Writing to the registry

There is **no registry transaction**. A cleanup of 200 items is 200 independent mutations. If one
fails — because a key is in use, permissions differ, or the hive is locked — the others have already
happened. Consequences:

- The apply engine must be **journaled at the item level** (ADR-018) so a partial apply is
  reconstructible.
- The engine must **fail closed** where it can: if the backup cannot be written and verified, the
  apply aborts *before* the first deletion, not midway.
- Item-level failures are reported, not silently swallowed.

### The backup format

A backup must be (a) complete, (b) readable by a human tool, and (c) machine-restorable. We write
two artifacts:

- **`backup.reg`** — the standard Windows Registry Editor export format. Text, diffable, and
  importable by double-click if our own restore command is unavailable. This is the "get out of
  jail" file.
- **`manifest.json`** — the machine-readable record: every target, its original type and data
  (base64 for binary), the rule that flagged it, the evidence, the risk tier, and a SHA-256 of
  `backup.reg`.

Restoring from the manifest (rather than only from the `.reg`) preserves exact value types and
binary payloads, which round-trips through the `.reg` format with fidelity but is far easier to
implement correctly in code.

### The TOCTOU problem

Between the scan and the apply, the registry may change — the user installs something, a Windows
Update runs, an app self-repairs. If the tool deletes "the key I found 40 seconds ago", it may be
deleting something that is no longer stale.

Mitigation: the plan file records the *expected state* of each target. At apply time the engine
re-reads the target and compares it. If it differs, the item is **skipped and reported**, not
deleted. This turns a dangerous race into a benign no-op.

### The permission problem

Registry writes under `HKLM` need elevation. The tool therefore:
- refuses to apply without elevation, with a clear message;
- the GUI declares `requireAdministrator` in its application manifest so UAC prompts at launch;
- the CLI detects non-elevation and offers to relaunch itself elevated via
  `ShellExecute` with the `runas` verb.

### The "is this really broken?" problem

The hard case: the file is missing, but the program is **not** gone. The user moved the install
directory, or the program lives on a network share that is disconnected right now, or the program
reinstalls itself. The cleaner is technically correct and practically wrong.

This is why confidence exists as a separate axis from risk, why the missing-network-path case
yields "unverifiable" rather than "missing", and why the highest-risk categories require explicit
typed confirmation rather than a click.

---

## 6. What separates a good cleaner from a bad one

| Property | Bad cleaner | This project |
|----------|-------------|--------------|
| Detection | Pattern-matches key names | Verifies the referenced artifact is actually absent |
| False positives | Silently deletes | Unverifiable → lower confidence; network paths excluded |
| Shared COM objects | Deleted if the key "looks empty" | Reference index proves no consumer exists |
| 32-bit apps | Blind to WOW6432Node | Both views scanned, view part of key identity |
| Reversibility | "Create a restore point first" | Per-item verified backup plus a working restore command |
| Explaining | "Found 142 items to clean" | Evidence string per finding, plain-English explanation |
| User control | One button | Severity tiers, selection, escalating confirmation |
| Network | "cloud scan" | No network capability at all (ADR-013) |

The pattern: **every unsafe shortcut in the table on the left is an instance of not doing enough
verification work.** A better cleaner is simply one that does more verification and is honest when
verification is impossible.

---

## 7. How this maps onto the rule catalog

`RULES.md` lists every planned rule with its primitive, its risk tier, and the specific checks it
performs. The primitives map as follows:

| Rule family | Primitive | Typical tier |
|-------------|-----------|--------------|
| Stale uninstall records | A (dangling path) | S2–S3 |
| Broken App Paths | A | S2 |
| Orphaned COM registrations | B (dangling reference) | S3 |
| Orphaned ProgIDs / TypeLibs | B | S3 |
| Broken shell extensions / BHOs | A + B | S2–S3 |
| Stale startup entries | A | S2 |
| Stale fonts, printers, DSNs | A | S3–S4 |
| Orphaned Installer product records | C (structural residue) | S3–S4 |
| Shell navigation residue | A | S1 |
| Large values, empty keys, pending operations | Structural / report-only | S5 |

---

## 8. The one-paragraph version

A registry cleaner reads the registry, finds values that point at files that no longer exist or
references that nothing uses anymore, ranks each finding by how much damage a wrong call would
cause, shows the user the evidence, writes a complete backup, removes only what the user selected,
and can put everything back. Everything else — the tier ordering, the reference index, the
protected-path guard, the escalating confirmations — exists to make "the user selects" mean
something safe.
