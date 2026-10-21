# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-21

First public release of **V-Clicker**, a complete rewrite of the original AutoClicker prototype
(kept in the first commit of the history).

### Added
- Click engine on its own thread: intervals from 1 ms to 24 h on an absolute schedule (no drift, no catch-up bursts), optional random variation.
- Left / middle / right button, single or double click, repeat forever or N times.
- Fixed click position with capture button and hotkey; clicks at the cursor otherwise.
- Hold mode (click only while the hotkey is held) besides the original toggle mode; configurable start delay.
- Configurable global hotkeys with conflict detection; non-fatal warning when another app owns a key.
- Dark, high-DPI aware interface with live click count, measured clicks/s and elapsed time; Portuguese and English.
- System tray, always-on-top, sound cues, single instance, restart-as-administrator link.
- Settings persisted to `%APPDATA%\V-Clicker\settings.ini`, with a portable mode.
- Official icon (`assets/`), MIT license, build script, CI and release workflows.
- Test suite: 36 unit tests plus 3 real-input integration tests.

### Changed
- Minimum interval lowered from 20 ms to 1 ms; the 1-second start delay is now optional (0–30 s).
- A refused click stops the run with an on-screen message instead of a modal dialog.
- Project moved to an SDK-style layout (`src/`, `tests/`) built with the .NET SDK.
