# SafeClick V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Passive step-by-step macro simulation over a window capture, a one-time safety review per macro version before
running impactful shortcuts, and an optional per-macro duration limit.

**Architecture:** The simulation reuses the player's own position and target code: `MacroTarget.Check`, `SmartTargets.Find`
and `MacroConditions.Read` are split into a shared "resolve" core (no focus/cover checks, optional bitmap as pixel source)
plus the run-only checks. A new `MacroPreview` evaluates every step once against a `PrintWindow` capture; a new
`SimulationDialog` draws it. Approvals are `SHA-256(content without name) @ file creation time`, kept in the local
settings file.

**Tech Stack:** C# 12 on .NET Framework 4.8, WinForms, UI Automation, GDI/`PrintWindow`; dependency-free test runner
(`tests/VClicker.Tests`).

**Spec:** `docs/superpowers/specs/2026-10-21-safeclick-design.md`

## Global Constraints

- No new dependencies; no network; one portable `.exe`.
- Every UI metric through `Theme.Dp`/`Theme.Sz`/`Theme.Pad`; every string through `Loc` in PT and EN.
- Custom controls keep keyboard operation, visible focus and accessible names.
- The simulation never sends input, never changes focus, never restores or activates a window.
- Copy fixed by the spec (PT): "Alvo não disponível no estado atual; pode depender de uma ação anterior.", "Alvo não
  encontrado.", "Revisão de segurança", "Esta macro contém ações que podem afetar o aplicativo ou seus dados.",
  "Revisar na simulação", "Cancelar", "Executar mesmo assim", "Não foi possível capturar esta janela", "Limite de duração
  atingido".
- Impactful list, exact combinations only: Shift+Delete, Delete, Alt+F4, Ctrl+W, Ctrl+Shift+Esc.
- Build/test: `./build.ps1 -Test` (unit), `./build.ps1 -Integration` (drives real input).

## Review Focus

1. A macro opened, never saved (no file) and then saved: approval given before saving must survive the save (no second prompt).
2. High-DPI / per-monitor scaling: markers must land on the computed physical point when the capture is scaled to fit.
3. A capture of a window partly off-screen or on a second monitor with a different scale: positions still from the
   physical client rectangle, never from the screen.
4. Duration limit hit during a 24 h Wait step or a paused run: stops promptly (≤ 1 s late), never mid-drag.
5. Settings file edited by hand with a malformed `ApprovedMacros` line: ignored entry by entry, never a crash.

---

### Task 1: Duration limit

**Files:**
- Modify: `src/Macro.cs` (Macro, MacroFile.Write/Read)
- Modify: `src/MacroPlayer.cs`
- Modify: `src/Loc.cs`
- Test: `tests/VClicker.Tests/MacroTests.cs`

**Interfaces:**
- Produces: `Macro.LimitMinutes` (int, 0 = none, clamp 0..`Macro.MaxLimitMinutes` = 5999); file header key `limit=`
  written only when > 0; `MacroStopReason.TimeLimit`; `MacroPlayer.MinuteMs` (internal, default 60000, settable for tests).

- [ ] Test `limit: round-trips, written only when set, clamped` — `Write` of a macro with `LimitMinutes = 0` contains no
  `limit=`; 90 → contains `limit=90` and reads back 90; `limit=999999` reads back 5999; `limit=x` reads back 0.
- [ ] Test `player: the duration limit stops a long wait and a paused run, never mid-drag` — `MinuteMs = 100`,
  `LimitMinutes = 3`: a Wait of 60 s ends with `TimeLimit` within 300–1300 ms; a guard returning `NotFocused` forever
  ends with `TimeLimit`, not `PauseTimeout`; a Drag starting at 290 ms completes (button released by the drag itself,
  not by `ReleaseAll`) before the stop.
- [ ] Implement: deadline = end of countdown + `LimitMinutes * MinuteMs`; one `Idle(int ms)` wait primitive used by
  `SleepUntil`, `Watch` and `Check` that throws `EndRun(TimeLimit)` once past the deadline, except while a drag holds
  the button; a deadline check at the start of every step in `RunOnce`.
