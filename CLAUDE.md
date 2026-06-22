# Hash Check — Claude Code Instructions

## What this project is
A Windows system-tray application that creates and validates file-integrity hashes for
archived media, to detect **bit-rot** (silent data corruption). Not a security tool —
fast, non-cryptographic hashing is preferred over tamper-resistance.

## Specification documents
Read these first before touching any code:

1. `_PLANS/plan.md` — original specification (hash algorithms, scanning, validation,
   phased roadmap). Where this file conflicts with `plan.md`, **this file wins**.
2. `_PLANS/plan-amendment-01.md` — covers app lifecycle, Print, and Re-create action.

**The `.hash` file format below is authoritative.** Never change section names, delimiter
choices, field order, or line structure without user confirmation first.

---

## Tech stack

- **Language:** C# on .NET 8
- **UI:** WinUI 3 via `Microsoft.WindowsAppSDK` (not WPF — uses `Microsoft.UI.Xaml`)
- **MVVM:** `CommunityToolkit.Mvvm` 8.2.2
- **Tray:** P/Invoke `Shell_NotifyIconW` + window subclassing via `comctl32` (no WinForms)
- **Hashing:** `System.IO.Hashing` NuGet (`XxHash3` default) + `System.Security.Cryptography`
  (`MD5`, `SHA1`, `SHA256`)
- **Volume info:** `System.IO.DriveInfo` + P/Invoke `GetVolumeInformation` (kernel32)
- **Autostart:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **App config** (not `.hash`): `%APPDATA%\HashCheck\settings.json`
- **Packaging:** MSIX (single-project packaging via `EnableMsixTooling`)

## Project structure

Single `.csproj` — no solution file, no separate Core or Tests projects:

```
HashCheck.csproj
Core/
  HashFile/       HashFileData, HashFileReader, HashFileWriter, VolumeEntry
  Hashing/        IHasher, HasherFactory, HashAlgorithms
  Scanning/       FileScanner, FilterEngine, ScanProgress, AutoscanEngine
  Validation/     ValidationEngine, ValidationReport, PauseToken
  Volumes/        VolumeIdentity, VolumeLocator
  Settings/       AppSettings, SettingsStore
  Scheduling/     ReminderScheduler
Views/            WinUI pages (CreateHashPage, ValidatePage, ReportPage,
                               DashboardPage, SettingsPage, ReCreatePage,
                               MediaGroupPage)
ViewModels/       MVVM view models (one per page + ViewModelBase, FolderNode,
                                    ValidationRow)
Services/         HashSetService, SchedulerService, VolumeAttachedEventArgs
Tray/             TrayIconHost (P/Invoke shell tray)
Converters/       WinUI value converters (incl. BoolToOnlineBrushConverter,
                                           InverseBoolToVisibilityConverter)
App.xaml.cs       Application entry — single-instance mutex, tray, scheduler, autoscan prompt
MainWindow.xaml.cs NavigationView shell, minimize-to-tray on close
AppServices.cs    Static service locator (Settings, HashSets, Scheduler)
```

All functional logic lives under `Core/`. WinUI/tray code only in `Views/`, `ViewModels/`,
`Tray/`, `App.xaml.cs`, and `MainWindow.xaml.cs`.

---

## Build & run

Build requires Visual Studio 2022 or the Windows App SDK workload for the WinUI toolchain.

```powershell
# Build (x64 debug)
dotnet build -p:Platform=x64

# Run via VS — set HashCheck as startup project, target x64

# Publish MSIX
# Use VS: Build → Publish → Create App Packages
```

> `dotnet run` does not work for WinUI 3 MSIX projects. Use Visual Studio to run/debug.

---

## Key conventions

### `.hash` file format (authoritative — do not change without user confirmation)

Plain UTF-8, no BOM, extension `.hash`. Section order is fixed:

