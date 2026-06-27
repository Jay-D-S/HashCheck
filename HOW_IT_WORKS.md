# How HashCheck Works

Technical overview of the major data flows. Read alongside `CLAUDE.md` (conventions)
and the `_PLANS/` documents (original specification).

---

## App startup

```
App.OnLaunched
  ├─ Named mutex check → exit if already running (single-instance)
  ├─ AppServices.Initialize()
  │    ├─ SettingsStore.Load() → reads %APPDATA%\HashCheck\settings.json
  │    ├─ HashSetService(settingsStore)
  │    └─ SchedulerService(hashSets)
  ├─ MainWindow.Activate() → shows window, navigates to DashboardPage
  ├─ TrayIconHost(hwnd) → Shell_NotifyIcon, SetWindowSubclass for WM_USER messages
  │    └─ context menu: Dashboard / Create / Settings / About+Donate / Exit
  ├─ SchedulerService.Start()
  │    ├─ _reminderTimer  → first tick at 5 s, then every 24 h
  │    └─ _volumeTimer    → first tick at 10 s (baseline only), then every 30 s
  └─ BuildWindowTitle() → "HashCheck v1.x.x — <nag>" or just version if donated
```

The 5-second reminder delay avoids a race where `XamlRoot` is null on the first frame —
the dialog would be silently dropped without it.

The 10-second volume baseline tick records which drives are already mounted at startup
so that they don't fire spurious "volume attached" autoscan prompts.

---

## Hash set creation

The user picks a folder, fills in details (description, algorithm, reminder interval,
filters, autoscan flag) on `CreateHashPage`, and clicks Create.

```
CreateHashPage → CreateViewModel.CreateAsync()
  └─ HashSetService.CreateAsync(CreateOptions)
       ├─ FilterEngine(filterMode, patterns)
       ├─ FileScanner.Scan(mediaRoot, scopePaths)   → list of ScanItems (relative paths)
       ├─ FileScanner.GetAllDirectories(...)         → for [PATHS] section
       ├─ for each file: HasherFactory.Create(alg).ComputeHashAsync(stream)
       ├─ Builds HashFileData { Volumes, Paths, Files, … }
       ├─ HashFileWriter.WriteAsync(data, tempPath)  → atomic: write temp, rename to final
       └─ SettingsStore.AddKnownHashFile(finalPath)
```

**Path representation:** Every path in `[PATHS]` and `[FILES]` is relative to the
scan root (e.g. `\Ambulance - Merc\IMG_001.jpg`), never to the drive root. This is
what makes mirror validation work — two drives can have the same files at different
absolute locations but identical relative paths.

**Integrity seal:** After all content is written, `HashFileWriter` computes SHA-256
over every byte up to (but not including) the blank line before `[INTEGRITY]` and
appends `SHA-256:<hex>`. This detects accidental corruption of the `.hash` file itself.

---

## Validation

```
DashboardPage → Validate button → Frame.Navigate(ValidatePage, hashFilePath)
  or
App.OnRemindersAvailable → ValidateNow → Frame.Navigate(ValidatePage, ValidateRequest)
  or
MediaGroupPage → Validate button → Frame.Navigate(ValidatePage, ValidateRequest(path, serial))
```

```
ValidatePage.OnNavigatedTo(parameter)
  └─ ValidateViewModel.StartWithFileAsync(hashFilePath, restrictToSerial?)
       ├─ HashFileReader.ReadAsync(path, verifyIntegrity: false)
       ├─ VolumeLocator.GetAllVolumes() → find which registered volumes are online
       ├─ Filter to restrictToSerial if set (single-volume validate from MediaGroupPage)
       ├─ Build one ValidationRow per online volume (serial, label, scanRoot)
       ├─ AppServices.ActiveValidation = this   ← reattach support
       └─ RunValidationsAsync()
            ├─ concurrent: Task.WhenAll(rows.Select(ValidateRowAsync))
            │   or sequential: foreach row await ValidateRowAsync(row)
            └─ AppServices.ActiveValidation = null  ← clear when all done
```

Per-row validation:
```
ValidateRowAsync(row)
  └─ HashSetService.ValidateAsync(hashFilePath, row.ScanRoot, row.SerialNumber, …)
       ├─ HashFileReader.ReadAsync(path, verifyIntegrity: true)   ← integrity check
       ├─ ValidationEngine.ValidateAsync(hashFile, mediaRoot, …)
       │    ├─ GetTopLevelPaths(hashFile.Paths) → minimal scope (usually just "\")
       │    ├─ FileScanner.Scan(mediaRoot, scopePaths) → all files on disk
       │    ├─ For each file on disk:
       │    │    ├─ Not in hash set → New
       │    │    ├─ Hash matches → Matching
       │    │    └─ Hash differs:
       │    │         ├─ size or mtime changed → Modified (likely legitimate edit)
       │    │         └─ size and mtime unchanged → Corrupted (likely bit-rot)
       │    └─ Files in hash set not found on disk → Missing
       ├─ report.VolumeSerial = serial; report.ScanRoot = mediaRoot
       ├─ if hashFile.Autoscan → AutoscanEngine.ScanAsync() → hash new files (outside lock)
       ├─ await _writeGates[hashFilePath].WaitAsync()   ← per-file semaphore
       │    ├─ HashFileReader.ReadAsync(path, verifyIntegrity: false)  ← fresh re-read
       │    ├─ latest.Validations.Add(new ValidationEntry(…))
       │    ├─ merge autoscan results (dedup against re-read file list)
       │    └─ HashFileWriter.WriteAsync(latest, path)
       └─ gate.Release()
```

