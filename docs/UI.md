# Interface Specification

Two front-ends over one core: `RegistryCleaner.Cli` (primary, scriptable) and
`RegistryCleaner.Gui` (WPF, review-oriented). Both call the same Core services, so cleanup
behavior is identical; only presentation differs.

---

# Part 1 — CLI

## 1.1 Global options

| Option | Default | Description |
|--------|---------|-------------|
| `--elevation <auto\|always\|never>` | `auto` | `auto` scans without elevation and relaunches elevated only when applying. `always` forces a relaunch. `never` disables elevation entirely and disables `apply` |
| `--no-color` | off | Disable ANSI color (also implied when output is redirected) |
| `--json` | off | Emit machine-readable JSON to stdout instead of the console table |
| `--verbose` | off | Per-finding diagnostics on stderr |
| `--config <path>` | `%ProgramData%\RegistryCleaner\config.json` | Configuration file |

## 1.2 Command overview

```
rc scan      [options]                    # read-only; list findings
rc plan      [options] --out <file>       # write a plan file
rc apply     --plan <file> | --severity <tier> | --rule <id>...
rc restore   <backupId>                   # reverse a completed apply
rc backups                                   # list available backups
rc verify    [backupId]                   # integrity-check backups and journals
rc rules                                   # list the rule catalog
rc export    --out <file>                 # HTML or JSON report
```

## 1.3 `rc scan`

```
rc scan [--severity <tier>] [--rule <id>]... [--category <name>]
        [--confidence <min>] [--include-speculative]
        [--search <text>] [--out <file>] [--json]
```

| Option | Description |
|--------|-------------|
| `--severity <tier>` | `safe`, `low`, `moderate`, `high`, `critical`. Repeatable. Default: all except `critical` |
| `--rule <id>` | Filter by rule ID (`R-005`). Repeatable |
| `--category <name>` | Filter by category. Repeatable |
| `--confidence <min>` | Minimum confidence, 0–100. Default: 70 |
| `--include-speculative` | Include findings in the 40–69 confidence band |
| `--search <text>` | Substring match over summary, explanation, evidence, and location |
| `--out <file>` | Write the full result set to a JSON file (the console still shows a summary) |

**Output.** A table sorted by risk ascending, then reclaimed size descending:

```
Registry scan — DESKTOP-ABC — 2026-11-20 20:41:09
Scanned 4 registry views · 23 rules · 38.4 s

  #  TIER  CONF  RULE    CATEGORY                     SUMMARY
  1  S1    95%   R-019   Shell history                Recent file 'Q3-report.docx' no longer exists
  2  S1    95%   R-019   Shell history                Recent folder 'C:\Users\joe\Desktop\old' no longer exists
  3  S2    95%   R-001   Stale app registration       'Contoso Widget Suite' is still listed in Apps & Features
  4  S2    95%   R-012   Startup                      Startup entry 'Contoso Updater' targets a missing file
  5  S3    85%   R-005   COM registration             CLSID {0F1E2D3C-…} has no remaining references

  5 findings · 1 critical tier excluded (--severity critical to show)
  Estimated registry bytes removable: 214 KB

  Detail:  rc scan --rule R-005 --json
  Apply:   rc plan --rule R-005 --out plan.json  →  rc apply --plan plan.json
```

**Exit codes:** `0` no findings, `1` findings present, `2` error.

## 1.4 `rc plan`

```
rc plan [--severity <tier>] [--rule <id>]... [--category <name>]...
        [--confidence <min>] [--include-speculative] [--search <text>]
        --out <plan.json>
```

Writes an immutable plan file containing the exact targets and their expected current state. The
plan records the scan's timestamp and the machine identity; `apply` re-verifies each target
against it.

**Exit codes:** `0` plan written, `4` nothing selected, `2` error.

## 1.5 `rc apply`

```
rc apply --plan <file> [--yes]
rc apply --severity <tier> [--rule <id>]... [--yes]
```

Requires elevation (exit `3` otherwise), passes the system state guard (`SAFETY.md` §7), writes and
verifies a backup, then removes only the selected targets.

| Option | Description |
|--------|-------------|
| `--yes` | Skip the interactive prompt |
| `--accept-moderate` | Required to apply any `S3` finding |
| `--accept-high` | Required to apply any `S4` finding |
| `--dry-run` | Perform everything except the removals — useful for validating a plan |

