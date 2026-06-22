# Hash Check — Build Plan

A Windows system-tray application that creates and verifies file-integrity hashes for
archived media, to detect **bit-rot** (silent data corruption) over time.

This document is the build specification. Implement it in phases (see *Roadmap*).
Confirm any deviation from the `.hash` file format with the user before changing it.

---

## 1. Goal & context

The user archives data to media (external drives, optical, cloud-synced folders) that may
sit untouched for months or years. Bits can flip silently (bit-rot). Hash Check records a
hash of every file when the archive is created, then reminds the user every *N* days to
re-verify the media so corruption is caught early.

Threat model is **accidental corruption, not tampering**. This justifies a fast,
non-cryptographic hash by default. It also lets the report distinguish *corruption* (hash
changed, but size and modified-time unchanged) from a *legitimate edit* (modified-time
changed).

---

## 2. Platform & tech stack

- **Language:** C# on **.NET 8**.
- **UI:** WPF for all windows/dialogs.
- **Tray:** WinForms `NotifyIcon` hosted inside the WPF app (standard WPF+WinForms interop)
  for the system-tray icon and context menu.
- **Hashing:** `System.IO.Hashing` NuGet package (provides `XxHash3`, `XxHash64`,
  `XxHash128`, `Crc32`, `Crc64`) plus `System.Security.Cryptography` (`MD5`, `SHA1`,
  `SHA256`).
- **Volume info:** `System.IO.DriveInfo` for labels/sizes; P/Invoke `GetVolumeInformation`
  (kernel32) or WMI (`Win32_LogicalDisk.VolumeSerialNumber`) for the volume serial number.
- **Autostart:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry value
  (toggleable in settings).
- **Packaging:** single self-contained executable (`dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained`).

The app is single-instance (named mutex). Launching a second time surfaces the existing
window/menu rather than starting a new process.

---

## 3. Core concepts

- **Media** — one physical/logical volume being protected. Identified by its **volume
  serial number** (stable) plus label and total size — never by drive letter, which changes.
- **Hash set / `.hash` file** — one file per media, recording the header, scope, and a hash
  for every file. Extension `.hash`.
- **Validation** — re-hashing the media and comparing against its `.hash` file, producing a
  report.
- **Reminder** — a daily background check; when `due date` is reached the user is prompted to
  re-validate.
- **Autoscan** — optional: when a media is re-validated (or on demand), detect files/folders
  added since creation and fold them into the `.hash` set.

---

## 4. The `.hash` file format (authoritative)

Plain UTF-8 text, one record per line. Extension `.hash`. **This format is fixed; do not
change it without user confirmation.**

```
HASHCHECK/1.0
[META]
Description=Wedding photos master archive
MediaName=Wedding_Master_2025
VolumeLabel=WED2025
SerialNumber=A1B2-C3D4
MediaTotalBytes=1999843328000
Algorithm=XxHash3
ReminderDays=180
FilterMode=Exclude
DateCreated=2026-05-30T14:22:10Z
DateModified=2026-05-30T14:22:10Z
[FILTERS]
Thumbs.db
*.tmp
System Volume Information\
[VALIDATIONS]
2026-05-30T15:01:22Z|PASS|files=3|bytes=5243904|matching=3|notmatching=0|missing=0|errors=0
[PATHS]
\
\Ceremony
\Reception
[FILES]
\
readme.txt|9f86d081...a08|1024|2025-06-14T08:00:00Z
\Ceremony
IMG_0001.jpg|2c26b46b...9824|2097152|2025-06-14T09:30:00Z
\Reception
IMG_0500.jpg|486ea462...0d1|3145728|2025-06-14T21:05:00Z
[INTEGRITY]
SHA-256:7d865e959b2466918c9863afca942d0fb89d7c9ac0c99bafc3749504ded97730
```

### Section rules

- **`HASHCHECK/1.0`** — format magic + version on line 1.
- **`[META]`** — `Key=Value` pairs. All timestamps ISO-8601 UTC. `Algorithm` records the
  hash used so validation always re-hashes identically even if the default changes later.
  `FilterMode` is `Include` or `Exclude`. `DateModified` updates on any autoscan change.