- [ ] `mstop.TimeLimit` = "Limite de duração atingido ({1}). A macro parou." / "Duration limit reached ({1}). The macro
  stopped." where {1} is "1 h 30 min" style.
- [ ] Run `./build.ps1 -Test`: PASS.

### Task 2: Impactful actions and approvals

**Files:**
- Create: `src/SafeClick.cs`
- Modify: `src/Settings.cs`, `src/MainForm.cs` (make `SaveSettings` internal)
- Test: `tests/VClicker.Tests/MacroTests.cs`

**Interfaces:**
- Produces:
  - `sealed class ImpactfulAction { int Step; Keys Key; bool Deletes; string Explain(); }`
  - `static class SafeClick`:
    - `List<ImpactfulAction> Impactful(Macro macro)` — Key and Shortcut steps whose `Key` equals one of the five combinations.
    - `string Signature(Macro macro)` — lowercase hex SHA-256 of `MacroFile.Write(macro)` without its `name=` line.
    - `string FileInstance(string path)` — creation time UTC ticks as invariant string; null if no file.
    - `string ApprovalKey(string signature, string instance)` → `signature + "@" + instance`.
  - `ClickSettings.ApprovedMacros` (`List<string>`, newest last, max `ClickSettings.MaxApprovals` = 200, entries matching
    `^[0-9a-f]{64}@[0-9A-Za-z-]{1,40}$`), serialized as one `ApprovedMacros=` line, comma-separated.
  - `bool ClickSettings.IsApproved(string key)`, `void ClickSettings.Approve(string key)` (moves to newest, trims oldest).

- [ ] Test `safeclick: impactful actions are the exact five combinations` — Key Delete, Shortcut Shift+Delete, Alt+F4,
  Ctrl+W, Ctrl+Shift+Esc flagged with the right step indexes; Ctrl+Delete, Alt+F5, Type "Delete" not flagged; Delete and
  Shift+Delete have `Deletes == true`.
- [ ] Test `safeclick: the signature ignores the name and changes with anything else` — rename: same; change a step,
  `Repeat`, `LimitMinutes`, `ExeName`: different; `Read(Write(m))` gives the same signature.
- [ ] Test `safeclick: approval is per local file, a copy with identical content is not approved` — save to `a.vcmacro`,
  approve its key; `File.Copy` to `b.vcmacro` (creation time set to now): key differs; `MacroFile.Save` over `a` and
  `File.Move` to `c`: key unchanged.
- [ ] Test `settings: approvals round-trip, are capped and survive garbage` — 205 approvals keep the newest 200;
  `ApprovedMacros=zz,<valid>,` keeps only the valid one.
- [ ] Implement.
- [ ] Run `./build.ps1 -Test`: PASS.

### Task 3: Shared resolve core for positions, targets and conditions

**Files:**
- Modify: `src/MacroTarget.cs`, `src/SmartTargets.cs`, `src/MacroCondition.cs`
- Test: `tests/VClicker.Tests/MacroTests.cs`, `tests/VClicker.Tests/SmartTargetTests.cs`

**Interfaces:**
- Produces:
  - `MacroCheck MacroTarget.Resolve(IntPtr bound, MacroStep step, out IntPtr anchor, out Rectangle client, out Point screen, out Point end)`
    — `AnchorMissing`, `PointOutside` or `Ok`; `Check` = `Closed` test + `Resolve` + its focus and cover checks (unchanged behavior).
  - `TargetLook SmartTargets.Look(IntPtr anchor, uint process, SmartTarget target, Bitmap shot, CancellationToken cancel)`
    — the search without focus checks; `shot` = the anchor's client area (null: the screen now, plus the foreign-cover
    check as today). `Find` = closed/focus checks + `FindAnchor` + `Look(..., null, ...)`.
  - `ConditionRead MacroConditions.Evaluate(IntPtr bound, uint process, MacroStep step, Bitmap shot, CancellationToken cancel, out Rectangle bounds)`
    — Window: anchor exists; Element: `FindElement` found and inside the client area (cover check only when `shot == null`);
    Color: 5×5 average from `shot` (or the screen when null) compared with `Close`. `Read` = closed/focus checks + `Evaluate(..., null, ...)`.
  - `int? MacroConditions.AverageColor(Bitmap shot, Point at)` — 5×5 average at a client point; null when the area leaves the bitmap.