The flags are cumulative and deliberate: `--yes` alone never authorizes `S3` or `S4` work. A script
must state its intent explicitly for each elevated tier.

**Output on success:**

```
Applied 12 removals from backup 20261120-204312-a41f
  Removed   9  S1
  Removed   3  S2
  Skipped   0
  Failed    0

Backup:  C:\ProgramData\RegistryCleaner\Backups\20261120-204312-a41f\backup.reg
Undo:    rc restore 20261120-204312-a41f
```

**Exit codes:** `0` success, `2` error, `3` elevation required, `4` nothing selected,
`5` blocked by system state, `6` partial completion (see the journal).

## 1.6 `rc restore`

```
rc restore <backupId> [--yes] [--dry-run]
```

Restores every entry in the backup manifest. Prints what will be restored before doing it, unless
`--yes`. The backup is archived, not deleted.

**Exit codes:** `0` restored, `2` error, `3` elevation required, `4` backup not found.

## 1.7 `rc backups`, `rc verify`, `rc rules`, `rc export`

| Command | Purpose |
|---------|---------|
| `rc backups` | Lists backups with timestamp, entry count, and size |
| `rc verify [backupId]` | Validates that a backup parses, its SHA-256 matches, and its entries are restorable. With no argument, verifies all backups |
| `rc rules` | Lists every rule with its ID, category, tier, and confidence baseline — useful for understanding what the tool looks for |
| `rc export --out <file>` | Writes an HTML report (default) or JSON (`--format json`) |

## 1.8 Scripting notes

```powershell
# Find everything in the "high" tier and review it before acting
rc scan --severity high --json | ConvertFrom-Json |
    Select-Object -ExpandProperty findings |
    Format-Table id, tier, confidence, summary

# Clean only the safe tier, unattended
rc apply --severity safe --yes

# Full audit trail
rc scan --out scan.json
rc export --out report.html
```

All commands are non-interactive except when a confirmation is required, and every prompt has a
flag that satisfies it for unattended use.

---

# Part 2 — WPF GUI