```
HASHCHECK/1.0
[META]
Description=<user description>
MediaName=<group name — derived from volume label at creation time>
Algorithm=XxHash3
ReminderDays=180
FilterMode=Exclude
Autoscan=False
DateCreated=<ISO-8601 UTC>
DateModified=<ISO-8601 UTC>
[VOLUMES]
<serial>|<label>|<totalBytes>|<dateAdded ISO-8601 UTC>|<scanSubPath>
<serial>|<label>|<totalBytes>|<dateAdded ISO-8601 UTC>|<scanSubPath>
[FILTERS]
$RECYCLE.BIN\
*.tmp
[VALIDATIONS]
<timestamp ISO-8601 UTC>|<PASS|FAIL>|volume=<serial>|files=N|bytes=N|matching=N|notmatching=N|missing=N|errors=N
[PATHS]
\
\Ambulance - Merc
\Corfe Castle
[FILES]
\
IMG_001.jpg|<hash>|<sizeBytes>|<modifiedUtc ISO-8601>
\Ambulance - Merc
IMG_002.jpg|<hash>|<sizeBytes>|<modifiedUtc ISO-8601>
[INTEGRITY]
SHA-256:<hex>
```

Key rules:
- `|` is the only delimiter — safe because `|` is illegal in Windows filenames
- `[PATHS]` is a fast scope index separate from `[FILES]` — do not merge them
- `[INTEGRITY]` is always SHA-256 over all bytes before it (no BOM), regardless of content hash algorithm
- **All stored paths are relative to the scan root** (the `ScanSubPath` folder for each volume),
  not relative to the volume root. `[PATHS]` always starts with `\` (the scan root itself).
- `[VOLUMES]` has five `|`-delimited fields; the 5th is `ScanSubPath` — the path of the scan
  root relative to the drive root (e.g. `\_PHOTOS\2026`, or `\` if the whole drive is scanned).
- There is **no backward compatibility** with old single-volume or old-path-format files.
- `[VALIDATIONS]` records include `volume=<serial>` to identify which copy was validated

### Scan root vs volume root

The **scan root** is the specific folder the user selected when creating the hash set (e.g.
`Z:\_PHOTOS\2026`). `ScanSubPath` stores this relative to the drive root (`\_PHOTOS\2026`).
All `[PATHS]` and `[FILES]` entries are relative to the scan root, so `\Ambulance - Merc`
means `Z:\_PHOTOS\2026\Ambulance - Merc`.

This allows mirrors to live at different paths on different drives — volume A might have
`ScanSubPath=\_PHOTOS\2026` and volume B might have `ScanSubPath=\PHOTOS\2026`, but both
validate correctly against the same stored relative paths.

- `VolumeEntry.GetFullScanPath(driveRoot)` returns the absolute path: `driveRoot + ScanSubPath`
- `FileScanner.ToRelative(scanRoot, absolutePath)` produces paths relative to the scan root
- `ValidationEngine.GetTopLevelPaths(hashFile.Paths)` returns the minimal set of paths
  to pass to `FileScanner.Scan` so directories are not visited more than once

### Media Groups

A single `.hash` file can track **multiple physical copies** of the same data (e.g. local
drive, NAS, and a removable backup). All copies share the same `[FILES]`, `[PATHS]`, and
`[FILTERS]` because relative paths and hashes are identical across identical copies.

- `[VOLUMES]` lists every registered copy; primary volume is `Volumes[0]`
- `HashFileData.SerialNumber`, `.VolumeLabel`, `.MediaTotalBytes` are convenience properties
  reading from `Volumes[0]` — work as before for single-volume sets
- To add a mirror: `HashSetService.AddVolumeAsync(hashFilePath, serial, label, totalBytes, scanSubPath)`
- Validation records which volume was scanned via `ValidationEntry.VolumeSerial`
- The Dashboard shows per-row availability (online/offline) by checking all registered serials
- The MediaGroupPage manages the group (view volumes, register mirrors, edit scan paths);
  it does **not** run validation inline — the Validate button navigates to `ValidatePage`

### `.hash` file storage — centralised only

`.hash` files live **on the PC** (default: `%APPDATA%\HashCheck\`), never on the media
itself. This is required for the Media Groups model to work (one file accessible when any
of the registered volumes is connected).

### Volume identity
Always identify media by **volume serial number**, never by drive letter. Drive letters
change between sessions. Use `VolumeLocator.FindBySerial(serial)` or
`VolumeLocator.GetAllVolumes()` for detection.

`VolumeLocator.GetVolumeIdentity` returns a sensible fallback label for unlabeled drives:
- Fixed drives → "Local Disk"
- Removable drives → "Removable Drive"
- Network drives → "Network Drive"

### Validation — multi-volume concurrent

`ValidatePage` handles validation for both single-volume hash sets and media groups. It
creates one `ValidationRow` per online volume and runs them concurrently (or sequentially
based on the `RunValidationsConcurrently` setting in `AppSettings`).

- Each `ValidationRow` owns a `PauseToken` and `CancellationTokenSource`
- **Pause/Resume** suspends the validation loop between files using `SemaphoreSlim(1,1)`;
  the loop calls `await pauseToken.WaitIfPausedAsync(ct)` before each file
- **Cancel** cancels that row's `CancellationTokenSource` only — other rows continue
- **View Report** navigates to `ReportPage` with the completed `ValidationReport`
- `ValidateViewModel` receives a `hashFilePath`, finds all online volumes, builds rows,
  then calls `Task.WhenAll` (concurrent) or a sequential foreach depending on the setting
- `HashSetService.ValidateAsync` and `ValidationEngine.ValidateAsync` both accept an
  optional `PauseToken?` parameter (default null — existing callers unaffected)

### Autoscan — mount-triggered with delay prompt

When a tracked volume comes online (detected by `SchedulerService`'s 30-second volume
poll), and the hash set has `Autoscan=True`, the app shows a prompt:

> "Media Attached: [label] — when would you like to autoscan?"
> Scan now / In 5 min / In 30 min / In 1 hr / In 4 hr / In 8 hr / In 24 hr / I'll do it manually

This prompt can be disabled globally in Settings (`AutoscanPromptOnAttach`). The delay
countdown fires `HashSetService.AutoscanAsync` after the chosen interval, but only if the
volume is still connected at that time.

The scheduler's first volume tick (10 s after start) records the baseline — volumes already
mounted at startup do NOT fire the attach event.

### File writes
Write to a temp file then atomic-rename to final path. Never leave a partial `.hash` on
disk. This applies to Create, Autoscan, and Re-create.

### Re-create
Full re-baseline: re-hashes everything; resets `DateCreated` to today; backs up the old
`.hash` to `name.YYYYMMDD-HHmm.hash.bak`; starts a fresh `[VALIDATIONS]` log; preserves
the full `[VOLUMES]` group (all registered copies).

`ReCreateAsync` calls `CreateAsync` internally which writes a timestamped temp file.
`ReCreateAsync` immediately deletes that temp file and removes it from `KnownHashFiles`,
then writes the final result to the original `hashFilePath`. This prevents a duplicate
entry appearing in the Dashboard.

### Autoscan vs Re-create (keep distinct)
- **Autoscan:** add-only; never re-hashes existing files; does not reset `DateCreated`;
  preserves `[VALIDATIONS]` history; uses `AutoscanEngine`
- **Re-create:** full re-baseline — see above

### Report categories
When comparing hashes, use three Not-Matching sub-categories:
- **Corrupted** — hash differs, but size AND mtime unchanged (likely bit-rot)
- **Modified** — hash differs AND size or mtime changed (likely a legitimate edit)
- **Missing** — in the `.hash` file but absent from the media
Also report: **New** (on media, not in hash file), **Errors** (unreadable/locked).

### Long paths
Use `\\?\`-prefixed paths throughout to handle paths longer than 260 characters.

### Filtering
Per-set `FilterMode` is `Include` or `Exclude` (stored in `[META]`), with patterns in
`[FILTERS]`. Default exclude patterns cover `$RECYCLE.BIN\`, `System Volume
Information\`, `Thumbs.db`, `*.tmp`.

### Threading
All hashing, scanning, and validation runs on a background thread. UI (tray + windows)
must stay responsive during long operations. Progress shows file count, bytes, current
file, ETA, and Cancel per validation row.

### System tray
`TrayIconHost` uses P/Invoke `Shell_NotifyIconW` + `SetWindowSubclass` (comctl32).
Context menu items: **Dashboard**, **Create new hash…**, **Settings**, **About / Donate…**, **Exit**.
`TrackPopupMenu` is called with `TPM_RETURNCMD` — the selected command ID is returned
directly (not via `WM_COMMAND`) and handled by calling `HandleMenuCommand` on the return
value. Do not add `WM_COMMAND` handling for menu items.

### Window title / donation nag
On launch, `App.xaml.cs` calls `BuildWindowTitle()` which produces:
`HashCheck v1.0.0 — <nag message>` (unless `HideDonationNag` is true, in which case
just `HashCheck v1.0.0`). The nag cycles sequentially through seven messages stored in
`NagMessages[]` in `App.xaml.cs`, advancing `NagMessageIndex` each launch.

The **About / Donate…** tray item shows a dialog with the legal disclaimer and a
**Donate** button that opens `https://www.paypal.me/jasondsutton` in the browser and
sets `HideDonationNag = true` (persisted to `settings.json`), removing the nag from
the title for all future sessions.