- [ ] Test `smart target: the image search on a supplied capture finds it at the right screen place` — a 400×300 bitmap
  with a known 32 px pattern at (120, 80), anchor client at (1000, 500): `Look` returns `Found`, `Screen` (1136, 596).
- [ ] Test `conditions: colour from a supplied capture` — `AverageColor` of a solid 0x336699 area returns 0x336699; at
  (1, 1) returns null.
- [ ] Run all existing player/target/condition tests: unchanged PASS (behavior preserved).
- [ ] Implement.
- [ ] Run `./build.ps1 -Test`: PASS.

### Task 4: Window capture and step preview

**Files:**
- Create: `src/MacroPreview.cs`
- Test: `tests/VClicker.Tests/MacroTests.cs`, `tests/VClicker.Tests/IntegrationTests.cs`

**Interfaces:**
- Consumes: Task 3 `Resolve`, `Look`, `Evaluate`; Task 2 `Impactful`.
- Produces:
  - `enum ShotProblem { None, Failed, Minimized, Blank }`
  - `sealed class WindowShot : IDisposable { Bitmap Image; Rectangle Client /* screen, physical */; DateTime TakenAt; ShotProblem Problem; bool Usable; static WindowShot Take(IntPtr window); }`
    — `PrintWindow(window, dc, PW_RENDERFULLCONTENT)` into a window-rect bitmap, cropped to the client rect; on a
    per-monitor-DPI thread; `Blank` when every sampled pixel (16 px grid) has the same colour.
  - `enum PreviewState { Verified, InCapture, Unavailable, NotFound, Ambiguous, Outside, Inconclusive, NoPosition, NoApp }`
  - `sealed class StepPreview { int Index; PreviewState State; IntPtr Window; Point? At; Point? To; Rectangle? Bounds; int Count; string Detail; }`
    — points and bounds in the coordinates of that window's shot.
  - `sealed class MacroPreviewResult : IDisposable { List<StepPreview> Steps; Dictionary<IntPtr, WindowShot> Shots; bool WindowFound; List<ImpactfulAction> Impactful; int Located; int Unverifiable; }`
  - `static MacroPreviewResult MacroPreview.Evaluate(Macro macro, IntPtr bound, uint process, Func<IntPtr, WindowShot> shoot, CancellationToken cancel)`
    — one shot per anchor window, taken once; `bound == Zero` → every positional step `NoApp`.
- State rules (spec "Estados de um passo" and "Confiança dos resultados"):
  - Anchor missing or element/image not found: `Unavailable` when any earlier step exists, else `NotFound`.
  - Image target, colour condition or plain point on an unusable shot: `Inconclusive`; on a usable shot: `InCapture`.
  - Element target / element condition found by UI Automation: `Verified`; read timed out: `Inconclusive`.
  - Ambiguous (Count ≥ 2): `Ambiguous`. Point outside the client area: `Outside`. Type/Key/Shortcut/Wait/Password: `NoPosition`.

- [ ] Test `preview: states and the "may depend" rule` — fake shoot returning a blank shot: a fixed click is
  `Inconclusive`; a missing dialog at step 0 is `NotFound`, at step 3 `Unavailable`; a Type step is `NoPosition`.
- [ ] Integration test `preview: a real capture, positions on the shot, and no input reaches the app` — the test form
  with a button; macro: click at the button centre, Delete key; `Evaluate` with `WindowShot.Take`: shot usable, step 0
  `InCapture` at the button centre in shot coordinates (±1 px); the form's click counter and the text box stay unchanged.
- [ ] Implement.
- [ ] Run `./build.ps1 -Test` and `./build.ps1 -Integration`: PASS.

### Task 5: Simulation dialog

**Files:**
- Create: `src/SimulationDialog.cs`
- Modify: `src/Loc.cs`
- Test: `tests/VClicker.Tests/MacroTests.cs`