- **`[FILTERS]`** — one glob/pattern per line. Interpreted per `FilterMode`. A trailing `\`
  means a directory pattern.
- **`[VALIDATIONS]`** — append-only audit log, one line per validation:
  `timestamp|PASS|FAIL|...stats`. Drives the "last verified" status and the due-date reset.
- **`[PATHS]`** — flat, sorted list of every directory (relative to volume root, leading
  `\`). Kept deliberately even though directories also head the `[FILES]` section, because it
  lets the UI display the full scope quickly without parsing every file line. Also captures
  **empty directories** and supports missing-directory detection.
- **`[FILES]`** — grouped by directory to avoid repeating the path on every line:
  - A line **starting with `\` and containing no `|`** is a **directory header**; it sets the
    current directory for the lines beneath it (full relative path).
  - Every other line is a file in the current directory:
    `filename|hash|sizeBytes|modifiedUtc`. Hash is lowercase hex.
  - `|` is illegal in Windows filenames, so it is a safe delimiter.
- **`[INTEGRITY]`** — footer: `SHA-256:<hex>` computed over **all bytes from the start of the
  file up to and including the newline immediately before the `[INTEGRITY]` line**. Always
  SHA-256 regardless of the file-content algorithm (it's one small one-shot hash, and a
  fixed, well-understood algorithm here keeps verification trivial). This detects a `.hash`
  file that has itself been corrupted or truncated, so a damaged index never silently reports
  every file as failed.

### Paths
All stored paths are **relative to the volume root** (leading `\`), so they survive
drive-letter changes.

---

## 5. Hash algorithms

- **Default = fastest:** `XxHash3` (64-bit XXH3 via `System.IO.Hashing`).
- **User-selectable per hash set** (dropdown), in roughly descending speed:
  `XxHash3`, `XxHash128`, `Crc64`, `Crc32`, `MD5`, `SHA1`, `SHA256`.
- The chosen algorithm is stored in `[META].Algorithm` and used verbatim on validation.
- Hashing reads files in buffered streams (e.g. 1 MiB chunks) so multi-TB media don't blow
  memory.

---

## 6. Application settings (global)

Stored in a per-user config file (e.g. `%APPDATA%\HashCheck\settings.json` — internal app
config may be JSON; only the `.hash` format is fixed text).

- **Default hash-storage location** — where new `.hash` files are saved (a physical drive,
  Google Drive / synced folder, etc.). Per-set override allowed at creation time.
- **Default reminder interval (days).**
- **Default hash algorithm** (defaults to `XxHash3`).
- **Autoscan media** — global default for the per-set autoscan toggle.
- **Run at login** (autostart) — on/off, writes/removes the registry Run key.
- **Known `.hash` locations** — folders the daily reminder check scans for `.hash` files.

---

## 7. Functional requirements

### 7.1 Tray application
- Lives in the system tray with a context menu: *Create new hash…*, *Validate media…*,
  *Dashboard*, *Settings*, *Exit*.
- Starts hidden on login (when autostart enabled); double-click tray icon opens the
  Dashboard.
- Long operations run on background threads; the tray and UI stay responsive.

### 7.2 Create a hash set
1. **Scope selection:** a `TreeView` with **tri-state checkboxes**: drives → directories →
   subdirectories, children lazy-loaded on expand. Ticking a folder includes it and (by
   default) recurses into all subdirectories; individual subdirectories can be unticked
   (tri-state shows partial selection).
2. **Details prompt:** a dialog for
   - **Description** (required — written to `[META].Description`),
   - **Reminder days** (defaulted from settings),
   - **Algorithm** (defaulted to fastest),
   - **Filter mode** radio: **Include** or **Exclude**, plus an editable pattern list,
   - **Storage location** for the `.hash` (defaulted from settings).
3. **Capture volume identity** (label, serial, total bytes) for the selected media.
4. **Hash** every file in scope (recursing subdirectories where selected), honouring filters.
   Show a **progress window**: files done / total, bytes done / total, current file, ETA, and
   a **Cancel** button.
5. Write the `.hash` file (with `[INTEGRITY]` footer) to the chosen location.

> One `.hash` file per media. A set may span an entire volume or selected directories, but it
> always describes a single volume.

### 7.3 Reminders
- A scheduler runs the check **on launch** and then **once per day** (and catches up any days
  the app was off).
- For each known `.hash` file, **due date = (most recent `[VALIDATIONS]` date, else
  `DateCreated`) + `ReminderDays`**. When `now ≥ due`, raise a reminder.
- Reminder popup names the media and offers: **Validate now**, **Snooze N days**, **Dismiss**.

### 7.4 Validate media (handles offline media)
1. From a reminder or the menu, the user picks a `.hash` file.
2. **Verify the `.hash` itself** against its `[INTEGRITY]` footer first; warn if corrupt.
3. **Locate the media:** poll `DriveInfo` for a mounted volume whose serial matches
   `[META].SerialNumber`. If absent, show an **"Insert media: *MediaName*"** prompt that keeps
   polling until the matching volume appears (with a manual drive-picker override and Cancel).
4. Re-hash every file listed (and walk the scope for new/extra files), comparing against the
   stored hashes.
5. Append a line to `[VALIDATIONS]` and produce a **report** (§7.5).

### 7.5 Validation report
Summary (the required figures, plus a few that make the result actionable):
- **Total Files / Directories** found on the media (in scope).
- **Total Bytes** on the media (in scope).
- **Total Files in hash file.**
- **Total Matching.**
- **Total Not Matching**, split into:
  - **Corrupted** — hash differs but size *and* modified-time unchanged (likely bit-rot),
  - **Modified** — hash differs and size or modified-time changed (likely a legitimate edit).
- **Total Missing** — in the hash file but not found on the media.
- **Total Errors** — files that could not be read (locked / permission / I/O error).
- **Total New** — present on the media but not in the hash file (informational).

Then **detail lists** (full relative path):
- Missing files/directories,
- Not-matching files (each labelled *Corrupted* or *Modified*),
- Unreadable/error files,
- New files (informational).

Report is shown on screen and **exportable** (PDF and/or CSV/HTML) saved next to the `.hash`
or to a reports folder.

### 7.6 Autoscan
- Per-set toggle (defaulting from settings) stored with the set.
- When enabled, validation also detects items added since creation and **adds** new
  files/folders to the set (re-hashing only the new files), updating `[PATHS]`, `[FILES]`,
  `DateModified`, and refreshing `[INTEGRITY]`.
- Decide and document behaviour for deletions (recommend: report as Missing, do **not**
  auto-remove, so accidental data loss is surfaced rather than hidden).

### 7.7 Dashboard
- Lists every known hash set: media name, description, file count, total bytes, created date,
  last validated, **status** (OK / Overdue / Never verified), next due date.
- Actions: validate now, view last report, open `.hash` location, edit reminder, remove from
  tracking.

---

## 8. Edge cases & robustness

- **Drive-letter changes:** never key on drive letter; always resolve by volume serial.
- **Long paths (>260 chars):** enable long-path support / use `\\?\` prefixed paths.
- **Unicode filenames:** UTF-8 throughout; verify round-trip through the `.hash` parser.
- **Locked/unreadable files:** catch per-file, record as an Error in the report, continue.
- **Symlinks / junctions:** do **not** follow by default (avoid loops and double counting);
  consider a setting later.
- **System/junk paths:** ship sensible default Exclude patterns (e.g. `Thumbs.db`,
  `$RECYCLE.BIN\`, `System Volume Information\`, `*.tmp`).
- **Corrupt/truncated `.hash` file:** detected via `[INTEGRITY]`; surface clearly.
- **App not running on a due date:** caught up on next launch.
- **Cancellation mid-hash:** no partial `.hash` is written (write to temp, atomic rename on
  completion).

---

## 9. Suggested solution structure

```
HashCheck.sln
  HashCheck.App/            WPF app, tray host, windows, view models
    TrayIcon/               NotifyIcon host + context menu
    Views/                  ScopeSelection, Details, Progress, InsertMedia, Report, Dashboard, Settings
    ViewModels/
  HashCheck.Core/           no-UI library (unit-testable)
    Hashing/                IHasher, hasher factory, streaming hashing + progress
    HashFile/               .hash reader/writer, model, integrity footer
    Volumes/                volume identity (serial/label/size), media locator
    Scanning/               directory walk, filters, autoscan diff
    Validation/             comparison engine, report model
    Scheduling/             reminder/due-date logic
    Settings/               app settings store
    Reporting/              report export (PDF/CSV/HTML)
  HashCheck.Tests/          unit tests
