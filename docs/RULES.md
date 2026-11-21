# Rule Catalog

Every detection rule the cleaner will implement, with its detection primitive, its risk tier, and
how its confidence is earned. Rule IDs are stable and appear in findings, plans, journals, and the
CLI's `--rule` filter.

**Detection primitives** (see `HOW-IT-WORKS.md` §3):
- **A — Dangling path**: a value holds a file path; the path does not exist.
- **B — Dangling reference**: a key declares an identifier; nothing references it.
- **C — Structural residue**: a key occupies a schema-defined position whose backing artifact is gone.

**Confidence baselines** are the score a rule produces when all its checks pass. Rules reduce the
score per the conditions in `SAFETY.md` §3.

---

## Summary

| ID | Rule | Primitive | Tier | Confidence baseline | Phase |
|----|------|-----------|------|---------------------|-------|
| R-001 | Stale uninstall record — 64-bit view | A | S2 | 95 | M1 |
| R-002 | Stale uninstall record — 32-bit view | A | S2 | 95 | M1 |
| R-003 | Stale uninstall record — per-user | A | S2 | 95 | M1 |
| R-004 | Broken App Paths entry | A | S2 | 95 | M1 |
| R-005 | Orphaned CLSID registration | B | S3 | 85 | M1 |
| R-006 | Orphaned ProgID | B | S3 | 80 | M1 |
| R-007 | Orphaned TypeLib | A | S3 | 85 | M2 |
| R-008 | Orphaned AppID grouping | B | S4 | 75 | M2 |
| R-009 | Broken Browser Helper Object | A + B | S2 | 90 | M2 |
| R-010 | Broken shell extension handler | A + B | S3 | 85 | M2 |
| R-011 | Broken context menu handler | A + B | S3 | 85 | M2 |
| R-012 | Stale startup entry | A | S2 | 95 | M2 |
| R-013 | Stale StartupApproved entry | A | S2 | 90 | M2 |
| R-014 | Orphaned Installer product record | C | S3 | 85 | M2 |
| R-015 | Orphaned Installer component record | C | S4 | 70 | M2 |
| R-016 | Stale font registration | A | S3 | 80 | M2 |
| R-017 | Stale printer registration | A | S4 | 85 | M2 |
| R-018 | Stale ODBC DSN | A | S3 | 85 | M2 |
| R-019 | Shell navigation residue | A | S1 | 95 | M2 |
| R-020 | Stale policy startup entry | A | S4 | 75 | M2 |
| R-021 | Large registry value | — | S5 | — | M2 |
| R-022 | Empty key (clutter) | — | S1 | 90 | M2 |
| R-023 | Pending file operations | — | S5 | — | M2 |

M1 ships R-001…R-005, which cover the highest-value and lowest-risk categories: phantom entries in
Apps & Features, broken app paths, and the first COM orphan check.

---

## R-001 / R-002 / R-003 — Stale uninstall record

**Location**
- R-001: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*` (64-bit view)
- R-002: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*` (32-bit view)
- R-003: `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*` (both views)

**What it finds:** an application that still appears in Settings → Apps, but whose files are gone.
This is the most commonly complained-about category of registry junk.

**Detection.** Read `InstallLocation` (preferred) and `UninstallString`. Extract the executable path
from `UninstallString` (strip quotes and arguments). Probe **both** paths:
- `InstallLocation` missing **and** `UninstallString` target missing → confident stale.
- Only one is missing → lower confidence (60–70): the program may have moved its uninstaller.

Additional guards:
- If `WindowsInstaller` = 1, cross-check the product code against
  `HKLM\SOFTWARE\Classes\Installer\Products` — if the product record exists, the app is registered
  with the installer and may only need repair, not removal.
- If `SystemComponent` = 1, **skip entirely** — this flag marks a component as part of the OS or
  another product.
- If `ParentKeyName` is present, this is a sub-feature of another product — skip unless the parent
  is also stale.
