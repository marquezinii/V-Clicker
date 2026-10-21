# Macro Studio V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gravar, ver, editar, salvar e repetir sequências de mouse e teclado presas a uma janela, sem mudar o modo casual.

**Architecture:** Model and file format (`Macro.cs`) are plain data. A pure `StepBuilder` turns recorded events into
steps, so the recording rules are unit-tested without hooks. `MacroRecorder` owns the low-level hooks (own thread
with a message loop) and a processing thread that resolves windows and password fields, then feeds `StepBuilder`.
`MacroPlayer` runs steps on its own thread against an `IInputSink` (SendInput or a fake) and a guard function
(`MacroTarget.Check`), with an absolute schedule like `ClickEngine`. `MacroStudio` is the window; `MainForm` hosts it
(button, hotkeys, HUD, tray, beeps, mutual exclusion).

**Tech Stack:** C# 12, WinForms, .NET Framework 4.8, Win32 P/Invoke, UI Automation (`UIAutomationClient`,
`UIAutomationTypes`: part of .NET Framework). No third-party packages.

**Spec:** `docs/superpowers/specs/2026-10-21-macro-studio-design.md`

## Global Constraints

- One `.exe`, net48, `TreatWarningsAsErrors`, LF line endings, comment density like the surrounding code.
- Every new string in PT and EN in `src/Loc.cs` (same placeholders in both).
- Hooks exist only between Gravar and Parar; never in casual mode.
- Hook callbacks only enqueue; injected events (`LLMHF_INJECTED` 0x1, `LLKHF_INJECTED` 0x10) are dropped.
- Recorder and player threads run per-monitor DPI aware (`SetThreadDpiAwarenessContext(-4)`, best-effort on old
  Windows); every screen/client coordinate of a macro is physical pixels.
- Constants: countdown 3 s; wait threshold 250 ms (rounded to 100 ms); min step spacing 50 ms; char spacing 50 ms;
  long pause ≥ 5000 ms, shortened default 1000 ms; hover rest ≥ 1000 ms; same-point radius 4 px; typing split at
  ≥ 1000 ms; resume settle 200 ms (`ClickEngine.ResumeSettleMs`); pause limit 5 min; F8 stop < 100 ms.
- The casual tests keep passing unchanged.

## Review Focus

- A step must never go to another window: guard runs right before every input, including between characters of `Digitar`.
- F8 / Parar mid-wait or mid-text releases every key and button the player pressed.
- ABNT2 typing (Shift, AltGr, dead keys) records the text the user actually saw.
- A hand-edited or newer `.vcmacro` never crashes, never loses unknown lines on save, never runs with them.
- The Studio stays usable with the HUD off (status line, beeps, tray).

---

### Task 1: Macro model and file format

**Files:** Create `src/Macro.cs`. Test: `tests/VClicker.Tests/MacroTests.cs` (new `MacroTests.Run()` called from `UnitTests.Run`).

