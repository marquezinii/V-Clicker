# Smart Target V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A click step can carry a target (UI element or captured image). The target is found anew, confirmed and
hit-tested before every click. The "wait until element" condition shares the same locator.

**Architecture:**
- `ImageTarget` (pure, done in the risk spike) matches images.
- `SmartTargets` reads the screen: UI Automation search, screenshots, the final check, and the F7 probe.
- `MacroPlayer` gets a `locate` and a `confirm` delegate, and a generic watch loop shared with `WaitFor`.
- The Studio runs the F7 analysis on a background task and shows a `TargetDialog`.

**Tech Stack:** C# 12, .NET Framework 4.8 WinForms, System.Windows.Automation, GDI+.

**Spec:** `docs/superpowers/specs/2026-10-23-smart-target-design.md`

## Global Constraints

- No new dependencies; net48; TreatWarningsAsErrors; strings in PT and EN in `Loc.cs`.
- Never click without: a unique target, two finds at the same place (±2 px), and a final check that passes.
- Never fall back to `step.At` for a targeted step.
- UI Automation and screen reads:
  - run off the UI and player threads, through `MacroConditions.WithTimeout`;
  - at most one in flight in the whole program;
  - a late result is thrown away;
  - F8 does not wait for them.
- Element search scope: the step window's UIA subtree. No per-element process filter.
- Defaults: find limit 10 s (1–3600 s); 2 s per element search; image region sides 32/64/96; cells of 8 px.

## Review Focus

1. **Target gone, look-alike present** ("Baixar DOC" where "Baixar PDF" was) → never found, never clicked.
2. **Abandoned slow search during a stop** → no click, no extra thread on the next run, and F8 answers in < 100 ms.
3. **Chrome's first contact** (empty tree for ~1 s) → the F7 uniqueness check retries; it never says "unique" from
   an empty tree.
4. **Old Conditional Click files** (`waitfor element` without `id`) → still parse and now search the whole window.
5. **The HUD over the target** → excluded from the screenshot; it never makes an image "covered" or "not found".

---

### Task 1: Model and file format

**Files:** Modify `src/Macro.cs`. Test `tests/VClicker.Tests/MacroTests.cs`.

**Interfaces (produces):**
- `enum TargetKind { Element, Image }`
- `sealed class SmartTarget`:
  - `Kind`
  - `ControlType`, `Name`, `AutomationId` (string)
  - `Png` (byte[])
  - `TimeoutMs` (default `DefaultTimeoutMs = 10000`)
  - `Clone()`
  - `Bitmap Decode()`
- New fields on `MacroStep`:
  - `MacroStep.Target` (`SmartTarget`, null = fixed point; Click/DoubleClick only)
  - `MacroStep.AutomationId` (for `waitfor element`)
- `Clone()` deep-copies `Target`.

**File format:**
- `click|double <btn> X Y find element "T" "N" [id "I"] timeout=N [in "c" "t"]`
- `... find image <base64 PNG> timeout=N`
- `waitfor element X Y "T" "N" [id "I"] timeout=...`
- A malformed `find` makes the line `Unknown`. That covers: an image that is not a square PNG of 32/64/96 px, an
  empty name, and a missing `timeout`.

**Describe():**
- `Clicar no botão "Baixar PDF"`, built from the existing `cond.type.*` labels;
- `Clicar na imagem capturada (gravado em 640, 520)`.

- [ ] Tests:
  - round trip for element (with and without `id`, with `in`) and for image;
  - malformed `find` → Unknown, line kept;
  - `Clone` keeps the target;
  - `waitfor element` with `id` round-trips.
- [ ] Implement; `.\build.ps1 -Test` green.

### Task 2: Locator (`src/SmartTargets.cs`), HUD out of captures

**Interfaces:**
- `enum TargetStatus { Found, NotFound, Ambiguous, Disabled, Offscreen, Inconclusive }`
- `struct TargetLook`:
  - `ConditionRead Read`
  - `TargetStatus Status`
  - `int Count`
  - `Point Screen`
  - `Rectangle Bounds`
  - `object Element` (the `AutomationElement`, for the hit test)
- `SmartTargets.Find(IntPtr bound, uint process, MacroStep step, CancellationToken)` → `TargetLook`.
  - First, `Closed` / `Away` as in the condition read.
  - The anchor is not open → `NotYet` / `NotFound`.
- `SmartTargets.Confirm(IntPtr bound, uint process, MacroStep step, TargetLook look, CancellationToken)` →
  `ConditionRead`. It checks:
  - `Closed` / `Away`;
  - the point is inside the anchor's client area;
  - `Cover` on a 5×5 area around the point;
  - Element: `FromPoint` walked up ≤ 3 parents, then `Automation.Compare` with `look.Element`;
  - Image: a screenshot of `look.Bounds`, then `ImageTarget.IsAt`.
- `SmartTargets.FindElement(IntPtr anchor, string type, string name, string id)` → `TargetLook`, with `Read` unset.
  - Search with `FindAll(Descendants)` using `ControlType` + `Name` (+ `AutomationId`).
  - Names containing `\n`: search by type only, then filter on `MacroConditions.Normalize`.
- Image `NotFound` while another program's window covers a point of a 16 px grid over the client area → `Covered`.
- `MacroConditions.WithTimeout` becomes `internal`. It refuses to start while the previous call is still running, and
  then returns the fallback. `inFlight` is global.
- `MacroConditions.Read`, Element case, uses `FindElement` on the anchor. A unique, enabled, on-screen element whose
  center is inside the client area, plus a `Cover` check, means `Met`.