The write gate (`_writeGates` — a static `ConcurrentDictionary<string, SemaphoreSlim>` in
`HashSetService`) prevents two concurrent volume validations from racing to write the same
`.tmp` file and from overwriting each other's `[VALIDATIONS]` entry. Hashing (the slow
part) still runs in parallel; only the final read-modify-write is serialised.

The validation result (`ValidationReport`) flows back to `ValidationRow.Report`.
"View Report" navigates to `ReportPage` which builds HTML or CSV on demand.

---

## Reattachable validation

While validation runs, `AppServices.ActiveValidation` holds the live `ValidateViewModel`.
The user can navigate to Dashboard or elsewhere; progress continues on the background thread.

When the user navigates back to `ValidatePage`:
- The constructor checks `AppServices.ActiveValidation` and reuses it instead of creating
  a new VM — `OnNavigatedTo` returns immediately without restarting.
- `DashboardPage.OnNavigatedTo` shows an InfoBar banner with a "Return to validation"
  button whenever `ActiveValidation != null`.
- `Progress<ScanProgress>` captures the `SynchronizationContext` at construction (tied to
  the window dispatcher, not the page), so progress callbacks reach the UI even after
  the original page is no longer in the frame.

---

## Post-validation autoscan (add new files)

If `hashFile.Autoscan == true`, `HashSetService.ValidateAsync` runs `AutoscanEngine` after
the main validation pass:

```
AutoscanEngine.ScanAsync(hashFile, mediaRoot)
  ├─ Build knownPaths set from hashFile.Files
  ├─ Scan mediaRoot (same scope as validation)
  ├─ newItems = scanned files not in knownPaths
  ├─ Hash each new file
  └─ Return AutoscanResult { AddedFiles, AddedPaths }
```

`HashSetService` then merges the result into `hashFile` and writes it back. The net effect
is that new files appear in the hash set after the next validation completes, without the
user having to run Re-create.

Toggle per hash set via the **Autoscan new files** checkbox on `MediaGroupPage`
(saves via `HashSetService.SetAutoscanAsync`). The app-level `DefaultAutoscan` in Settings
only affects hash sets created in the future.

---

## Mount-triggered autoscan

Separate from post-validation autoscan. `SchedulerService` polls volumes every 30 s.
When a serial appears that wasn't in the baseline, it fires `VolumeAttached`. `App.xaml.cs`
handles this:

1. Finds hash sets that have `Autoscan=True` and include that serial.
2. Shows the delay-choice dialog (Scan now / In 5 min / … / I'll do it manually).
3. After the chosen delay, confirms volume still connected, then calls
   `HashSetService.AutoscanAsync` (add-only, no re-hash of existing files).

This is controlled by `AppSettings.AutoscanPromptOnAttach`. If turned off, no prompt
appears and no autoscan runs on attach.

---

## Re-create

Full re-baseline of an existing hash set. Used when the media contents have been
intentionally updated and the old hashes are no longer meaningful.

```
ReCreatePage → ReCreateViewModel → HashSetService.ReCreateAsync(hashFilePath, mediaRoot)
  ├─ Read existing file (verifyIntegrity: false — may be outdated)
  ├─ Back up to name.YYYYMMDD-HHmm.hash.bak
  ├─ CreateAsync(options derived from existing file) → writes a timestamped temp file
  ├─ Delete the temp file + remove from KnownHashFiles (prevent Dashboard duplicate)
  ├─ Restore Volumes list from the original (all mirrors preserved)
  └─ HashFileWriter.WriteAsync(newData, original hashFilePath)
```

Re-create resets `DateCreated` to now and starts a fresh `[VALIDATIONS]` section.
It preserves all registered mirror volumes in `[VOLUMES]`.

---

## Reminders

`SchedulerService` ticks every 24 h. `ReminderScheduler.GetOverdueItems` compares each
hash set's `DueDate` (`LastValidated + ReminderDays`, or `DateCreated + ReminderDays` if
never validated) against `DateTime.UtcNow`.

If any are overdue, `RemindersAvailable` fires. `App.xaml.cs` shows a `ContentDialog`:

| Overdue count | Dialog content |
|---|---|
| 1 | Names the set; Validate Now opens ValidatePage pre-loaded for that set |
| 2–20 | Bullet list of all names; Validate Now goes to Dashboard |
| > 20 | "N hash sets overdue — use the Dashboard" |

Snooze is shown as a button but not yet implemented (closes the dialog).

---

## Media Groups

A `.hash` file may have multiple entries in `[VOLUMES]`. Each represents a physical copy
of the same data (primary drive, NAS, USB backup, etc.).

