# Safety Model

This document is the specification the code is reviewed against. If a change contradicts a rule
here, either the rule changes (with a decision-log entry) or the code is wrong.

---

## 1. The five invariants

These hold for every execution path — CLI, GUI, scheduled run, or future API.

1. **A scan never writes to the registry.** The scan path has no write capability at all.
2. **No removal happens without a complete, verified backup on disk.** Backup → verify → *then*
   mutate. If verification fails, zero removals occur.
3. **Protected paths cannot be removed, by any rule, under any configuration.** The guard is code,
   not convention, and has no bypass API.
4. **Nothing is removed that the user did not explicitly select.** The default selection is the
   `Safe` tier, and even that requires a confirmation step.
5. **Every action is journalled.** Scan, plan, apply, restore, and every skipped/failed item.

Anything that violates one of these is a bug, regardless of how it was reached.

## 2. Risk tiers

| Tier | Name | Definition | Removal friction | Pre-selected? |
|------|------|------------|------------------|---------------|
| **S1** | Safe | Removing the target cannot plausibly affect running software. Shell residue, expired navigation entries, orphaned registration of a program that is definitively uninstalled | Single confirm | Yes |
| **S2** | Low | Administrative residue of a program that is no longer present. Startup entries, app paths, dead uninstall records | Single confirm | No |
| **S3** | Moderate | Registration whose removal could affect an application that is still installed but partially broken. Orphaned COM objects, stale shell extensions, orphaned installer records | Checkbox acknowledgment | No |
| **S4** | High | Could affect system, driver, or shell behavior. Printers, fonts, policy entries | Typed confirmation phrase | No |
| **S5** | Critical | Not offered for removal. Reported for awareness only | Not selectable | No |

### Tier assignment rules

- A rule declares `InherentRisk`. A finding starts at that tier.
- `RiskTierer` may **raise** a tier based on context: a path under a system tree, an entry that
  controls a shell extension, a target that other keys reference.