- If `DisplayName` is missing, the key is malformed rather than stale — report at `S2` but never
  delete the whole key without a `DisplayName` present (the key may be referenced by an installer).

**Evidence:** `InstallLocation 'C:\Program Files\Contoso Widget Suite\' does not exist; uninstall
string target 'C:\Program Files\Contoso Widget Suite\unins000.exe' does not exist.`

**Why S2:** the record is metadata about a program that is not on disk. Removing it does not
affect any running process, and Windows itself reconciles this data during repair operations.
**Confidence 95** because both the install directory and the uninstaller are confirmed absent.

**Note.** This is the single rule most likely to produce a finding on a healthy machine, because
Windows itself does not always clean up partially-uninstalled software. It is also the rule users
most want.

---

## R-004 — Broken App Paths entry

**Location:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\*` (both views),
`HKCU\...\App Paths\*`

**What it finds:** an entry that lets Windows find an application by name (used by `Start → Run`,
the file-open dialog, and `ShellExecute`).

**Detection.** Read the default value (the executable path), plus `Path` (a directory to search).
If the default value's file is absent:
- and `Path` is present and the file exists under `Path` → the entry is broken (default points at
  the wrong place) → report at `S2`, remove the **value**, keep the key.
- and the file is absent everywhere → the program is gone → report at `S2`, remove the whole key.

**Evidence:** `App Paths entry for 'contoso.exe' points to 'C:\Program Files\Contoso\contoso.exe',
which does not exist.`

---

## R-005 — Orphaned CLSID registration

**Location:** `HKCR\CLSID\*` (merged view), `HKLM\SOFTWARE\Classes\CLSID\*`,
`HKCU\Software\Classes\CLSID\*`

**What it finds:** a COM class registration whose implementation is gone and which nothing uses.

**Detection — in order:**
1. Resolve the server: `InprocServer32` (default value → DLL path), else `LocalServer32`
   (default value → EXE path), else `TreatAs` (follow the chain one level), else
   `ProgID`/`VersionIndependentProgId` (follow to the referenced `CLSID` and resolve there).
2. If the server path is under `%SystemRoot%` → **skip** (in-box COM component; see
   `SAFETY.md` §4.3).
3. Probe the server path. If it is **missing** → candidate.
4. Consult the **reference index**. If any key anywhere in the scanned hives references this
   `{GUID}` — as an extension handler, a `ProgID` target, a `TypeLib`, an `AppID` member, a
   `TreatAs`, a `ProgID` under `HKCR` — it is **not** an orphan.
5. If the reference index shows no consumer, the finding stands.

**Evidence:** `CLSID {0F1E…} has InprocServer32 'C:\Program Files\Contoso\comctl.dll' which does
not exist, and no key in HKCR, HKLM\SOFTWARE\Classes or HKCU\Software\Classes references this
GUID.`

**Why S3:** a shared COM object can back many applications. The reference index is what makes this
rule safe, and the tier reflects that a mistake here breaks applications in ways that are hard to
trace back. The "no server path at all" case (an empty-looking `CLSID` key) is **not** reported
unless the reference index confirms it is unreferenced — some managed-only COM classes have no
`InprocServer32` of their own.

---

## R-006 — Orphaned ProgID

**Location:** `HKCR\*` (non-`CLSID`, non-`TypeLib`, non`Interface` top-level keys) and
`HKCU\Software\Classes\*`

**Detection.** A `ProgID` is orphaned when:
- it is not the target of any extension association (`HKCR\.<ext>\OpenWithProgids`),
- it is not the `Default` value of any extension,
- it is not referenced by any `CLSID` key's `ProgID` or `VersionIndependentProgID`,
- it has no `PersistentAddHandlerRegistered` pointing at a live `CLSID`,
- and it has no `SupportedSoftware`/`Application` partner entry.

**Why S3:** `ProgID` chains are the classic fan-out point. A `ProgID` referenced by three
extensions breaks all three when removed.

---

## R-007 — Orphaned TypeLib

**Location:** `HKCR\TypeLib\{GUID}\1.0\0\win32` (and `win64`, `wow64` variants)

**Detection.** Read `win32`/`win64` → `0\win32.dll` → path. If the DLL is absent, and the
`{GUID}` is not referenced by any `CLSID`'s `TypeLib` value, the TypeLib is orphaned.

**Extra guard:** never report a TypeLib whose server path is under `%SystemRoot%`.

**Evidence:** `TypeLib {2B4C…} references 'C:\Program Files\Contoso\contoso.tlb' which does not
exist; no CLSID references this TypeLib.`

---

## R-008 — Orphaned AppID grouping

**Location:** `HKCR\AppID\*`

**Detection.** An `AppID` is a grouping of `CLSID`s that must be registered/unregistered together
(`HKCR\AppID\{guid}\CLSID` subkeys). An `AppID` is orphaned when it has no `CLSID` subkeys, or all
of its `CLSID` subkeys point to `CLSID`s that no longer exist, and it is not referenced by a
service or by a running application.

**Why S4:** `AppID`s are referenced by service registration and by DCOM launch permissions.
Removing a live one breaks service startup in ways that surface as a hung service, not an obvious
error. Typed confirmation required.

---

## R-009 — Broken Browser Helper Object

**Location:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects\*`,
`HKLM\SOFTWARE\WOW6432Node\...\Browser Helper Objects\*`, `HKCU\...\Explorer\Browser Helper Objects\*`