- **Add mirror:** Dashboard → Register Mirror → pick a folder → confirm. `HashSetService.AddVolumeAsync`
  appends a new `VolumeEntry` with the computed `ScanSubPath`.
- **Validate one copy:** MediaGroupPage → select a volume row → Validate. Passes
  `ValidateRequest(hashFilePath, selectedSerial)` so only that volume is validated.
- **Validate all copies:** Dashboard → Validate (no serial restriction). Builds one
  `ValidationRow` per currently-online volume and runs them all.
- **Edit scan path:** MediaGroupPage → Edit Mirror Root → type corrected path. Saves via
  `HashSetService.UpdateVolumeScanPathAsync`.
- **Repair:** MediaGroupPage → Repair (enabled when ≥ 2 volumes registered and ≥ 1 online).
  See Cross-Drive Repair below.

All copies share `[FILES]`, `[PATHS]`, `[FILTERS]` — the relative paths are identical
on every copy by definition. `[VALIDATIONS]` records carry `volume=<serial>` so per-copy
history is distinguishable.

---

## Cross-Drive Repair

Triggered from `MediaGroupPage → Repair`. Navigates to `RepairPage` with the hash file
path as parameter. `RepairViewModel.RunAsync` drives a two-phase flow:

**Phase 1 — validate all online volumes**

Reuses `ValidationRow` (same card UI as `ValidatePage`) and calls
`HashSetService.ValidateAsync` for each online volume — meaning `[VALIDATIONS]` entries
are written to the hash file just as with a normal validation. Runs concurrently or
sequentially according to `RunValidationsConcurrently`.

**Phase 2 — repair**

```
RepairEngine.RunAsync(reports, onlineVolumes)
  ├─ Collect all RelativePaths that are Corrupted on ≥ 1 volume
  ├─ For each corrupted path:
  │    ├─ goodSerials   = volumes where file was validated + not corrupted/missing/error
  │    ├─ corruptedSerials = volumes where file is Corrupted
  │    ├─ goodSerials empty → Unrecoverable (skip)
  │    └─ For each corruptedSerial:
  │         ├─ DriveType == CDRom → ReadOnlySkipped
  │         ├─ File.Copy(sourcePath, targetPath + ".repair_tmp", overwrite: true)
  │         ├─ Re-hash .repair_tmp with HasherFactory.Create(hashFile.Algorithm)
  │         ├─ Hash matches stored FileEntry.Hash
  │         │    → File.Move(.repair_tmp → targetPath, overwrite: true)  → Repaired
  │         ├─ Hash mismatch → delete .repair_tmp → VerificationFailed
  │         ├─ UnauthorizedAccessException / ERROR_WRITE_PROTECT → ReadOnlySkipped
  │         └─ Any other exception → delete .repair_tmp → Error
  └─ On cancellation → delete in-progress .repair_tmp, propagate OperationCanceledException
```

Only `Corrupted` files are repaired (`NotMatchingReason.Corrupted` — hash differs but
size and mtime are unchanged). `Modified` files are left untouched.

`RepairPage` shows summary cards (Repaired / Unrecoverable / Read-only skipped / Failed)
and colour-coded detail lists. The "Validate Again" button navigates to `ValidatePage`
for a confirmation run after repair completes.

---

## Dashboard

Loads all known hash files from `KnownHashFiles` (tracked list) and from any configured
scan folders. Each row is a `DashboardItem` wrapping a `HashFileData`.

**Sorting:** Column header buttons call `DashboardViewModel.SortBy(column)`. The VM keeps
a `_allItems` backing list and repopulates `Items` (the bound `ObservableCollection`) in
the new order. Clicking the same column header reverses direction; the active column shows
▲ or ▼.

**Availability dot:** Green if any registered volume is currently online; red if none.
Multi-volume sets show "N/M online".

**Open Location:** Opens the scan root of the first online volume in Explorer. Falls back
to the folder containing the `.hash` file if all volumes are offline.

---

## `.hash` file integrity

`[INTEGRITY]` contains a SHA-256 hash of every byte in the file up to and including the
newline immediately before the blank line that precedes `[INTEGRITY]`. This is computed by
`HashFileWriter` on every write and verified by `HashFileReader` when `verifyIntegrity: true`
is passed.

`LoadAllKnownAsync` and `AutoscanAsync` use `verifyIntegrity: false` (performance).
`ValidateAsync` uses `verifyIntegrity: true` — if the `.hash` file itself is corrupt the
validation fails with an exception rather than silently comparing against bad data.

---

## Tools

`Tools/Corrupt-Byte.ps1` — developer utility for testing bit-rot detection. XORs one byte
(default: middle of file) with `0xFF` then restores all timestamps and file attributes so
the corruption is invisible to everything except a hash check.

```powershell
.\Tools\Corrupt-Byte.ps1 "C:\path\to\file.jpg"          # corrupt middle byte
.\Tools\Corrupt-Byte.ps1 "C:\path\to\file.jpg" -Offset 0 # corrupt first byte
```