**Interfaces (produces):**
- `enum StepKind { Click, DoubleClick, Drag, Hover, Scroll, Type, Key, Shortcut, Wait, Password, Unknown }`
- `sealed class WindowRef { string Class; string Title; }` — null on a step = the bound window.
- `sealed class MacroStep` fields: `Kind`, `ClickButton Button`, `Point At`, `Point To` (drag end),
  `WindowRef Window`, `string Text`, `Keys Key` (key code + `Keys.Shift/Control/Alt` flags), `int Count` (key
  repeats, ≥1), `int Notches` (wheel, + up / − down), `int DurationMs`, `int RecordedMs` (waits; 0 = not recorded),
  `string Raw` (Unknown: the original line). `MacroStep Clone()`. `string Describe()` (localized sentence of the
  spec's table). `bool IsMouse`.
- `sealed class Macro` fields: `Name`, `ExeName`, `Title`, `Size ClientSize`, `int Repeat` (0 = until stopped),
  `List<MacroStep> Steps`. `Macro Clone()`. `bool HasText`, `bool HasPassword`, `bool HasUnknown`,
  `IEnumerable<int> LongWaits()` (indexes, `DurationMs >= 5000`).
- `static class MacroFile`: `string Folder(string settingsPath)` (`Macros` beside the settings file),
  `void Save(Macro, string path)` (write temp + replace), `Macro Load(string path)` → throws `FormatException` with
  a short reason on an invalid header; unknown step lines become `StepKind.Unknown` with `Raw`.
  `string SafeFileName(string name)`.

Format (UTF-8, one record per line, `key=value` header then `[steps]`):
```
V-Clicker macro 1
name=Relatórios de sexta
exe=chrome.exe
title=Sistema - Google Chrome
client=1280x720
repeat=60
[steps]
click left 812 440
double left 10 20 in "#32770" "Salvar como"
drag left 100 200 400 200
hover 300 210
scroll -3 500 400
type "Relatório \"mensal\""      (escapes: \\ \" \n \t; any char may be written raw)
key Shift+Tab x2
shortcut Ctrl+C
wait 1000 recorded=25000
password
```

- [ ] Tests: round trip of every kind (including quotes, backslash, newline, non-ASCII, anchored steps); header
  without the magic line throws `FormatException`; `blah 1 2` survives load → save byte-identical as Unknown and
  sets `HasUnknown`; negative/huge numbers are clamped (wait 0…24 h, coords ±100000, count 1…999); `SafeFileName`
  strips `\/:*?"<>|`, trims dots/spaces, refuses reserved names (`CON`, `NUL`…), never returns empty.
- [ ] Implement; run `.\build.ps1 -Test`; expect all green.

### Task 2: StepBuilder (events → steps)

**Files:** Create `src/MacroRecorder.cs` (this task: `RecordedEvent`, `StepBuilder`, `DeadKeys`). Test: `MacroTests.cs`.

**Interfaces (produces):**
- `enum EventKind { MouseDown, MouseUp, Move, Wheel, KeyDown, KeyUp, Left, Back }` (`Left`/`Back`: the bound window
  lost / regained the foreground, from polling).
- `struct RecordedEvent { EventKind Kind; int Time (ms, hook clock); Point At (client-relative to Window);
  WindowRef Window; bool InTarget; ClickButton Button; int Notches; Keys Key; string Text (chars it produces, already
  dead-key composed; null = none); bool Password; }`
- `sealed class StepBuilder`: `StepBuilder(int doubleClickMs)`; `void Add(RecordedEvent e)`;
  `List<MacroStep> Finish(int stopTime)`; `List<MacroStep> Steps` (live view for the Studio).
- `static class DeadKeys`: `string Compose(char dead, char next)` (`´`+`a`→`á`, `~`+`n`→`ñ`, `^`+`e`→`ê`,
  `` ` ``+`a`→`à`, `¨`+`u`→`ü`, else dead+next; via combining marks + `Normalize(FormC)`).

Rules (copy of the spec + builder decisions): events with `InTarget == false` produce nothing but time still
passes; a gap ≥ 250 ms before an action becomes `Wait` (rounded to 100 ms, `RecordedMs = DurationMs`); click /
double click (same button, ≤ 4 px, within `doubleClickMs`) / drag (> 4 px while down); hover (rest ≥ 1000 ms, not
followed by a mouse action within 4 px; consecutive rests keep the last); wheel at the same point with < 250 ms
gaps merges; `Text` events merge into `Type` while gaps < 1000 ms; special keys with optional Shift → `Key`
(merged ×N when < 250 ms apart and identical); Ctrl/Alt combos without text → `Shortcut`; Win combos, Alt+Tab,
Alt+Shift+Tab, Alt+Esc, Alt alone dropped; `Password` events merge into one `Password` step; trailing wait from
the last action to `min(stopTime, first Left after it)`.

- [ ] Tests (one per rule above, each feeding a short event list and asserting `Describe()` texts and durations),
  plus the spec scenario (`Enter`, wait, click, wait, click, `↓`) producing exactly that list; `DeadKeys` table.
- [ ] Implement; run tests.

### Task 3: Target windows and DPI

**Files:** Create `src/MacroTarget.cs`; modify `src/WindowLock.cs` (make `IsCandidate` → `internal static bool IsAppWindow`, expose `ClientArea` as internal). Test: `MacroTests.cs`.

**Interfaces (produces):**
- `static class MacroTarget`:
  - `bool Owns(IntPtr bound, IntPtr window)` — window is `bound`, or its `RootOwner` is `bound`, or (same process,
    not `WindowLock.IsAppWindow`, and `RootOwner(foreground) == bound`).
  - `WindowRef Describe(IntPtr bound, IntPtr root)` — null when `root == bound`; else class + title (title empty for
    `#32768`).
  - `IntPtr FindAnchor(IntPtr bound, WindowRef w)` — bound if null; else the frontmost visible top-level window that
    `Owns` and matches class (+ title unless menu).
  - `MacroCheck Check(IntPtr bound, uint pid, MacroStep step, out Point screen)` with
    `enum MacroCheck { Ok, NotFocused, AnchorMissing, PointOutside, Covered, Closed }`.
  - `IDisposable PerMonitorDpi()` — sets the thread DPI context, restores on dispose.
  - `bool IsBrowser(string exe)` — chrome, msedge, firefox, brave, opera, vivaldi (`.exe`, case-insensitive).
- [ ] Tests with two test forms (one owning a dialog-like form, one independent): `Owns` true for owned, false for the
  independent window of the same process; `FindAnchor` by class+title; `Check` → `Closed` after dispose,
  `PointOutside` for a point beyond the client area, `Ok` for a point inside when in front.
- [ ] Implement; run tests.

### Task 4: MacroPlayer

**Files:** Create `src/MacroPlayer.cs`. Test: `MacroTests.cs`.

**Interfaces (produces):**
- `interface IInputSink { bool MoveTo(Point screen); bool Button(ClickButton b, bool down); bool Wheel(int notches);
  bool Key(Keys key, bool down); bool Char(char c); }`; `sealed class SendInputInputSink : IInputSink` (uses
  `Native.SendInput`, `KEYEVENTF_UNICODE` for chars, extended flag for arrows/Home/End/PgUp/PgDn/Insert/Delete).
- `enum MacroStopReason { Completed, Manual, TargetClosed, PauseTimeout, Password, Blocked, Suspended, Unsupported }`
- `enum MacroPhase { Idle, Countdown, Running, Paused }`
- `sealed class MacroPlayer : IDisposable`: `MacroPlayer(IInputSink sink)`;
  `bool Start(Macro macro, Func<MacroStep, MacroCheckResult> guard, int countdownMs = 3000)` where
  `struct MacroCheckResult { MacroCheck Status; Point Screen; }`; `void Stop(MacroStopReason r = Manual)`;
  `bool WaitForStop(int ms)`; `event Action<MacroStopReason> Stopped` (worker thread); properties `Phase`,
  `Iteration` (1-based), `StepIndex`, `PauseCause`, `CountdownRemaining`.
- Scheduling: per iteration, `due[i]` = start + Σ(previous waits) + 50 ms × steps so far; a step starts at
  `max(due, previousEnd + 50 ms)`; pauses shift the remaining schedule by the paused time. Waits sleep on the cancel
  handle (wakes at once on Stop). `Type` sends one char per ≥ 50 ms, guard before each char. Drag: down, 10 moves
  over min(recorded, 1 s)… simple: 10 moves over 200 ms, up. Hover: move only. Scroll: move + wheel. Password →
  stop `Password`. Unknown anywhere → refuse at Start (`Unsupported`).
- Pause: guard not `Ok` → `Paused` with the cause; resumes after 200 ms of `Ok`; `Closed` stops; 5 min stops.
- `finally`: release every button/key the player pressed and has not released.
- [ ] Tests with `FakeInputSink` (records calls with timestamps) and a scripted guard: order of inputs for each kind;
  repetition count; schedule does not drift over 3 repetitions (each step ≤ 50 ms late); Stop during a 5 s wait
  ends < 100 ms; Stop between chars leaves no key down; pause/resume with settle; `Closed` → `TargetClosed`;
  `Password` stop; timeout with a short `PauseLimitMs` test hook.
- [ ] Implement; run tests.

### Task 5: MacroRecorder (hooks)

**Files:** Modify `src/MacroRecorder.cs` (add `MacroRecorder`); add `UIAutomationClient`/`UIAutomationTypes` references in `src/VClicker.csproj`. Test: integration in `tests/VClicker.Tests/IntegrationTests.cs`.

**Interfaces (produces):** `sealed class MacroRecorder : IDisposable`: `MacroRecorder(IntPtr bound, IEnumerable<Hotkey> ignored, bool acceptInjected = false)`;
`void Start()`; `List<MacroStep> Stop()`; `bool Paused` (bound window not in front); `event Action Changed`
(processing thread). Hook thread: `SetWindowsHookEx(WH_MOUSE_LL / WH_KEYBOARD_LL)` + `GetMessage` loop, posts raw
data to a `BlockingCollection`. Processing thread (per-monitor DPI): resolves window under point
(`GetAncestor(WindowFromPoint, GA_ROOT)`), `MacroTarget.Owns`, client-relative point, modifier tracking, text via
`ToUnicodeEx(..., flags 0x4)` with the foreground thread's layout and tracked Shift/Ctrl/Alt/CapsLock, dead keys
via `DeadKeys`, password via `AutomationElement.FocusedElement.Current.IsPassword` (exceptions → not password),
hotkey filtering, and a 50 ms foreground poll that emits `Left`/`Back`.
- [ ] Integration test (`acceptInjected: true`): test form with a button and a text box; record SendInput click on
  the button, typing `Olá` (unicode) and `Enter`; stop; steps are click, type `"Olá"`, `Tecla Enter` (waits allowed
  between); replay with `SendInputInputSink` and `MacroTarget.Check`; button clicked once more, text box has `Olá`.
- [ ] Implement; run `.\build.ps1 -Integration`.

### Task 6: Macro Studio window and MainForm wiring

**Files:** Create `src/MacroStudio.cs` (form, step list, detail panel, banners, pause-optimizer dialog, undo stack);
modify `src/MainForm.cs`, `src/Loc.cs`. Test: `UnitTests.cs` / `MacroTests.cs`.

**Interfaces:**
- `MacroStudio(MainForm host, ClickSettings settings, string settingsPath)`; `bool Busy` (recording or running);
  `void StopAll(MacroStopReason reason)`; `bool OnCaptureHotkey()` (true = consumed); `(Color dot, string text)? Indicator`
  (null when idle and no flash) and `IntPtr IndicatorWindow`.
- `MainForm`: header button "Macro Studio" (one per app; reopen brings it front); `internal void Beep(bool)`,
  `internal void ErrorBeep()`, `internal bool ClickerBusy`; `IdStop` also calls `studio.StopAll(Manual)`;
  `IdCapture` first offers the key to the Studio; `IdToggle` ignored while `studio.Busy`; `StartClicking` refuses
  while `studio.Busy`; suspend/lock stop the studio too; `UpdateHud`/`UpdateTray` show the Studio indicator when present.
- Undo: snapshots (`Macro.Clone`) before each edit, 100 levels, Ctrl+Z / Ctrl+Y and buttons.
- Status line text = HUD text; beeps: one per countdown second, start, stop/pause.
- [ ] Tests: Studio builds with an empty and with a loaded macro and fits a 1024×720 screen; undo/redo restores
  the list; optimizer shortens only checked waits and Restaurar brings back `RecordedMs`; all new `Loc` keys exist in
  both languages with the same placeholders (existing localization test covers it).
- [ ] Implement; run `.\build.ps1 -Test`; open the Studio and check it by hand (screenshot).

### Task 7: Docs and verification

**Files:** `README.md`, `README.en.md`, `CHANGELOG.md`, `docs/ROADMAP.md`.
- [ ] Document the Studio (what it records, limits: browsers/tabs, password fields, elevated apps, Win shortcuts).
- [ ] `.\build.ps1 -Test -Integration` green; independent review of the branch; fix findings.