```

Keep all logic in `HashCheck.Core` with interfaces so it can be unit-tested without UI.

---

## 10. Roadmap (build in phases)

1. **Core format + hashing.** `.hash` model, reader/writer with `[INTEGRITY]`, hasher
   factory (XxHash3 default), streaming hash. Unit tests round-tripping the format.
2. **Scanning + create flow (headless first).** Directory walk with include/exclude filters,
   volume identity capture, write a complete `.hash`. CLI/test harness before UI.
3. **Validation + report (headless).** Comparison engine producing the full report model,
   `[VALIDATIONS]` append. Tests for matching/corrupted/modified/missing/error/new.
4. **WPF shell + tray.** Tray icon, single-instance, settings store, autostart toggle.
5. **Create UI.** Tri-state drive/folder tree, details dialog, progress window with cancel.
6. **Validate UI.** Media-locator + "insert media" prompt, report window, export.
7. **Reminders + dashboard.** Daily/catch-up scheduler, reminder popup with snooze, dashboard.
8. **Autoscan.** Diff + add new items into existing sets.
9. **Packaging.** Single-file self-contained exe; verify run-at-login.

---

## 11. Acceptance criteria (high level)

- Creating a set on a folder tree produces a valid `.hash` that re-parses identically and
  whose `[INTEGRITY]` footer verifies.
- Flipping a single byte in a data file (without touching its timestamp) is reported as
  **Corrupted**, not Modified.
- Editing a file (changing its mtime) is reported as **Modified**, not Corrupted.
- Deleting a file is reported as **Missing**; adding a file is **New** (or folded in when
  autoscan is on).
- The same media reported under a different drive letter still validates (matched by serial).
- With the media absent, validation shows the **Insert media** prompt and proceeds once the
  matching volume is mounted.
- A set whose due date has passed appears **Overdue** on the dashboard and triggers a reminder
  popup, including after the app was closed over the due date.
- Corrupting the `.hash` file is detected before validation runs.

---

## 12. Interpretation notes (flag if you disagree)

- **Due-date reset:** the reminder clock restarts from the most recent successful validation,
  not only from creation, so a re-verified archive isn't nagged the next day. Change to
  "always from creation" if preferred.
- **`[INTEGRITY]` algorithm:** always SHA-256, independent of the file-content algorithm.
- **Deletions under autoscan:** reported as Missing, never auto-removed.