Version is read from `Windows.ApplicationModel.Package.Current` (packaged) or
`Assembly.GetExecutingAssembly().GetName().Version` (unpackaged/debug).

### AppSettings fields
- `DefaultHashStoragePath` — where new `.hash` files are saved
- `DefaultReminderDays` — default validation reminder interval
- `DefaultAlgorithm` — default hash algorithm (XxHash3)
- `DefaultAutoscan` — default autoscan setting for new hash sets
- `AutoscanPromptOnAttach` — whether to show autoscan delay prompt on volume attach
- `RunAtLogin` — whether to register in HKCU Run key
- `RunValidationsConcurrently` — whether `ValidatePage` runs all volume rows in parallel
  (default `true`; users on single-drive systems should turn this off)
- `KnownHashLocations` / `KnownHashFiles` — paths the dashboard scans for `.hash` files
- `NagMessageIndex` — which donation nag message to show next (increments each launch, wraps)
- `HideDonationNag` — set to `true` when the user clicks Donate; suppresses the nag permanently

### Dashboard — Open Location
The "Open Location" button opens the **scan root** of the first online registered volume
in Explorer (e.g. `Z:\_PHOTOS\2026`). Falls back to the directory containing the `.hash`
file if no volume is currently online.

---

## Build phases (current status)

