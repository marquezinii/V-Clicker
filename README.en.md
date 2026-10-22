<div align="center">

<img src="assets/brand/logo-horizontal.png" width="440" alt="V-Clicker">

# V-Clicker

**A fast, precise, lightweight autoclicker for Windows.**<br>
A single `.exe`: no installer, no telemetry, no network.

[![CI](https://github.com/marquezinii/V-Clicker/actions/workflows/ci.yml/badge.svg)](https://github.com/marquezinii/V-Clicker/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)

[🇧🇷 Português](README.md) · 🇺🇸 English

<img src="docs/screenshot-en.png" width="720" alt="V-Clicker window">

</div>

## Features

- **Simple or advanced**: the switch at the top toggles between a lean window (speed, mouse button and hotkeys, for anyone who just wants to press F6) and the full one. New installs start simple; whatever was set up in Advanced stays in effect, and the simple window says so in one line ("Also in effect: Window Lock (Roblox) · fixed spot…").
- **Real speed** – intervals from **1 ms** up to 24 h (up to ~1000 clicks/s, depending on the target program), using an absolute schedule that never accumulates lag plus a high-resolution wait.
- **Speed in CPS** (clicks per second, one decimal) or as an **interval in h / min / s / ms**: both edit the same speed, with no rounding (15 CPS = 66.667 ms). Presets `5`, `10`, `15`, `20` and `50 CPS`.
- **Window Lock**: pick an app (e.g. Roblox) and V-Clicker only clicks while it is in front. On Alt+Tab it **pauses and resumes by itself** when you come back (or stops, if you prefer). F6 only starts with the app in front; the *Start* button brings it to the front. It stops for good if the window closes, if a pause lasts over 5 minutes, or if the PC is locked or goes to sleep.
- **Profiles per app**: with the Window Lock on, V-Clicker remembers each app's click settings (speed, button, type, repeats, position, delay, mode and what to do on Alt+Tab) and loads them when you choose the app again, with nothing to create or name. Hotkeys and window options apply to every app. Under *Profiles* (footer), export an app's profile, with its macros, to a `.vcprofile` file and import it on another PC.
- **Macro Studio** (its own window, button at the top): click *Record*, choose the app and do the task once. Every click, key, text and wait becomes a plain-language step (`Click (left) at 812, 440`, `Wait 8 s`, `Type "Report"`) that you can edit, save and repeat N times or until you stop it. A macro only acts on the window it was recorded in (and its dialogs): if another window comes to the front, it pauses; F8 stops everything at once. Long waits can be shortened, always with your confirmation and with the recorded time kept. Password fields are not recorded, and running stops at that point. In browsers, switching tabs inside the same window is not detected.
- **Wait until… (Conditional Click)**, in Macro Studio: turn a recorded wait (or insert one from scratch) into "wait until the *Download PDF* button is available", "until this point's colour shows up" or "until the *Save as* window opens". The macro moves on as soon as the condition is confirmed (two reads in a row) and stops before the next step if the time limit runs out. Time with another window in front does not count; a doubtful read never lets the macro through. You can go back to the original wait. An element is waited for wherever it is in the window; a colour is watched at a fixed point.
- **Smart target**, in Macro Studio: a recorded click looks for *what* to click instead of *where*. Point at the button and press F7; the V-Clicker recognizes it **by name** (Windows accessibility; works in Chrome/Edge and most programs) or **by appearance** (a small region of the screen, for games and programs without names) and shows a preview. Before every click, the macro looks for the target and only clicks when it finds **exactly one**, still, enabled and itself under the click point. Two candidates, a disabled or covered target, or nothing found within the time limit (10 s by default): the macro stops without clicking and says why. The recorded point is never a fallback, but you can go back to it.
- **SafeClick (simulation and review)**, in Macro Studio: *Simulate* captures the app's window (without bringing it to the front) and shows, step by step, where each click would land, the text that would be typed and the shortcuts, with each target and condition checked **in the capture**. Nothing is clicked, typed or sent to the app; a capture that cannot be trusted (hardware-accelerated games often come out blank) shows as inconclusive, never as a confirmation. Before the first run of a macro with potentially impactful actions (Delete, Shift+Delete, Alt+F4, Ctrl+W, Ctrl+Shift+Esc), a safety review appears; once approved, that version of the macro does not ask again (any change, or an imported copy, asks again). Each macro can also have an optional **duration limit** that ends the run, pauses included.
- **Build with AI (optional)**, in Macro Studio: describe the task ("click this button every two seconds, but only while this window is open") and the AI (Anthropic's Claude) drafts an editable macro proposal. Nothing runs: clicks come without a point and you capture each one (F7) before running. It uses your own Anthropic API key (kept encrypted for your Windows user); only the description and the app's name leave the computer, and only when you press *Generate*.
- **Developer mode**: run a macro or a profile from the command line (`V-Clicker.exe --macro Report --var month=10 --exit-when-done`) with an exit code for scripts and tests; `{{name}}` variables in typed text; structured logs (JSON Lines) of every run that never record what was typed. Macro and profile formats are documented in [`docs/developer.md`](docs/developer.md) (Portuguese).
- **Random variation** (±0–50 %) for less mechanical clicking.
- **Left, middle or right button**, **single or double** click.
- **Repeat forever or N times** (up to 1 billion); it stops by itself when done.
- **Position**: at the cursor, or at a **fixed spot (X, Y)** that you capture with a button (3 s countdown) or a hotkey.
- **Toggle or Hold mode**: the hotkey switches on/off, or clicks **only while you hold it**.
- **Configurable global hotkeys** (default: `F6` start/stop, `F8` stop, `F7` capture position) that work while another program has focus or the window is minimized.
- **Start delay** (0–30 s) so you can position the mouse.
- **Live panel**: click count, **requested** CPS vs. CPS **sent to Windows** (the game may register fewer; V-Clicker does not pretend to measure that) and elapsed time.
- **On-screen indicator** (optional): a small pill in a screen corner shows *clicking*, *paused* or *stopped* over the game, never taking clicks or focus. The tray icon also changes color with the state.
- **System tray**, **always on top**, **sound cues**, **single instance**, **English/Portuguese** and a **dark theme** (dark title bar on Windows 10/11).
- **High-DPI aware** (4K screens, 125 % / 150 % scaling…).
- **Settings are saved automatically**, plus a **portable mode** (see below).
- Safety stop: if Windows refuses the click, V-Clicker stops and tells you instead of retrying forever.

## Download and use

1. Download the latest `V-Clicker.exe` from **[Releases](https://github.com/marquezinii/V-Clicker/releases)** (one file, nothing to install).
2. Double-click it. Set the interval, pick the button and the position.
3. Press **F6** (or the *Start* button) to begin and **F8** to stop.

Requirements: Windows 10 (1903 or newer) or Windows 11 — .NET Framework 4.8 ships with the OS, so there is nothing else to install.

| Default hotkey | Action |
| --- | --- |
| `F6` | Start / stop (or, in *Hold* mode, click while held down) |
| `F8` | Stop immediately, including during the start delay |
| `F7` | Capture the current cursor position as the fixed spot |

To change a hotkey, click its box and press the new combination (`Esc` cancels). `F1`–`F24`, navigation keys, the numeric keypad, or any key combined with `Ctrl`/`Alt` are accepted; bare letters are refused so they cannot break normal typing.

### FAQ

**Clicks do nothing in one particular program.** Programs running as administrator ignore input from normal programs (Windows privilege isolation). Use the **Restart as admin** link in the footer. Games with anti-cheat may also block simulated input — and many games and online services forbid autoclickers. Use it responsibly and follow each service's rules.

**My antivirus or SmartScreen complained.** Autoclickers simulate mouse input, which some antivirus products distrust, and the `.exe` is not code-signed. All source code is here and you can compile it yourself (see below). Every release lists the file's SHA-256.

**Where are the settings stored?** In `%APPDATA%\V-Clicker\settings.ini` (plain text, editable while the app is closed). For **portable mode**, create an empty `V-Clicker.ini` next to the `.exe` and it will use that file instead.

**F6/F7/F8 are already used by another program.** V-Clicker tells you in the window and you pick another key under *Hotkeys*.

**How accurate is the interval?** V-Clicker uses a high-resolution timer and busy-waits for the last couple of milliseconds, so the average rate matches the setting; each individual click still depends on Windows scheduling and on how quickly the target program handles input.

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (any current version; the app itself targets .NET Framework 4.8).

```powershell
.\build.ps1                  # build -> src\bin\Release\net48\V-Clicker.exe
.\build.ps1 -Test            # build and run the unit tests
.\build.ps1 -Test -Package   # also writes dist\V-Clicker.exe, the .zip and SHA-256 files
.\build.ps1 -Integration     # + tests with real clicks and hotkeys (moves the cursor for a few seconds)
```

### Layout

```
src/             the app (WinForms, .NET Framework 4.8, no third-party dependencies)
  ClickEngine.cs   click loop on a dedicated thread (timing, jitter, hold mode, Window Lock pause)
  WindowLock.cs    Windows windows for Window Lock (list, focus, window area);  WindowPicker.cs  app chooser
  Macro*.cs        Macro Studio: .vcmacro format, recorder (hooks only while recording), step rules,
                   bound window, player with a check before every step, "wait until" conditions, and the Studio window
  SmartTargets.cs  smart target: UI Automation search, screenshots, the check before the click, the F7 probe
  SafeClick.cs / MacroPreview.cs / SimulationDialog.cs  SafeClick: impactful actions and approval, passive capture
                   and step evaluation, the simulation window and the safety review
  ImageTarget.cs   image recognition (normalized correlation in 8 px cells; pure, testable functions)
  Hud.cs           on-screen indicator (separate window, click-through, never takes focus)
  Profiles.cs / ProfilesDialog.cs  per-app Window Lock profiles: the .vcprofile file, export and import
  MacroAi.cs / AiDialog.cs  Build with AI: the API call (HTTPS, structured output), proposal validation, DPAPI key
  Developer.cs     developer mode: command line, exit codes, {{name}} variables and the JSON Lines log
  Hotkey.cs        global hotkeys (RegisterHotKey) and validation
  Settings.cs      .ini settings with validation and a safe fallback
  MainForm.cs      main window;  Controls.cs / Theme.cs / Icons.cs  keys, LEDs, display and drawn icons (DESIGN.md)
  Loc.cs           Portuguese and English strings
tests/           dependency-free test runner (162 unit tests + 8 integration tests)
assets/          app icon and brand identity (assets/brand: icon, logos and brand art)
docs/            screenshots
```

The click engine talks to Windows through a tiny interface (`IClickSink`), so the timing, stop, blocked-input and hold-mode tests run without moving your mouse.

## Support

V-Clicker is free, ad-free and open source. If it helps you, you can support the project (also through the **Support ♥** link in the app's footer):

- **Ko-fi** (card or PayPal, from any country): [ko-fi.com/vemryx](https://ko-fi.com/vemryx)
- **Pix** (Brazil, random key): `f1f20b16-c964-4465-9f45-2cb2a7647726`

## License

[MIT](LICENSE) © 2026 marquezinii