**Detection.** Each subkey is a `{GUID}` whose default value or `Loader`/`Clsid` points at a
registered `CLSID`. Resolve the `CLSID` → server path → probe. If the server is missing and the
`CLSID` is unreferenced, the BHO is dead.

**Why S2:** BHOs are a legacy shell extension mechanism. A dead one contributes nothing but
clutter, and the underlying browser is unaffected by removing a registration whose DLL is gone.

---

## R-010 — Broken shell extension handler

**Location:** `HKCR\*\shellex\*`, `HKCR\Directory\shellex\*`, `HKCR\Folder\shellex\*`,
`HKCR\Drive\shellex\*`, `HKCR\AllFilesystemObjects\shellex\*`

**Detection.** Each handler value is a `{GUID}` → resolve to `CLSID` → server path → probe. If the
server DLL is missing and the `CLSID` is unreferenced elsewhere, the handler is dead.

**Why S3:** Explorer enumerates shell extensions at folder view time. A broken handler usually
causes a silent no-op, but some are referenced by applications that call them directly.

---

## R-011 — Broken context menu handler

**Location:** `HKCR\*\shellex\ContextMenuHandlers\*`, `HKCR\Directory\Background\shellex\ContextMenuHandlers\*`

**Detection.** Same as R-010.

**Why S3:** same reasoning as R-010, with the added nuance that a missing context menu handler is
a very common cause of "right-click is slow" — a genuinely useful finding for the user.

---

## R-012 — Stale startup entry

**Location:** `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`,
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, `RunOnce`, `RunOnceEx`,
`HKLM\SOFTWARE\WOW6432Node\...\Run`

**Detection.** Parse the command line (handle quotes, `%VAR%` expansion, and arguments). Probe the
executable. Missing → stale.

**Extra guard:** if the path is on a network drive → cap confidence at 69 (the share may simply be
disconnected). If the command references a `.bat`/`.cmd` wrapper whose target script is missing,
also check the script's contents for a nested path.

**Evidence:** `Startup entry 'Contoso Updater' runs 'C:\Program Files\Contoso\updater.exe',
which does not exist.`

**Why S2:** a startup entry pointing at a missing file produces an error dialog on every logon.
Removing it is a clear improvement with no functional loss.

---

## R-013 — Stale StartupApproved entry

**Location:** `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`,
`Run32`, `StartupFolder`

