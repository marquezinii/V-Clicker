# Conditional Click V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A `waitfor` macro step that waits for an element, a color or a dialog, with a mandatory timeout.

**Architecture:** The step is a new `StepKind.WaitFor` in `Macro.cs` (same file format conventions). Reading the
screen lives in `src/MacroCondition.cs` (`MacroConditions.Probe` at F7, `MacroConditions.Read` while running). The
player gets an injectable `Func<MacroStep, ConditionRead>`, like the guard, so the waiting rules are unit-tested
without a screen. The Studio converts a wait, inserts from scratch, re-points and converts back.

**Spec:** `docs/superpowers/specs/2026-10-22-conditional-click-design.md`

## Global Constraints

- Poll 100 ms; met after 2 consecutive positive reads; timeout default 30 s, 1–3600 s; one read ≤ 500 ms (later
  answers discarded); after a pause, reads are ignored for 200 ms and the count restarts.
- Pause (clock stopped): another app in front, another process covering the point. "Not yet" (clock runs): the
  macro's own app (dialog not open, own window covering, point outside the window), failed/slow/mismatched reads.
- Color: 5×5 average, ±24 per channel, window under center + 4 corners checked before and after the read.
- Element: interactive control first (≤ 3 levels up), else first named element; same process as the bound window.
- The step never clicks. Stop before the next step on timeout. F8 < 100 ms.

## Review Focus

- A late or failed read never advances the macro.
- Paused time never counts toward the timeout.
- Convert → back is identical after save/reopen (`was=` + `recorded=`).
- A color read over a covering window is never "met".

---

### Task 1: Model and file format (`src/Macro.cs`, tests in `MacroTests.cs`)
- `StepKind.WaitFor`; `enum ConditionKind { Element, Color, Window }`; step fields `Condition`, `ControlType`,
  `Rgb`, `WasMs`, `WasRecordedMs`; `Text` = element name, `DurationMs` = timeout, `At` + `Window` = point anchor,
  `Window` = the dialog for `Window` conditions.
- `waitfor element X Y "Type" "Name" timeout=N [was=N] [recorded=N] [in "class" "title"]`,
  `waitfor color X Y RRGGBB timeout=N ...`, `waitfor window "class" "title" timeout=N ...`.
- Tests: round trip of the three kinds; malformed `waitfor` lines stay Unknown.

### Task 2: Reading conditions (`src/MacroCondition.cs`)
- `ConditionRead { Met, NotYet, Away, Covered, Closed }`; `Probe Capture(IntPtr bound, Point screen)`;
  `ConditionRead Read(IntPtr bound, uint process, MacroStep step)`.
- Tests with own forms (reads run off the UI thread while it pumps): button found from its text, enabled vs
  disabled, dialog open vs not, color of a panel, own window covering → NotYet.

### Task 3: Player (`src/MacroPlayer.cs`)
- `Start(..., Func<MacroStep, ConditionRead> condition)`; `MacroStopReason.ConditionTimeout`; `Watching`, `WatchedFor`.
- Tests: met after 2 reads continues at once; one blip does not pass; timeout stops before the next step; Away pauses
  without spending the timeout; Stop while watching < 100 ms; Closed stops.

### Task 4: Studio (`src/MacroStudio.cs`, `src/Loc.cs`)
- Wait detail: **Transformar em condição**; edit bar: **+ Condição**; condition detail: timeout, **Apontar de novo**,
  **Voltar a ser espera**. Condition dialog after F7. Status `Aguardando …`; stop message names the step.
- Tests: convert and back identical; insert from scratch; dialog builds.

### Task 5: Docs, full test run, independent review, fixes.
