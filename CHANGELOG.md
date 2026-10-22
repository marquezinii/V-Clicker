# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- **New interface, "custom keyboard"**: every window redrawn as a custom mechanical keyboard on a graphite plate.
  Actions are keycaps (the amber Start key with its hotkey printed on it, a coral Stop key while clicking, the
  5/10/15/20/50 CPS number row), choices are clusters of keys where the chosen one is held down, on/off options are
  lock keys with an LED, typed values sit in recessed wells, and live numbers (requested CPS, clicks, CPS sent, time,
  messages) are on a black display. Groups are labelled like a printed plate instead of boxed in cards. Legends use
  Bahnschrift (ships with Windows); step and indicator-corner icons are drawn, not font glyphs. The main window is
  reorganized (display, Start, speed and hotkeys on the left; click, position, Window Lock and options on the right);
  the Macro Studio, its dialogs, the app picker, the tray menu and the on-screen indicator follow the same look.
  No function, setting, hotkey or file format changed.
- The footer credits the maker: "Made by Vemryx" (links to vemryx.com), next to GitHub and the version; the `.exe`
  carries Vemryx as its company.
- New official brand, butter-yellow paper collage: the graphite V on a cream card is the app icon (window, taskbar,
  tray, `.exe`, header), plus a new README logo, social preview and the brand kit in `assets/brand` (logos, emblem,
  monogram and brand art). The previous cyan/violet brand files were removed.

### Added
- **AI Macro Builder** (see `docs/superpowers/specs/2026-10-21-ai-macro-builder-design.md`): *Build with AI* in the
  Macro Studio. Choose the app, describe the task, and Claude (`claude-opus-5`, Messages API over HTTPS with a JSON
  Schema-constrained reply) drafts a macro that opens unsaved with a note; nothing runs. Proposed clicks are stored as
  `click <button> pending` and are never clicked until a point is captured (F7) or typed, or a smart target is set:
  Run refuses them, the player stops before them and the simulation shows them without a place. Only the description
  and the app's name are sent, only when *Generate* is pressed, with the user's own API key, kept DPAPI-protected in
  the settings (`AiKey=`; a clear-text key is ignored). Every proposed step is validated like a `.vcmacro` line.
- **Developer Mode** (see `docs/superpowers/specs/2026-10-21-developer-mode-design.md` and `docs/developer.md`):
  - Command line: `--macro <file|name>` runs a macro through the Macro Studio's Run, `--profile <exe|name>` starts
    an app's profile through Start, with `--var name=value`, `--log file.jsonl` and `--exit-when-done` (exit codes:
    0 done, 1 stopped, 2 bad arguments, 3 already open, 4 did not start, 10+ other stop). Same window and safeguards
    as the buttons (F8, Window Lock, SafeClick review, duration limit); with V-Clicker already open it does nothing.
  - `{{name}}` variables in typed text, from `--var`, `var.name=` header lines or a prompt in the Studio, plus
    `{{iteration}}`, `{{date}}` and `{{time}}`.
  - Structured logs (JSON Lines: `run.start`, `step`, `run.stop`) to `Logs\v-clicker-YYYY-MM-DD.jsonl` when the new
    *Developer mode* option is on, or to `--log`; typed text and variable values are never logged.
  - A *Copy command* button in the Macro Studio (Developer mode) and a documented `.vcmacro` / `.vcprofile` format.
- **Simple and advanced interface** (see `docs/superpowers/specs/2026-10-21-adaptive-interface-design.md`): a
  Simple | Advanced switch at the top of the main window. Simple is one narrow column (display, Start, speed in CPS
  with the presets, mouse button, hotkeys); Advanced is the full window. New installs start simple; existing settings
  files without the new `Advanced=` key stay advanced. Hidden settings keep working, and the simple window lists the
  ones that are not at their defaults ("Also in effect: …"). Switching never starts or stops a run.
- **Profiles per app** (see `docs/superpowers/specs/2026-10-21-profiles-design.md`): with the Window Lock on, the
  click settings (speed, jitter, button, type, repeats, position, start delay, hotkey mode, focus-loss mode) are kept
  per app executable in `Profiles\<exe>.vcprofile` and loaded when the app is chosen again, or when the lock is turned
  on, never during a run. Hotkeys, window options, sounds and language stay global. A *Profiles* window (footer link)
  lists them and exports one, with the app's saved macros, to a single `.vcprofile` file, or imports one (values are
  validated like the settings; unreadable macros or macros of another app are left out; an identical macro is not
  duplicated; imported macros go through the SafeClick review on first use).
- **SafeClick** (see `docs/superpowers/specs/2026-10-21-safeclick-design.md`):
  - *Simulate* in the Macro Studio: a still of the app's window (captured without focusing it) with each step's
    place marked, one step at a time, using the same position, target and condition code as a run. Nothing is
    sent to the app. Results are stated as "in the capture"; a capture that fails its checks (minimized, refused,
    or one flat colour) makes the steps that depend on it inconclusive, never confirmed.
  - A safety review before the first run of a macro with Delete, Shift+Delete, Alt+F4, Ctrl+W or Ctrl+Shift+Esc.
    The approval covers that content in that local file (renaming keeps it; any change, a copy or an imported file
    asks again) and is kept in the settings file, never in the shareable macro.
  - An optional duration limit per macro (`limit=` in the file, minutes): the run stops between inputs once it is
    reached, pauses included, never in the middle of a drag.
- **Smart Target** (macro clicks that find what to click): a recorded click can carry a target, recognized at F7 by
  name (UI Automation control type and name, plus the automation id only when needed to tell two apart) or by
  appearance (a 32/64/96 px region: the smallest one that is unique and clearly distinct on the screen). Before every
  click the target is searched in the step's window; the click happens only after two finds at the same place and a
  final check that the target itself is under the point. Ambiguous, disabled, off-screen, covered or not found within
  the time limit (10 s by default, 1–3600 s): stopped without clicking, with the reason. The recorded point is never
  used as a fallback; "back to the fixed point" restores the click exactly. UI Automation and screen reads run off
  the UI and player threads, one at a time; F8 never waits for them. The on-screen indicator is left out of screenshots.
- **Conditional Click** now waits for an element wherever it is in the window (same search as Smart Target), and only
  when it is unique there.
- **Conditional Click** ("wait until…" macro steps): wait until a UI element is available (UI Automation; the
  interactive control is found from its text), until a point shows a colour, or until a dialog opens, with a time limit
  (30 s by default, 1–3600 s). Moves on after two consecutive positive reads; stops before the next step on timeout,
  with a message naming the step. Another app in front pauses the watch without spending the limit; failed, slow or
  ambiguous reads never count as met, and colours are never read through a covering window. Created by converting a
  recorded wait (reversible, exactly) or inserted from scratch.
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
