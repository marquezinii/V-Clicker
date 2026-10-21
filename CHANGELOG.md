# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Macro Studio**: record a task once in one app window, see it as plain-language steps (click, double click, drag,
  hover, scroll, type, key, shortcut, wait), edit them with undo/redo, save them as `.vcmacro` files and repeat them
  N times or until stopped. Macros are bound to the recorded window (plus its dialogs and menus): another window in
  front pauses the run, a closed window stops it, F8 stops at once and releases every key and button. Waits are
  recorded faithfully; long ones can be shortened only after you confirm, and restored. Password fields are never
  recorded and stop the run. Typed text keeps Shift, AltGr and accents (ABNT2). Clicks are relative to the window,
  so moving it does not break the macro. Works with the on-screen indicator off (status line, beeps, tray).
- **Window Lock**: restrict clicks to one app. Pauses on Alt+Tab and resumes when the app is back (or stops, if set);
  F6 only starts with the app in front, the Start button brings it to the front. Stops for good when the window
  closes, after 5 minutes paused, or when the PC is locked or suspended. The chosen app (executable) is remembered.
- **On-screen indicator** (optional, any screen corner): clicking / paused / stopped over the game, click-through and never focused.
- Tray icon changes color with the state.
- Speed in **clicks per second** (default) or as an interval; presets 5 / 10 / 15 / 20 / 50 CPS.
- Live panel compares **requested** CPS with CPS **sent to Windows**.

### Fixed (audit of Window Lock and the click engine)
- After a Window Lock pause, clicking resumes only once the app has been back in front for 0.2 s (no Alt+click right after Alt+Tab), and never sooner than the interval after the previous click.
- Strict mode ("stop on focus loss") ignores focus blips shorter than 0.1 s instead of ending the run.
- The "sent" CPS is measured from the actual click times: steady and exact from 0.1 to 1000 CPS (it used to read 0 or spike at slow rates).
- The CPS field never applies a stale value after showing "< 0.1"; slow speeds no longer read as "0 CPS".
- A game's own dialog in front counts as the game; elevated games are refused with a "Restart as admin" hint instead of silently ignoring clicks; Store hosts and non-.exe processes are not listed; the picker shows the process id and works when started from the tray.
- A late "stopped" message can no longer overwrite a run that already started again; hotkeys cannot start a run under the app picker.
- A fault on the click thread stops the run instead of closing the app; a failed cursor move counts as a refused click; an invalid interval is never treated as "as fast as possible".
- Long app names are shortened in the badge and the indicator; the indicator stays on the screen where the clicks land.

### Changed
- The interval is stored with fractional milliseconds (15 CPS is exactly 66.667 ms). Older settings files load unchanged.
- The elapsed time shown is the session time; the clicking time excludes pauses.

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
