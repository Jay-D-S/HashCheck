Just to carify - I've been a software developer for over 15 years.  This project was written with help from AI (Claude)

I built a free portable tool that detects bit-rot by distinguishing corrupted files from legitimately modified ones — HashCheck v1.0.0
A few years back I watched a photographer lose archived shoots to bit-rot on drives that showed no errors whatsoever. As a keen photographer, It stuck with me.
Most checksum tools tell you "this file doesn't match" — but they don't tell you *why*. There's a meaningful difference between:
- A file whose hash changed but size and modified date are identical
→ that's bit-rot. The drive silently corrupted it.
- A file whose hash changed AND size or date changed
→ that's probably a legitimate edit.
Lumping those together means either ignoring real corruption or chasing false alarms on files you actually edited. HashCheck separates them into distinct categories: Corrupted, Modified, Missing, and New.
What it does:
- Creates a hash baseline for a drive or folder
- Validates against it on demand or on a reminder schedule
- Reports exactly what changed and why
- Supports multiple physical copies of the same archive (local drive, NAS, USB backup) — validates all copies in one pass
- Can repair a corrupted file from a known-good registered copy
- Autoscan adds new files to the baseline without re-hashing everything
- Runs in the system tray, out of your way until needed
- Default algorithm is XxHash3 (fast — this is integrity detection, not cryptography)
- `.hash` files are plain UTF-8 text — human readable, stable across versions, easy to inspect
Practical details:
- Portable — unzip and run, nothing to install
- Windows 10/11 x64
- Free, open source (MIT)
- Donate link in the About screen if it's useful to you
Download + source:
https://github.com/Jay-d-s/HashCheck/releases/tag/v1.0.0
---
Built this in my spare time. Feedback, bug reports, and feature requests very welcome — that's what the Issues tab is for.
Happy to answer questions here too.