**Detection.** The value names are the startup entries that were enabled/disabled by the user. If
the corresponding startup entry no longer exists (checked against R-012's inventory), this
approval record is residue.

**Why S2:** purely administrative metadata for an entry that no longer exists.

---

## R-014 — Orphaned Installer product record

**Location:** `HKLM\SOFTWARE\Classes\Installer\Products\*`,
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\*\Products\*`

**What it finds:** a Windows Installer product record whose cached MSI package is gone. These are
the entries that make a program appear in Apps & Features even after its files were removed
manually.

**Detection.** The product key name is a *packed* GUID. Decode it to the product code, then:
1. Locate the `LocalPackage` value → the `.msi` path.
2. Probe the `.msi`. Missing → candidate.
3. Cross-check `HKLM\SOFTWARE\Classes\Installer\Products` and the `UserData` tree for a matching
   product code — both must be dead for the record to be removable.
4. Check that no `Component` entry in `HKCR\Installer\Components` still references this product's
   components.

**Why S3:** the installer database is cross-referenced and partially transactional. Removing a
product record whose components are still registered can leave the installer database inconsistent,
which shows up later as a failed uninstall or a repair prompt.

---

## R-015 — Orphaned Installer component record

**Location:** `HKCR\Installer\Components\*`

**Detection.** A component record whose owning product no longer exists, and which is not
referenced by any remaining feature.

**Why S4:** components are the finest-grained unit of the installer database and are referenced from
many places. This rule requires whole-graph analysis and gets the highest non-critical tier.

---

## R-016 — Stale font registration

**Location:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`

**Detection.** Each value maps a font display name to a file path. Probe the file.

**Extra guard:** never report a font whose file resides in `%SystemRoot%\Fonts` (see
`SAFETY.md` §4.3).

**Why S3:** Windows silently substitutes a fallback font for a missing one, so the visible symptom
is usually a subtle rendering change in an application that specifies that font by name — the kind
of problem that gets misattributed for months.

---

## R-017 — Stale printer registration

**Location:** `HKLM\SYSTEM\CurrentControlSet\Control\Print\Printers\*`,
`...\Control\Print\Monitors\*`

**Detection.** The printer's `Printer Driver` value names a driver; the driver's `Driver` key
points at a `.dll` under the driver store. If the driver is absent, the printer registration is
stale. Also detect printers whose `Port` names a port that no longer exists.

**Why S4:** this is the only rule in the catalog that writes under `HKLM\SYSTEM`. The printer
spooler holds handles to these keys and can recreate or conflict with them. Typed confirmation
required; the `Ports` subtree is never removed.

**Alternative considered and rejected:** removing stale printers via `Remove-Printer` /
`spooler` APIs instead of the registry. It is safer for the spooler but out of scope for a
registry tool — noted as a possible future integration.

---

## R-018 — Stale ODBC DSN

**Location:** `HKLM\SOFTWARE\ODBC\ODBC.INI\*` (64-bit and 32-bit views), `HKCU\Software\ODBC\ODBC.INI\*`

**Detection.** The DSN names a driver. Check that the driver is registered under
`HKLM\SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers\*` in the corresponding view. If the driver is not
installed → the DSN cannot be used.

**Why S3:** DSNs are referenced by application configuration and connection strings that live
outside the registry. Removing a DSN breaks a connection at runtime, not at launch.

---

## R-019 — Shell navigation residue

**Location:**
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RunMRU`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\*` (ROT13-encoded)

**Detection.** These record where the user has navigated. Each entry holds a path; probe it.
Missing → residue.

**Why S1:** this is shell history, not configuration. No application depends on it. Removing it
cannot affect any running software; the only effect is that the recent-items list and the "type a
path" suggestion dropdown lose entries for files that no longer exist anyway.

**Note:** this rule is a **privacy** feature as much as a cleanup feature. It is included in `S1`
because it is genuinely harmless, and the GUI labels the category as "Shell history" so users
understand what they are removing.

---

## R-020 — Stale policy startup entry

**Location:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run`,
`HKCU\...\Policies\Explorer\Run`

**Detection.** Same as R-012, but the target is expected to be on a domain share or a path that may
not be mounted.

**Why S4:** policy entries are set by an administrator, often to run something on a network share.
A missing target is usually "the share is not mounted", not "the program is gone". Network paths
are capped at confidence 69, and this rule requires typed confirmation.

---

## R-021 — Large registry value (report only)

**Location:** any scanned key

**Detection.** Value data exceeding 512 KB.

**Tier: S5 — not selectable.** This is an informational report, not a cleanup target. A large
value is usually a legitimate cache, an icon cache, or application state that the application
manages itself. Deleting it risks breaking the owning application, and the space reclaimed is
trivial.

**Why it exists at all:** users want to know where registry bulk lives. The tool tells them; it
does not offer to remove it.

---

## R-022 — Empty key (clutter)

**Location:** any scanned key

**Detection.** A key with no values and no subkeys.

**Tier: S1, disabled by default.** Deleting an empty key is harmless in itself, but the noise-to-value
ratio is poor and some applications use empty keys as markers.

**Why it exists:** it gives users who like a spotless registry a safe, opt-in sweep.

---

## R-023 — Pending file operations (report only)

**Location:** `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations`

**Detection.** Report the presence and the number of queued operations.

**Tier: S5 — not selectable, structurally.** This list holds file rename/delete operations that
Windows will perform at the next boot, typically left by an installer. Deleting the value discards
those queued operations, which can leave half-installed files and orphaned directories. It is also
a signal that a reboot is pending — which `SAFETY.md` §7 already uses to block applies.

---

## Considered and deliberately not implemented

| Idea | Why not |
|------|---------|
| Remove Windows Store / Appx package registrations | Windows manages these; manual removal breaks the package repository. AppX cleanup belongs to `Remove-AppxPackage`, not to a registry cleaner |
| Remove WinSxS component store entries | The component store is not a registry feature; "cleanup" tools that touch it are performing file operations, not registry cleanup |
| Remove driver store / `DriverStore\FileRepository` | File-system operation, high risk, and unrelated to the registry |
| Optimize / compact hives | Explicitly out of scope (ADR-010) |
| Remove `AppInit_DLLs`, IFEO, `Winlogon` | These are attack-surface components; a cleaner touching them is a malware vector, not a utility |
| Remove Windows Update history | Managed by the update agent; removal is unsupported and causes re-download loops |
| Remove Office / Visual Studio shared components | Shared components are referenced by other products; identification requires the VS installer API, not the registry |
| Remove `HKCU\Software` entries for uninstalled apps | No reliable signal exists; the app may be portable and reinstallable. Would produce the majority of false positives |
| Remove network share mappings | `HKCU\Network` entries are recreated on reconnect and are not clutter |
| Clear `MuiCache`, `UserAssist`, `MountedDevices` history | `UserAssist` is included via R-019; `MuiCache` and `MountedDevices` affect UI behavior and mount points — the mount point case in particular can break drive letters |

---

## Rule authoring checklist

Every new rule must satisfy all of the following before it ships:

- [ ] Registered in `RuleCatalog` with a stable ID, name, category, and inherent risk tier.
- [ ] Emits evidence that states a concrete, verifiable fact.
- [ ] Uses `IFileSystemProbe` and treats `Unverifiable` as distinct from `Missing`.
- [ ] Consults the reference index before making any "orphan" claim.
- [ ] Handles both registry views where relevant, and declares which views it inspects.
- [ ] Never targets a path in `SAFETY.md` §4.1.
- [ ] Has a "must find" fixture and a "must not find" fixture in the test harness.
- [ ] Is covered by the manual verification checklist in `SAFETY.md` §9 where applicable.
- [ ] Is listed in this document with its tier and confidence baseline.
