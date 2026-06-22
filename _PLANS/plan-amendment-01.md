# Hash Check — Plan Amendment 01

Supplements `plan.md`. Read alongside it; it does not replace anything, only refines §7
(functional requirements) and adds one action. Where this conflicts with `plan.md`, this
amendment wins.

---

## A. Lifecycle (confirmation)

This matches `plan.md` §7 and is restated here only as the anchor for the changes below:

1. The user **Creates** a hash file for a media.
2. The user can **Validate** the media against its hash file at any time, manually.
3. Otherwise the app waits until **today ≥ (hash file date + ReminderDays)** and then informs
   the user that a **validation is due** (`ReminderDays` comes from settings at creation time
   and is stored in `[META].ReminderDays`; "hash file date" is the most recent validation
   date, or `DateCreated` if never validated — see `plan.md` §7.3).
4. Running a validation always **produces a report**.
   - If everything matches: success state, no action needed.
   - If anything does not match: the user can act on the report (below).

---

## B. Report — add Print

In addition to **Export** (PDF / CSV / HTML, already in `plan.md` §7.5), the report window
must offer **Print** (standard Windows print dialog, rendering the same summary + detail
lists). Print and Export are available for any report, pass or fail.

---

## C. New action — Re-create

A **Re-create** action that re-baselines an existing hash set. It is distinct from Autoscan.

**Where it appears:** on the validation **report window** (most relevant after a mismatch)
and on the **Dashboard** for any tracked set.

**What it does:**
- Reuses the **previously used settings** taken straight from the existing `.hash`:
  scope (`[PATHS]`), `FilterMode` + `[FILTERS]`, `Algorithm`, `ReminderDays`, the storage
  location, the `Description`, and the media identity (`VolumeLabel` / `SerialNumber`).
- **Refreshes the hashes and the file list:** re-walks the scope and re-hashes every file
  from scratch, picking up changed, added, and removed files (the freshly scanned state
  becomes the new trusted baseline).
- **Stamps the date to today:** `DateCreated` and `DateModified` are set to now, which
  **resets the reminder clock** — the next "validation due" is now `today + ReminderDays`.
- Requires the media to be present; if absent, use the same **Insert media** prompt /
  serial-matching flow as validation (`plan.md` §7.4).
- Writes via temp-file + atomic rename (`plan.md` §8), so a cancelled or failed Re-create
  never leaves a partial `.hash`.

**Re-create vs Autoscan (keep these clearly separate):**
- **Autoscan** (`plan.md` §7.6): keeps the existing baseline, only *adds* newly found files,
  does **not** re-hash existing files, does **not** move the reminder date. Used to extend
  coverage.
- **Re-create:** throws away the old baseline and builds a fresh one (re-hashing everything)
  with today's date. Used when the current state of the media is now the reference of truth —
  e.g. after reviewing a mismatch report and confirming the changes are intended.

---

## D. Acceptance criteria (additions to `plan.md` §11)

- The report window prints via the Windows print dialog and exports, on both pass and fail.
- Re-create on an existing set, with the media present, produces a new `.hash` that: uses the
  same description, scope, filters, algorithm and reminder interval; reflects the media's
  current files (added/removed/changed all captured); and carries today's `DateCreated`.
- After Re-create, the set's next-due date is `today + ReminderDays` and the Dashboard status
  returns to OK.
- Re-create with the media absent shows the Insert-media prompt and does not modify the
  existing `.hash` until the correct volume is mounted.

---

## E. Interpretation notes (flag if you disagree)

- **Backup before overwrite:** Re-create renames the prior `.hash` to a timestamped backup
  (e.g. `name.20260530-1422.hash.bak`) before writing the new one, so an accidental Re-create
  is recoverable.
- **Validation history:** because Re-create starts a new baseline, the new `.hash` begins a
  fresh `[VALIDATIONS]` log; the prior history stays with the backed-up file. (Alternative:
  carry the old log forward with a `RECREATED` marker line — switch if preferred.)
