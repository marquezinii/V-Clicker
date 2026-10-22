# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

Native Windows 10/11 desktop app: WinForms on .NET Framework 4.8, one portable `.exe`, every control painted by the
app itself (`src/Controls.cs`, `src/Theme.cs`). No web runtime, no third-party UI libraries, no network.

## Users

Primary: the casual player. They open V-Clicker next to a game (Roblox, clicker games), set a speed, maybe a fixed point
or Window Lock, and press F6. Most of the time the window sits beside or behind the game; they glance at it, they do
not study it.

Secondary: people automating repetitive PC tasks with the Macro Studio (record once, edit the steps, repeat). The
Studio is a separate window; the casual player never needs to understand it.

## Product Purpose

A fast, precise, lightweight autoclicker that is honest about what it does: it clicks at the requested rate, says
what Windows accepted (not what the game recognised), and stops and explains instead of clicking blindly.

## Positioning

- Real precision: 1 ms to 24 h, absolute schedule, CPS and h/min/s/ms editing the same exact speed.
- Window Lock: clicks only while the chosen app is in front; pauses and resumes on Alt+Tab. Each app keeps its own
  click settings (profiles), loaded when it is chosen again; export/import with the app's macros.
- Macro Studio with "wait until" conditions and Smart Target (find the button by name or appearance, never click on
  ambiguity), and SafeClick: see where a macro would click before running it, without sending anything.
- One `.exe`, no installer, no telemetry, no network, open source (MIT).

## Operating Context

Used while gaming or working in another app: global hotkeys (F6 start/stop, F8 stop, F7 capture position) do most of
the work, the on-screen indicator (HUD) and the tray icon report state over the game. The main window must read at a
glance: running or not, how fast, what F6 will do.

## Capabilities and Constraints

Must keep every existing function: CPS / interval speed, presets, jitter, button, single/double, repeat forever or N
times, toggle/hold mode, start delay, cursor or fixed position with capture, Window Lock (app picker, pause or stop on
focus loss, per-app profiles with export/import), configurable hotkeys, always on top, tray, sounds, HUD and its corner, PT/EN, restart as admin, GitHub
link, live stats (clicks, requested CPS, sent CPS, time), notices. Macro Studio: record, run, stop, repetitions,
macro list (new, rename, delete, save), step list and step editor, long-pause optimizer, conditions and smart target
dialogs, undo/redo. SafeClick: simulate (passive, over a window capture), safety review before impactful
shortcuts, optional duration limit per macro.

Constraints: high DPI (every metric through `Theme.Dp`), keyboard and screen-reader access on custom controls,
dark title bar via DWM, laptop-sized window (scrolls on short screens), PT and EN text lengths, tests drive the real
UI (`tests/VClicker.Tests`).

## Brand Commitments

The official logo is the paper-collage graphite V on a cream card (`assets/brand/app-icon.png`, source of
`assets/icon*`); the rest of the butter-yellow paper brand kit is in `assets/brand/` (see its README). The interface
palette is free: it does not have to reuse the brand's paper colors.

Made by Vemryx: the main window's footer credits "Made by Vemryx" with a link to https://vemryx.com.

## Evidence on Hand

Brand kit in `assets/brand/`, screenshots in `docs/`. No testimonials, user counts or benchmarks exist; do not invent
them.

## Product Principles

1. The casual player comes first: the main window answers "is it clicking, how fast, how do I start" before anything
   else.
2. Honest instruments: show what is measured, never imply more.
3. Light and fast: small window, instant response, no motion that delays a task.
4. Advanced power stays out of the way: productivity features live in the Studio.
5. Must not read as gamer RGB, as a generic template, or as a heavy app.

## Accessibility & Inclusion

Every custom control keeps keyboard operation, visible focus and an accessible name/role (already implemented; the
redesign must preserve it). Text contrast at least WCAG AA on the dark surfaces.