## 2.1 Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Registry Cleaner                                          [Scan] [Export]    │
│  DESKTOP-ABC · scan 20:41:09 · 5 findings                                 │
├────────────────────┬──────────────────────────────────┬──────────────────────┤
│ FILTERS            │ FINDINGS                         │ DETAIL               │
│                    │                                  │                      │
│ ▾ S1 Safe (2)     │ TIER CONF RULE  SUMMARY           │ Contoso Widget       │
│    Shell history 2 │ S1   95%  R-019 Recent file '…'   │ Suite is still       │
│ ▾ S2 Low (2)      │ S1   95%  R-019 Recent folder '…' │ registered in Apps   │
│    App registr. 1 │ S2   95%  R-001 'Contoso Widget…'  │ & Features           │
│    Startup     1  │ S2   95%  R-012 Startup entry '…'  │                      │
│ ▾ S3 Moderate (1) │ S3   85%  R-005 CLSID {0F1E…}     │ WHAT IT IS           │
│    COM regist. 1  │                                  │ …                    │
│ ▸ S4 High (0)     │                                  │ WHY IT IS STALE      │
│ ▸ S5 Critical (0) │                                  │ …                    │
│                    │                                  │ EVIDENCE             │
│ [search…]         │                                  │ …                    │
├────────────────────┴──────────────────────────────────┴──────────────────────┤
│ 5 findings · 1 selected (S1) · ~214 KB        [Back up & clean selected]    │
└──────────────────────────────────────────────────────────────────────────────┘
```

## 2.2 Left pane — filters

A tree: **Severity → Category → Rule**, each node showing a count. Nodes can be expanded and used
as filters. `S5` is shown in a distinct, muted style and is visually marked *read-only*.

Above the tree:
- a search box (filters on summary, explanation, evidence, and path);
- an "include speculative findings" toggle (reveals the 40–69 confidence band, which is hidden by
  default);
- a sort toggle: **Risk ascending** (default) ⇄ **Risk descending**.

## 2.3 Center pane — findings table

Columns: `Tier` (colored badge), `Confidence` (bar + percentage), `Rule`, `Category`, `Summary`,
`Location` (path, truncated with a tooltip), `Software`.

- Sortable on every column.
- Multi-select with checkboxes. The checkbox is disabled for `S5` rows.
- Row coloring is subtle — a tier accent on the badge, not a full-row fill, so long lists stay
  readable.
- Double-click opens the location in `regedit` (read-only browse; the tool never asks regedit to
  write anything).

## 2.4 Right pane — detail

For the selected finding:

| Section | Content |
|---------|---------|
| **Summary** | One line, plain language |
| **What it is** | What this registry entry belongs to and what it controls |
| **Why it is flagged** | The reasoning in plain English — no jargon without explanation |
| **Evidence** | The raw fact, in a monospace box, selectable and copyable |
| **If you remove it** | The concrete consequence, including "nothing visible will change" where that is true |
| **Risk & confidence** | Tier badge, confidence bar, and the reason for the tier |
| **Location** | Full path, with a "Copy path" button and an "Open in regedit" button |
| **Backup reference** | Which backup contains this entry, if a previous apply included it |

Every string in this pane is authored per rule — it is not generated from a template. A rule's
`Description` and per-finding `Explanation` are written by a human for that specific rule.

## 2.5 Actions

| Action | Behavior |
|--------|----------|
| **Scan** | Runs the scan; shows a progress indicator with the current phase (reading hive → building reference index → running rules). The scan is cancellable |
| **Clean selected** | Runs the confirmation flow, then the apply pipeline. Shows a progress dialog with per-item results |
| **Back up only** | Writes a backup of the current selection without removing anything — lets a cautious user capture a restore point in advance |
| **Export report** | Writes an HTML report of the current filtered findings |
| **Restore** | Opens the backup history window; restores a chosen backup |

## 2.6 Confirmation flow

Escalating by the highest tier in the selection (`SAFETY.md` §5):

| Tier | Dialog |
|------|--------|
| S1–S2 | "Remove 4 selected items. All are low-risk. A backup will be created first." → **Remove** / **Cancel** |
| S3 | The same, plus a checkbox: "I understand these may affect applications that are currently installed." The **Remove** button stays disabled until it is checked |
| S4 | The same, plus a text field requiring the user to type `REMOVE`. Typing anything else keeps the button disabled |

The dialog also states where the backup will be written and shows the exact undo command.

## 2.7 Elevation

The GUI's `app.manifest` declares `requireAdministrator`, so Windows prompts for elevation at
launch and the process runs elevated for its whole lifetime. The window title bar shows an
"Administrator" indicator so the state is never ambiguous.

A scan does not strictly require elevation — `HKCU` and much of `HKCR` are readable as a standard
user. The tool runs elevated for simplicity and to avoid partial results, and the UI says so.

## 2.8 Behaviour details

- **First run:** the window opens on the scan screen with an empty state that explains what the tool
  does and offers a **Scan** button. It never scans silently at launch.
- **Session state:** the last scan's results are kept in memory only. Nothing is written outside
  `%ProgramData%\RegistryCleaner` without the user performing an action.
- **No network:** the application has no networking code at all, and no analytics.
- **Theme:** follows the system light/dark setting.
- **Accessibility:** full keyboard navigation; the tier is conveyed by text and icon, not by color
  alone; screen-reader labels on the tree and grid.
- **Localization:** strings are resources, not literals in XAML, from day one.

## 2.9 Application states

| State | Presentation |
|-------|--------------|
| Not yet scanned | Empty-state panel: explanation, "Scan" button, and a link to the rule catalog |
| Scanning | Determinate progress with the phase name and the current rule |
| Scan complete, no findings | "No stale entries found" plus the scan scope and timestamp |
| Scan complete, with findings | The main layout, populated |
| Apply in progress | Modal progress dialog, per-item results streaming in, cancellable before the first removal |
| Apply complete | Result summary with the backup location and the undo command |
| Error | A dismissible banner with the error, the technical detail behind a disclosure, and the journal path |

---

# Part 3 — Shared behavior

Both front-ends share these guarantees, because they are implemented in Core, not in the UI:

1. A scan never writes to the registry.
2. Apply always writes and verifies a backup first.
3. Protected paths are never removed, regardless of tier or rule.
4. Only user-selected items are removed.
5. Every action is journalled.
6. Failures are reported, never silently swallowed.