- In `Hud`, on handle creation: `SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE = 0x11)`. Failure is
  ignored.

- [ ] Tests on the pure parts:
  - `ImageTarget`, with GDI-drawn buttons:
    - moved and brightness-shifted → Found;
    - duplicate → Ambiguous;
    - different word → not Found;
    - a plain region has too little detail;
    - a 1920×1080 search takes < 300 ms.
  - The element-name filter.
- [ ] Integration (Task 6) covers the UIA paths.

### Task 3: Player

**Files:** Modify `src/MacroPlayer.cs`. Test `MacroTests.cs`.

- `Start(..., Func<MacroStep, CancellationToken, TargetLook> locate = null, Func<MacroStep, TargetLook, CancellationToken, ConditionRead> confirm = null)`.
  - When the delegates are null, a target is never found and the step times out.
- `MacroStopReason.TargetTimeout`.
- `LastTarget` exposes the status and count of the last look, for the message.
- Extract `Watch(int limitMs, MacroStopReason onTimeout, int confirmReads, Func<ConditionRead> read)` from
  `WaitFor`, with the same pause, settle and clock rules. `WaitFor` = `Watch(step.DurationMs, ConditionTimeout, 2, ...)`.
- `FindTarget(step)` = `Watch(step.Target.TimeoutMs, TargetTimeout, 1, closure)`. The closure:
  1. returns `Met` only on the second find within 2 px of the first;
  2. then calls `confirm`. `Met` → click point; anything else → reset.
- Click/DoubleClick with a `Target`: `MoveTo(FindTarget(step))` and press. Never `Check(step)` or `step.At`.

- [ ] Tests, with a fake locate/confirm and `FakeSink`:
  - found at (700, 610) twice → the click goes there;
  - a moving target (two different points) → no click until stable;
  - ambiguous forever → `TargetTimeout`, no input;
  - confirm says `NotYet` → no click and the search restarts;
  - `Away` stops the clock;
  - F8 during a slow locate → stops in < 100 ms with no input;
  - `step.At` is never moved to.

### Task 4: F7 probe for targets and conditions

**Files:** `src/SmartTargets.cs`, `src/MacroCondition.cs`.

- `TargetProbe SmartTargets.Capture(IntPtr bound, WindowRef stepWindow, Point screen, Bitmap clientShot, Rectangle client)`.
  - Called on a background task. The screenshot is taken on the UI thread at F7.
  - Checks:
    - the point must be in the step's window (`Locate` + `WindowRef` equality), else `null`;
    - element: `FromPoint` + `Resolve`;
    - uniqueness by `FindElement` on the anchor. Retry up to 4× every 300 ms while the count is 0 (Chrome warming
      up);
    - if the count is above 1 and the element has an `AutomationId`, retry with the id.
  - Produces:
    - `ElementCount`
    - `AutomationId`
    - `ElementPreview` (crop of the element's rect)
    - `Images[side]` (crop, detail, count from `ImageTarget.Search` on the screenshot)
    - `Suggested` (Element if a unique named element exists; else the smallest unique image with enough detail)
- `MacroConditions.Capture` also fills `ElementCount` and `AutomationId` (same helper). The ConditionDialog disables
  Element when it is ambiguous and stores `id`.

### Task 5: Studio

**Files:** `src/MacroStudio.cs`, `src/Loc.cs`, `tests/VClicker.Tests/Screenshot.cs`.

- New capture modes: `CaptureFor.Target`, `Retarget`.
- After F7, for every condition or target mode:
  1. read the cursor and take the client screenshot (UI thread);
  2. start `Task.Run(capture)` with `PerMonitorDpi`;
  3. `analyzing` makes the Studio busy, and the status reads "Analisando o ponto…";
  4. `Tick` picks up the result, brings the Studio back and opens the dialog;
  5. F8 / Stop drops the result.
- `TargetDialog`:
  - the suggested sentence and a preview (`PictureBox`, zoom, nearest-neighbour for images);
  - the uniqueness line;
  - a secondary Segmented: "Pelo nome" / "Pela aparência";
  - a Segmented of sizes, with the surroundings note;
  - timeout;
  - Confirm, enabled only when the target is unique.
- Detail panel:
  - Click/DoubleClick without a target: **Transformar em alvo inteligente** plus its tip.
  - With a target:
    - the preview and description;
    - the find time limit;
    - **Apontar de novo**;
    - **Voltar para o ponto fixo (X, Y)**;
    - the recorded point shown read-only;
    - no point capture button.
- `ConvertToTarget(index, SmartTarget)` / `RevertTarget(index)` are exact and undoable.
- The step list shows a marker for targeted steps.
- `WatchingText`: `Procurando "…"… N s` / `Procurando a imagem… N s`.
- Messages:
  - `OnPlayerStopped` builds them for `TargetTimeout` from `player.LastTarget`;
  - the run is "stopped before step N";
  - the step gets selected.
- `Screenshot.cs`: a `target` option renders the dialog.

### Task 6: Integration, docs, review

- `IntegrationTests`: a `NoActivateForm` with a "Baixar PDF" button that moves after the macro starts.
  - The targeted click lands on it (the button's `Click` fires).
  - A second form state with two such buttons → `TargetTimeout`, no click.
- Docs: README PT/EN (feature bullet and test counts), CHANGELOG, ROADMAP.
- Run `.\build.ps1 -Test`, then `-Integration`. Then a review agent on the branch diff, and fix its findings.