**Interfaces:**
- Consumes: Task 4.
- Produces:
  - `sealed class SimulationDialog : Form` — `SimulationDialog(Macro macro, Func<MacroPreviewResult> evaluate, int startStep)`;
    `internal int Step { get; set; }`; `internal void Refresh Capture()` named `Recapture()`.
  - `static class MarkerLayout`: `RectangleF Fit(Size image, Rectangle view)` (uniform scale, centred);
    `PointF ToView(Point shotPoint, Size image, Rectangle view)`; `List<MarkerGroup> Merge(IEnumerable<KeyValuePair<int, Point>> points, int px)`
    with `MarkerGroup { Point At; List<int> Steps; }` (points within `px` merge; label "2, 5").
- Layout: captions "Captura das HH:mm:ss"; capture view (PixelView-like, markers painted: current step full marker,
  others small dimmed numbers, ×2 for double click, line + arrow for drag, contour for bounds); summary on top
  (window found, located N of M, impactful steps, "Alguns alvos só poderão ser verificados durante a execução.");
  "Passo N de M", state badge, sentence ("Clicaria em (640, 610) na captura."), Anterior / Próximo / Atualizar captura /
  Fechar. Shot unusable: "Não foi possível capturar esta janela" + reason, no image. Esc closes; arrows navigate.
- [ ] Test `simulation: marker layout scales and merges` — image 2000×1000 in view 500×400 at (10, 20): fit
  (10, 95, 500, 250); shot point (1000, 500) → (260, 220); points 4 px apart merge at px 6, 10 px apart do not.
- [ ] Test `simulation: the dialog builds, navigates and fits a small screen` — with a fabricated result of 3 steps:
  `Step` 0→2 via Next, never past the end; dialog height ≤ 720 at scale 1.
- [ ] Implement.
- [ ] Run `./build.ps1 -Test`: PASS.

### Task 6: Studio integration

**Files:**
- Modify: `src/MacroStudio.cs`, `src/Loc.cs`
- Test: `tests/VClicker.Tests/IntegrationTests.cs`, `tests/VClicker.Tests/MacroTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 4, 5.
- Produces:
  - Run bar: Record, Run, **Simulate**, Stop. New third bar: Repetições, Até eu parar, **Limitar duração** (LedCheck) +
    NumberField minutes (1..5999, unit "min").
  - `internal void BeginSimulation()` (window choice as `BeginRun`; no app → dialog without capture).
  - `enum ReviewChoice { Cancel, Simulate, Run }`; seam `internal Func<List<ImpactfulAction>, ReviewChoice> ReviewSafety`
    (default: the review dialog, PT copy of the spec).
  - Approval instance: `FileInstance(path)` when the file exists, else a per-Studio `Guid` for the unsaved macro; on
    `Save()` of a macro whose session key was approved, approve the file key too.
  - Order in `BeginRun`: quick refusals → review (if impactful and not approved) → app-dependent refusals. `Run` approves
    then continues; `Simulate` opens the simulation; `Cancel` returns.
- [ ] Integration test (extend "macro studio: Record ... Run replays it") — append a Delete key step: first `BeginRun`
  calls `ReviewSafety` once (answer `Run`) and the run deletes; second run: not called; edit a step: called again;
  answer `Cancel`: nothing sent (text unchanged, studio not busy).
- [ ] Unit test `studio: limit field writes LimitMinutes and the bars fit 760 px` — tick + 45 → `Current.LimitMinutes == 45`; untick → 0.
- [ ] Implement.
- [ ] Run `./build.ps1 -Test` and `./build.ps1 -Integration`: PASS.

### Task 7: Docs and final review

**Files:** `docs/ROADMAP.md`, `PRODUCT.md`, `README.md`, `README.en.md`, `CHANGELOG.md`

- [ ] ROADMAP: SafeClick V1 implemented (link to the spec); "Camada de marcadores sobre a janela real" under evolutions.
- [ ] PRODUCT.md capabilities: Simulate, safety review, duration limit.
- [ ] READMEs: short "Simular / Simulate" section.
- [ ] CHANGELOG entry.
- [ ] Review the whole diff; run `./build.ps1 -Test` and `./build.ps1 -Integration`.