1. ✅ Core format + hashing — `.hash` model, reader/writer, `[INTEGRITY]`, hasher factory
2. ✅ Scanning + Create (headless) — directory walk, filters, volume identity, write `.hash`
3. ✅ Validation + report (headless) — comparison engine, `[VALIDATIONS]` append
4. ✅ WinUI shell + tray — tray icon (P/Invoke), single-instance mutex, settings, autostart
5. ✅ Create UI — folder tree, details, progress
6. ✅ Validate UI — multi-volume concurrent rows, Pause/Resume/Cancel per row, View Report
7. ✅ Reminders + Dashboard — scheduler, reminder popup, dashboard page
8. ✅ Autoscan — `AutoscanEngine`; mount-trigger + delay prompt wired in `App.xaml.cs`; scan-root-relative paths
9. ✅ Re-create — re-baseline action, backup, fresh log, no-duplicate fix
10. ✅ Add Mirror UI — `RegisterMirror` button on Dashboard, `EditMirrorRoot` on MediaGroupPage
11. ⬜ Packaging — finalize MSIX, verify autostart
    ✅ App icon — `HashIcon.ico` in project root; copied to output dir; loaded in tray
       (`LoadImage` + `LR_LOADFROMFILE`), window title bar, and taskbar (`AppWindow.SetIcon`)

---

## Things to flag before doing

- Any change to the `.hash` file format (section names, field names, delimiter, section order)
- Any change that would make existing `.hash` files unreadable (no backward compat)
- Adding a dependency that conflicts with `System.IO.Hashing` (ships with .NET 8)
- Storing paths as absolute rather than relative to the **scan root** (not volume root)
- Storing `.hash` files on the media itself (must stay centralised)
- Adding concurrent disk I/O for validation without respecting `RunValidationsConcurrently`

### App icon
`HashIcon.ico` lives in the project root and is copied to the output directory on every
build (`CopyToOutputDirectory: PreserveNewest`). It is loaded in three places:
- **Tray** — `TrayIconHost.LoadAppIcon()` uses `LoadImage` + `LR_LOADFROMFILE`; falls
  back to `LoadIcon(IDI_APPLICATION)` if the file is missing
- **Title bar / taskbar** — `MainWindow.SetupWindow()` calls `_appWindow.SetIcon(iconPath)`
- **MSIX assets** — still using default placeholder PNGs in `Assets\`; replace these with
  resized versions of the icon before publishing