- A rule may **lower** a tier only for a specific, enumerated subclass (e.g. "an uninstall record
  for a program whose install directory is confirmed absent" is `S2`, while "an uninstall record with
  an unresolvable path" stays at `S3`).
- `ProtectedPathGuard` may only **raise** a tier, and raises to `S5` (non-selectable).

## 3. Confidence

Confidence is the strength of evidence, on 0–100, and it is displayed independently of risk.

| Band | Meaning | Visibility | Selection |
|------|---------|-----------|-----------|
| 90–100 | Verified absent on disk **and** proven unreferenced | Always | Allowed per tier rules |
| 70–89 | Verified absent, one supporting signal missing | Always | Allowed per tier rules |
| 40–69 | Pattern-based inference; not independently verified | Only with `--include-speculative` / GUI toggle | Never pre-selected; requires acknowledgment even at S1 |
| 0–39 | Weak | Never shown | Never selectable |

### Confidence reduction rules

| Condition | Effect |
|-----------|--------|
| Target path is on a network drive or UNC path and is currently unreachable | Cap at 69 and mark `Unverifiable` |
| Target path is on removable media that is not currently present | Cap at 69 |
| Path contains environment variables that could not be expanded | Cap at 69 |
| Path could not be parsed (malformed quoting, trailing garbage) | Cap at 49 |
| Registry view could not be determined unambiguously | Cap at 79 |
| A referencing key exists but its own server could not be verified | Cap at 79 |

A finding that is *unverifiable* never states "the file is missing" in its evidence string. It
states "the path could not be verified".

## 4. Protected paths

Enforced by `ProtectedPathGuard` at scan time and again at apply time. Matching is
case-insensitive, and the guard normalizes separators, trailing slashes, `.` and `..` segments, and
8.3 short names before comparing.

### 4.1 Never removed, at any tier

| Path | Reason |
|------|--------|
| `HKEY_LOCAL_MACHINE\SAM` | Local account database — not recoverable |
| `HKEY_LOCAL_MACHINE\SECURITY` | LSA secrets and policies — not recoverable |
| `HKEY_LOCAL_MACHINE\SYSTEM\Select` | Duplicate current-control-set view; writes corrupt the live set |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class` | Device class configuration |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceClasses` | Plug and Play device classes |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard` | Secure boot / code integrity policy |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum` | Enumerated device and driver instances |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services` | Service and driver configuration |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management` | Paging and memory configuration |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\BootExecute` | Boot-time execution |
| `HKEY_LOCAL_MACHINE\SYSTEM\Setup` | Pending setup state |
| `HKEY_LOCAL_MACHINE\BOOT` | Boot loader configuration |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Cache` | MSI/MSP cache — required for repair and uninstall |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` | Shell, Userinit, notifications |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options` | Per-executable redirection/debugging |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows` | AppInit_DLLs and legacy loaders |
| `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders` | Location of the user's folders |
| `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders` | Same, unexpanded |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies` | Group Policy |
| `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies` | Group Policy |
| `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations` | Queued boot-time file operations — report-only |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\*` | Windows Update component store |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Channels` | Event log channel definitions |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate` | Update agent state |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender` | Security policy and exclusions |
| `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects` (whole key) | BHO container — only individual entries with a verified-missing server are eligible, and the container key itself is never deleted |
| `HKEY_CLASSES_ROOT\CLSID\{6D809377-6AF0-444B-8957-A3773F02200E}` and other in-box CLSIDs under `%SystemRoot%` | Shell infrastructure used by Explorer itself |

### 4.2 Protected value names

Within otherwise-eligible keys, these value names are never removed:

`System`, `BootId`, `CurrentVersion`, `PathName`, `ServiceDll`, `ImagePath`, `Start`, `Type`,
`ObjectName`, `Security`, `Parameters`, `EventMessageFile`, `CategoryMessageFile`,
`MessageFile`, `ProviderPath`, `LocationInformation`, `ServiceMain`, `UninstallString` (on a key
whose `DisplayName` is missing — indicates a live system component), `InstallLocation` (on a key
that also has a live `ProductCode`).

### 4.3 Rule-level restrictions

Even for unprotected paths, these rules are restricted:

| Rule family | Restriction |
|-------------|-------------|
| COM rules (`R-004`–`R-007`) | Never remove a `CLSID` whose server path lies under `%SystemRoot%` |
| Shell extension rules (`R-010`, `R-011`) | Never remove a handler whose `InprocServer32` lies under `%SystemRoot%` |
| Font rules (`R-015`) | Never remove a font whose file is in the Windows font directory |
| Printer rules (`R-014`) | `S4`; requires typed confirmation; never removes the `Ports` subtree |

## 5. Confirmation model

| Highest tier in selection | GUI requirement | CLI requirement |
|---------------------------|-----------------|-----------------|
| S1 | Confirm dialog: "Remove N selected items (all low-risk)" | `--yes` or interactive prompt |
| S2 | Confirm dialog showing counts by tier | `--yes` |
| S3 | Confirm dialog **plus** an explicit acknowledgment checkbox: "I understand these may affect installed applications" | `--yes --accept-moderate` |
| S4 | Confirm dialog **plus** typing the phrase `REMOVE` | `--yes --accept-high` |
| S5 | Not selectable | Not selectable |

The CLI requires a separate flag per elevated tier so a script cannot accidentally pass `--yes` and
have it mean "yes to everything". A script that intends S4 cleanup must say so explicitly.

### Backup directory

```
%ProgramData%\RegistryCleaner\
├── Backups\
│   └── 20261120-204312-a41f\
│       ├── backup.reg          # regedit-compatible export
│       ├── manifest.json       # machine-readable, includes SHA-256 of backup.reg
│       └── backup-summary.txt  # human-readable one-page summary
└── Journals\
    └── 20261120-204312-a41f.jsonl
```

`%ProgramData%` is chosen because it is readable and writable by an administrator and survives per-user profile changes. Backups are never stored in `%TEMP%`.

## 6. Backup and restore

### 6.1 Backup contents

`backup.reg` — the standard Windows Registry Editor export format. Chosen because it is the one
registry backup format a user can restore with tools that already exist on their machine.

`manifest.json` — the authoritative record:

```jsonc
{
  "schemaVersion": 1,
  "backupId": "20261120-204312-a41f",
  "createdUtc": "2026-11-20T20:43:12Z",
  "machine": "DESKTOP-ABC",
  "user": "CORP\\joe",
  "scanId": "20261120-204109-77c2",
  "backupSha256": "…",
  "entries": [
    {
      "id": "F-20261120-0031",
      "ruleId": "R-001",
      "hive": "HKEY_LOCAL_MACHINE",
      "keyPath": "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{7A3B…}",
      "valueName": null,
      "view": "Registry64",
      "kind": "Key",
      "risk": "Low",
      "confidence": 95,
      "evidence": "InstallLocation 'C:\\Program Files\\Contoso Widget Suite\\' does not exist",
      "originalValues": [
        { "name": "", "kind": "REG_SZ", "data": "Contoso Widget Suite" },
        { "name": "InstallLocation", "kind": "REG_EXPAND_SZ", "data": "%ProgramFiles%\\Contoso Widget Suite\\" }
      ],
      "subKeyNames": ["SubKey1"]
    }
  ]
}
```

Binary values are base64-encoded. `REG_MULTI_SZ` preserves order. `REG_EXPAND_SZ` retains the
unexpanded string.

### 6.2 Backup verification

After writing, the tool re-reads `backup.reg` and `manifest.json` and checks:
- the file parses and every `entries[]` record is present and well-formed;
- the entry count matches the intended removal count;
- the SHA-256 of `backup.reg` matches the value recorded in `manifest.json`;
- every value type in the manifest is a supported type for restoration.

Any failure → abort the apply, remove the incomplete backup directory, and report. **Zero removals
occur.**

### 6.3 Restore

`restore <backupId>`:
1. Reads the manifest and validates the schema version.
2. Recreates each parent key if it is missing.
3. Restores each value with its exact original type and data.
4. Restores subkey names for keys that were removed wholesale.
5. Journal each restored item.
6. Does **not** delete the backup afterward; the backup is archived and the user is told it can be
   removed manually.

Restore is idempotent: running it twice produces the same result.

## 7. System state guard

Before applying, the tool checks the machine's state and refuses with exit code 5 if any of these
hold:

| Condition | Detection | Message |
|-----------|-----------|---------|
| Running in Safe Mode | `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal` or `Network` present | "Safe Mode detected. Cleaning in Safe Mode is not supported." |
| Pending reboot | `PendingFileRenameOperations` present, or `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations2` | "Windows has pending file operations. Reboot before cleaning." |
| Update in progress | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired` | "A Windows Update requires a reboot." |
| Installer transaction active | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\InProgress` | "An installation is in progress." |
| Disk space below threshold | Free space on the system volume < 512 MB | "Not enough free space to write the backup." |
| Not elevated | Token elevation check | "Administrator privileges are required to apply changes." |

These conditions are exactly when a machine is least able to tolerate a mistake.

## 8. Additional implementation rules

- **Open keys with minimal rights.** The writer opens a key with `KEY_SET_VALUE` for value
  deletions and `DELETE` for key deletions — never `KEY_ALL_ACCESS`.
- **No wildcard deletes.** Every deletion targets an exact, fully-qualified location. There is no
  `DeleteSubKeyTree` convenience wrapper exposed to rules.
- **No recursive delete of a key with unresolved subkeys.** A key containing subkeys is only
  deleted wholesale when the backup captured those subkey names *and* the rule explicitly targets
  the whole tree.
- **Values are deleted individually.** Deleting a key wholesale is reserved for rules that own the
  entire subtree and set `FindingKind.Key` with `OwnsSubtree = true`.
- **Binary data is preserved exactly.** No re-encoding on backup or restore.
- **No writes outside the target.** The tool never creates, modifies, or sets any registry value
  as a side effect of a cleanup operation.
- **Logging never contains secrets.** Journal entries contain registry paths and evidence strings.
  Values that commonly hold credentials (`Password`, `Token`, `Secret`, `LicenseKey`) are excluded
  from evidence strings by name.

## 9. Manual verification checklist

Before any release, verify on a disposable Windows 11 machine:

- [ ] Scan on a clean machine reports no `S1`/`S2` findings.
- [ ] Scan after uninstalling a test application reports its uninstall record as `S2` with evidence.
- [ ] A disconnected network share yields no finding, or yields one capped at confidence 69.
- [ ] A shared COM object referenced by two applications is **not** reported.
- [ ] Selecting an `S3` item requires the acknowledgment checkbox.
- [ ] Apply → Restore returns the registry to a byte-identical state.
- [ ] Apply in Safe Mode is refused with exit code 5.
- [ ] A corrupted backup file causes the apply to abort with zero removals.
- [ ] The tool performs no outbound network connections.
- [ ] Every protected path in §4.1 is confirmed unremovable, including case-variant and
      8.3-short-name bypass attempts.
