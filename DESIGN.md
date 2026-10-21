---
name: V-Clicker
description: A fast, honest autoclicker drawn as a custom keyboard, for Windows.
colors:
  amber: "#F5C542"
  amber-skirt: "#BF952A"
  coral: "#F0645A"
  coral-skirt: "#B9483F"
  lamp-green: "#5FE08C"
  lamp-orange: "#FFA53D"
  lamp-red: "#FF6A5C"
  plate: "#1A1B1F"
  well: "#111215"
  glass: "#0B0C0E"
  hairline: "#2D2F35"
  alpha-key: "#33353C"
  alpha-skirt: "#16171A"
  mod-key: "#4B4E57"
  mod-skirt: "#26282D"
  light-cap: "#E7E2D6"
  light-cap-skirt: "#B3AC9C"
  legend: "#ECE8DF"
  legend-dim: "#A3A19A"
  ink: "#17181B"
typography:
  display:
    fontFamily: "Bahnschrift Light, Segoe UI"
    fontSize: "30pt"
    fontWeight: 300
  headline:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "17pt"
    fontWeight: 600
  number-key:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "12pt"
    fontWeight: 600
  readout:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "11.5pt"
    fontWeight: 600
  field:
    fontFamily: "Bahnschrift, Segoe UI"
    fontSize: "10.5pt"
    fontWeight: 400
  status:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "10pt"
    fontWeight: 600
  key-legend:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "9.5pt"
    fontWeight: 600
  label:
    fontFamily: "Bahnschrift, Segoe UI"
    fontSize: "9.75pt"
    fontWeight: 400
  segment:
    fontFamily: "Bahnschrift, Segoe UI"
    fontSize: "9.25pt"
    fontWeight: 400
  unit:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "8.5pt"
    fontWeight: 600
  silkscreen:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "8pt"
    fontWeight: 600
    letterSpacing: "1.4dp"
  caption:
    fontFamily: "Bahnschrift SemiBold, Segoe UI Semibold"
    fontSize: "7.5pt"
    fontWeight: 600
    letterSpacing: "1.2dp"
  body:
    fontFamily: "Segoe UI"
    fontSize: "9.5pt"
    fontWeight: 400
  body-list:
    fontFamily: "Segoe UI"
    fontSize: "10pt"
    fontWeight: 400
  note:
    fontFamily: "Segoe UI"
    fontSize: "9pt"
    fontWeight: 400
  note-small:
    fontFamily: "Segoe UI"
    fontSize: "8.5pt"
    fontWeight: 400
rounded:
  key: "5dp"
  focus: "6dp"
  well: "7dp"
  panel: "8dp"
  bezel: "11dp"
  pill: "16dp"
spacing:
  xs: "4dp"
  sm: "6dp"
  md: "8dp"
  field-pad: "10dp"
  lg: "12dp"
  xl: "16dp"
  inset: "18dp"
  gutter: "28dp"
components:
  key-primary:
    backgroundColor: "{colors.amber}"
    textColor: "{colors.ink}"
    typography: "{typography.key-legend}"
    rounded: "{rounded.key}"
    height: "36dp"
  key-primary-hover:
    backgroundColor: "#F6CD5C"
  key-stop:
    backgroundColor: "{colors.coral}"
    textColor: "{colors.ink}"
    typography: "{typography.key-legend}"
    rounded: "{rounded.key}"
    height: "36dp"
  key-stop-hover:
    backgroundColor: "#F27971"
  key-mod:
    backgroundColor: "{colors.mod-key}"
    textColor: "{colors.legend}"
    typography: "{typography.key-legend}"
    rounded: "{rounded.key}"
    height: "36dp"
  key-mod-hover:
    backgroundColor: "#595C64"
  key-alpha:
    backgroundColor: "{colors.alpha-key}"
    textColor: "{colors.legend}"
    typography: "{typography.key-legend}"
    rounded: "{rounded.key}"
    height: "36dp"
  key-alpha-hover:
    backgroundColor: "#43454B"
  key-number-row:
    backgroundColor: "{colors.alpha-key}"
    textColor: "{colors.legend}"
    typography: "{typography.number-key}"
    rounded: "{rounded.key}"
    height: "42dp"
  segmented:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend-dim}"
    typography: "{typography.segment}"
    rounded: "{rounded.well}"
    padding: "3dp"
    height: "32dp"
  segmented-chosen:
    backgroundColor: "{colors.light-cap}"
    textColor: "{colors.ink}"
    rounded: "{rounded.key}"
  field-number:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend}"
    typography: "{typography.field}"
    rounded: "{rounded.well}"
    padding: "0 10dp"
    height: "32dp"
  field-text:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend}"
    typography: "{typography.body-list}"
    rounded: "{rounded.well}"
    padding: "0 10dp"
    height: "34dp"
  choice-box:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend}"
    typography: "{typography.body-list}"
    rounded: "{rounded.well}"
    padding: "0 12dp"
    height: "36dp"
  hotkey-box:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend}"
    typography: "{typography.status}"
    rounded: "{rounded.well}"
    height: "40dp"
  led-check:
    backgroundColor: "{colors.alpha-key}"
    textColor: "{colors.legend}"
    typography: "{typography.label}"
    rounded: "{rounded.key}"
    width: "26dp"
    height: "24dp"
  section-label:
    textColor: "{colors.legend-dim}"
    typography: "{typography.silkscreen}"
    height: "28dp"
  display-glass:
    backgroundColor: "{colors.glass}"
    textColor: "{colors.legend}"
    rounded: "{rounded.bezel}"
    padding: "18dp"
  led-badge:
    backgroundColor: "{colors.glass}"
    textColor: "{colors.legend}"
    typography: "{typography.key-legend}"
    rounded: "{rounded.focus}"
    height: "30dp"
  list-row:
    backgroundColor: "{colors.well}"
    textColor: "{colors.legend}"
    typography: "{typography.body-list}"
    height: "32dp"
  list-row-selected:
    backgroundColor: "{colors.alpha-key}"
    rounded: "{rounded.key}"
  hud:
    backgroundColor: "{colors.glass}"
    textColor: "{colors.legend}"
    typography: "{typography.status}"
    rounded: "{rounded.pill}"
    height: "32dp"
---

# Design System: V-Clicker

Native Windows desktop (WinForms, .NET Framework 4.8). Every control is painted with GDI/GDI+ from `src/Theme.cs`, `src/Controls.cs` and `src/Icons.cs`; there is no stylesheet. Units: **dp** is a 96-DPI pixel that goes through `Theme.Dp` / `Theme.Pad` / `Theme.Sz` (the app is system-DPI aware, so 1dp = 1.5 device pixels at 150%); type sizes are **points**, which Windows scales itself. Colors are the hex values in `Theme.cs`, the normative source.

## Overview

**Creative North Star: "The Custom Keyboard"**

V-Clicker is drawn as a piece of hardware sitting next to the game: a graphite anodized plate, PBT keycaps in two flat layers (a face on a skirt), a black OLED module set into the plate, LEDs for anything that is on or off, and silkscreen lettering where other apps put panels. Every action is a key you press, every option is a key you hold down, every number you type sits in a recess in the plate, and every live number sits behind glass. The window is small and dense like a keyboard's top case, and it is built to be read at a glance from beside a game: running or not, how fast, what F6 will do.

The material story replaces the category's dashboard. There are no rounded cards on a dark page, no blue accent, no gamer RGB, and the brand's paper-collage art (butter yellow, cream, graphite card) stays in the logo and the brand kit. Color is spent the way a custom keyboard spends an accent keycap: one amber key per window is the thing that window exists to do; coral appears when that key becomes Stop or when something is deleted; green, orange and red exist only as lamps and message lines. Everything else is graphite, grey and off-white.

Depth is physical and has exactly three heights: recessed (wells and display glass), the plate, and raised (keycaps with a skirt and a short shadow). State is shown the way hardware shows it: a pressed key sits 1.5dp lower without its shadow, the chosen option is the light cap held down, an option that is on lights its lamp. Nothing animates; every change repaints at once.

**Key Characteristics:**
- Graphite plate, keycaps with a visible skirt, recessed wells, black display glass.
- One amber key per window; coral only to stop or delete; signal colors only as lamps and messages.
- Bahnschrift (DIN) for everything printed on the hardware, Segoe UI for sentences.
- Groups are silkscreen labels with a hairline, never cards.
- Instant, physical state changes: travel, lamps, held-down caps; no motion.
- Every metric in dp through `Theme.Dp`, every type size in points.

## Colors

A near-black graphite world lit by one warm amber key, with off-white legends and signal colors kept to lamps.

### Primary
- **Accent Amber** (#F5C542, skirt #BF952A): the one primary key per window: Start on the main window (the wide 60dp key), Run in the Macro Studio, Confirm / Apply / OK in dialogs. The same amber is the lit color of option lamps (LED checks, the OLED's LOCK lamp, the current item in a menu) and the 1.5dp edge that marks focus on a key or a well that is being edited or is listening for a hotkey. Legends on it are Ink.

### Secondary
- **Stop Coral** (#F0645A, skirt #B9483F): the key that stops or deletes. On the main window the Start key turns coral and stays latched down while clicking, so pressing it again stops; in the Studio, Delete (the macro) is coral. Legends on it are Ink.

### Tertiary
- **Lamp Green** (#5FE08C): clicking, running, done, saved, captured; the LED beside the step that is playing; the "running as admin" link.
- **Lamp Orange** (#FFA53D): paused, counting down, needs attention (focus lost, hotkey taken, long waits, password steps).
- **Lamp Red** (#FF6A5C): blocked, unknown steps, failed saves; the lamp on the Studio's Record key.

### Neutral
- **Graphite Plate** (#1A1B1F): the window itself and everything that is neither raised nor recessed.
- **Well Black** (#111215): every recess: number and text fields, segmented wells, hotkey boxes, choice boxes, step and window lists, menus.
- **OLED Glass** (#0B0C0E): display glass: the main OLED, the Studio's display, the LED badge, the on-screen indicator.
- **Hairline** (#2D2F35): the rule after a silkscreen label, menu borders and separators, the frame around a scrolling pane. Inside glass it is mixed 90% into the glass (#292B31).
- **Graphite Alpha Key** (#33353C, skirt #16171A): option keys, the number row, the Studio's step-edit bar, unchosen segments, hotkey caps, an off LED check; also the selection bar in lists and menus.
- **Grey Mod Key** (#4B4E57, skirt #26282D): action keys: Macro Studio, Capture, Choose…, New, Rename, Save, Record, the Studio's Stop, Cancel; an LED check that is on.
- **Light Cap** (#E7E2D6, skirt #B3AC9C): the chosen option in a segmented cluster, always drawn held down; the pressed color of footer links.
- **Off-White Legend** (#ECE8DF): legends, values and text on graphite and glass.
- **Dim Legend** (#A3A19A): row captions, silkscreen, OLED captions, units, unchosen segment legends, help text. It holds 4.7:1 even on an alpha key face; it is the lowest-contrast text color allowed.
- **Ink** (#17181B): legends on the light cap, the amber key and the coral key.

### Named Rules
**The One Amber Key Rule.** Each window has exactly one amber key, the action the window exists for (Start, Run, Confirm, Apply, OK). Elsewhere amber only lights lamps and marks focus; it is never a fill, a heading color or a decoration.

**The Coral Means Stop Rule.** Coral is the key that ends what is running or deletes something. It is never a second accent and never a warning color for text; warnings are Lamp Orange.

**The Lamps Only Rule.** Green, orange and red appear only as LEDs and as the color of a message line, note or step description. They are never a key face, a fill or a border.

## Typography

**Display Font:** Bahnschrift (the DIN face shipped with Windows 10 1709+), falling back to Segoe UI if it was removed
**Body Font:** Segoe UI (with Segoe UI Semibold)
**Label/Mono Font:** Bahnschrift in Light, Regular and SemiBold

**Character:** Bahnschrift is the lettering printed on the hardware: key legends, labels, units, readouts and the big OLED number, engineered and even. Segoe UI is the voice that explains: messages, help, notes and list text, where full sentences need a humanist face.

### Hierarchy
- **Display** (Bahnschrift Light 300, 30pt): the requested speed on the OLED, the one large number on screen.
- **Headline** (Bahnschrift SemiBold 600, 17pt): the "V-Clicker" wordmark beside the logo. The wide Start key carries a 13pt SemiBold legend; it is the only other large legend.
- **Title** (Bahnschrift SemiBold 600, 12pt / 11.5pt / 10pt): number-row legends (12pt), OLED readout values (11.5pt), status lines, hotkey caps, the indicator and app names in the window picker (10pt). Banner titles in the Studio use 10.5pt SemiBold in their lamp color.
- **Body** (Segoe UI 400, 9.5pt / 10pt / 9pt / 8.5pt): 9.5pt is the window default; 10pt for lists, text fields, choice boxes and menus (Semibold 10pt for a step's title or a found element); 9pt for the OLED message line, subtitles, the rate hint and banner notes; 8.5pt for Studio help notes and window titles in the picker.
- **Label** (Bahnschrift 400, 9.75pt; SemiBold 600, 9.5pt on keys): row captions and LED-check labels at 9.75pt regular; key legends at 9.5pt SemiBold (9pt on the Studio's small edit keys); segmented legends at 9.25pt, SemiBold on the chosen cap; units at 8.5pt SemiBold; key sub-legends (the hotkey, "CPS") at 8.5pt SemiBold on amber and coral keys and 7.5pt on the others. Typed numbers are Bahnschrift 10.5pt, centered.
- **Silkscreen** (Bahnschrift SemiBold 600, 8pt, uppercase, 1.4dp tracking; 7.5pt and 1.2dp on glass): group labels on the plate and captions on the OLED (CLIQUES, ENVIADO, TEMPO, LOCK). GDI has no tracking, so `Theme.DrawTracked` places one character at a time.

### Named Rules
**The Two Voices Rule.** Bahnschrift for anything printed on the hardware (legends, labels, numbers, units, silkscreen); Segoe UI for anything that is a sentence. A number is never Segoe UI and a two-line explanation is never Bahnschrift.

**The Silkscreen Rule.** A group is named by one uppercase silkscreen label in Dim Legend, sitting low in its 28dp row and followed by a hairline. Nothing sits above it: no kicker, eyebrow or second heading.

## Layout

The main window is a fixed instrument, not a responsive page: a header row across the top (40dp logo, wordmark and subtitle, the Macro Studio key at 136×36dp, the PT/EN cluster at 96×34dp), then two fixed columns, 372dp and 380dp, separated by a 28dp gutter, then a footer of credits and the admin link. The window padding is 16dp. The left column is the instrument, top to bottom in reading order: the OLED (164dp tall), the amber Start key (60dp tall, full column width), the speed cluster (unit selector, the speed field, the number row of five 42dp keys with 6dp between them, the rate hint, jitter) and the three hotkey boxes. The right column holds the settings clusters: Click & Start, Position, Window Lock, Options.

Settings rows have a label column (150dp on the left, 116dp on the right), controls 32dp tall, and a 6dp gap below each row; a section label takes a 28dp row with 4dp below it. Spacing steps are 4, 6, 8, 10, 12, 16, 18 and 28dp; 12dp separates large blocks (header, OLED), 8dp separates keys in a row. On a short screen the window scrolls instead of shrinking.

The Macro Studio is a resizable editor: a file bar (macro choice box at 300×36dp, then New, Rename, Delete, Save), a run bar (Record, Run, Stop, repetitions, "until I stop"), the display glass with its notes, a 12dp gap, then the step list well (60%) beside the step detail (40%, a scrolling pane in a hairline frame that ends level with the list) with 18dp between them, and the step-edit bar of alpha keys docked at the bottom. Bars put 8dp between keys and 12dp below themselves; Studio keys are 36dp tall. Dialogs use 18dp padding, explanation first, the 32dp field rows next, and the keys bottom-right with Cancel (Mod) before the amber key.

## Elevation & Depth

Depth is physical, not ambient. Three heights only: recessed, flat plate, raised. Recesses are flat fills with a shaded 1px edge (dark #070809 at the top, catching light #3C3D42 at the bottom); display glass adds a bezel (the plate darkened 45%, #0E0E11) around the black glass with a faint edge. Raised keycaps cast a short soft shadow onto the plate. No panel, dialog body or list ever floats on a drop shadow.

### Shadow Vocabulary
- **Key shadow** (two passes under the skirt: offset 1.5dp at 35% black, then inflated 1dp and offset 2.5dp at 14% black): every keycap at rest. Removed while pressed or latched.
- **Face dish** (vertical gradient from the face mixed 7% toward white at the top edge, to the face): every keycap face, as if the dish catches the light.
- **Well edge** (1px vertical gradient stroke, #070809 to #3C3D42): every recess and the glass bezel.

### Named Rules
**The Three Heights Rule.** Anything typed, listed or chosen from sits recessed; anything pressed is raised; anything live is behind glass. Nothing else gets depth.

**The Physical Press Rule.** Pressed, latched and chosen keys drop 1.5dp, show less skirt (2.5dp instead of 5.5dp) and lose their shadow. Hover lightens the face (8% toward white on graphite and grey keys, 14% on light, amber and coral keys); disabled mixes face and skirt 55% toward the plate. No glow, no scale, no animation.

## Shapes

Softly rounded hardware. Keycap skirts use a 5dp radius (never more than a third of the key's height) with a face 2.5dp narrower on each side and 1dp less rounded; focus rings follow at 6dp. Wells for single controls (fields, segmented clusters, hotkey boxes, choice boxes) are 7dp; list wells, the hairline frame around a scrolling pane and the glass inside a bezel are 8dp; the display bezel is 11dp. Selection bars in lists are 5dp, menu hover bars 4dp; menus themselves are square with a 1px hairline border. LEDs are circles (6 to 9dp). The on-screen indicator is a full pill (16dp radius on a 32dp height). Icons sit on a 16-unit grid with one stroke weight (1.35 units, never under 1.2px) and round caps and joins.

## Components

### Buttons
Keycaps in two flat layers, a lighter face on a darker skirt, legends in Bahnschrift SemiBold.
- **Shape:** 5dp skirt, face inset 2.5dp at the sides and 5.5dp above the bottom edge; 36dp tall in the Studio and dialogs (32dp in the generic ask dialog), 34 to 36dp in the main window header and clusters.
- **Primary:** the amber key with an Ink legend. On the main window it is the wide Start key (full column width, 60dp tall, 13pt legend, F6 as an 8.5pt sub-legend under it). While clicking it turns coral, stays latched down and reads Stop.
- **Stop / Delete:** the coral key with an Ink legend.
- **Secondary (Mod):** grey actions with off-white legends. The Studio's Stop is a Mod key with F8 as a sub-legend on its right; Record is a Mod key with a red lamp before its legend that lights while recording.
- **Alpha (Chip):** graphite keys for options and small or repeated actions: the number row (12pt value, "CPS" sub-legend), the Studio's step-edit bar (Up, Down, Duplicate, Delete, + Wait, + Condition, Undo, Redo at 9pt).
- **Hover / Focus:** hover lightens the face; pressing drops the cap 1.5dp. Focus is a 1.5dp amber ring at 6dp around the key, ink on the amber key itself. Space and Enter press the key as the mouse does.
- **Disabled:** face and skirt 55% toward the plate, legend dimmed.

### Chips
Segmented option clusters: several keys in one 7dp recess, separated by 2dp.
- **Style:** unchosen keys are graphite alpha caps with Dim legends (9.25pt); the hovered key's legend turns off-white.
- **State:** the chosen key is the light cap, held down, with an Ink SemiBold legend. Each key is as wide as its legend needs and spare width is shared out. With glyphs (the indicator corner picker) the keys show 15dp icons from `Icons.cs` and the names go to screen readers. Focus draws the well's edge in amber; arrow keys move the choice.

### Cards / Containers
There are no cards. Groups live on the plate under a silkscreen label; the only containers are recesses, glass, and one outline: a hairline frame around a pane that scrolls (the Studio's step detail), so the scroll edge reads.
- **Corner Style:** 7dp for single-control wells, 8dp for list wells and frames, 11dp bezel around 8dp glass.
- **Background:** Well Black for recesses, OLED Glass for displays, the plate itself inside a frame (a frame is never filled).
- **Shadow Strategy:** none; recesses carry the shaded edge described in Elevation & Depth.
- **Border:** the well edge gradient; glass has a faint edge (the glass mixed 45% toward the hairline); a frame is a 1px Hairline stroke.
- **Internal Padding:** 12dp in a plain well (6dp around a list), 18dp inside the main OLED, 14dp × 10dp inside the Studio display and the detail frame (4dp on the scrollbar side).

### Inputs / Fields
Anything typed sits in a recess.
- **Style:** Well Black, 7dp, 32dp tall in settings rows. Numbers are Bahnschrift 10.5pt centered with the unit at the right in 8.5pt SemiBold Dim; free text and choice boxes use Segoe UI 10pt, left aligned 10 to 12dp in. The choice box ends in a drawn chevron and opens a menu in the same well colors.
- **Focus:** the well's edge turns 1.5dp amber while editing (numbers and text) or while a choice box or hotkey box has the keyboard. Values are clamped on commit; arrows and the wheel step by 1, Shift by 10.
- **Hotkey box:** a 40dp well holding the combination as small graphite caps (one per part, 5dp apart); while listening, the edge and the "…" prompt are amber; Esc cancels.
- **Disabled:** the fill moves 50% toward the plate and the text dims.

### LED Check
Every on/off option is a lock key: a 26×24dp keycap with a 7dp lamp in its face, and its label 10dp to the right in Bahnschrift 9.75pt. Off: a graphite cap with a dark lamp. On: a grey Mod cap held down with the lamp lit amber. Focus is the amber ring around the cap.

### Display Glass (OLED)
The main window's instrument, 164dp tall in the left column. A bezel and black glass; inside, 18dp in: the status lamp (8dp) and status line (10pt SemiBold), and on the right the LOCK caption with its 6dp amber lamp; below, the requested speed in 30pt Bahnschrift Light with "CPS" (11pt SemiBold, Dim) and its tracked caption stacked beside it; on the right three readouts (tracked caption left, 11.5pt value right-aligned) in a 150dp column; then a hairline and the message line in Segoe UI 9pt, colored by its lamp. The Studio's display uses the same glass for an LED badge, the status text, and notice blocks separated by hairlines (banner title in the lamp color, sentence in off-white, an optional Mod key on the right).

### Lists and Menus
Lists sit in a well: 32dp rows (40dp in the window picker) on Well Black, the selected row on a graphite alpha bar at 5dp with an amber focus ring while the list has the keyboard. Step rows read: a green lamp if the step is playing, the number in Dim Bahnschrift, a 16dp icon and the description, colored by the step's lamp (Dim for waits, orange for long waits and passwords, red for unknown steps). Menus use the same well color with a hairline border, a graphite hover bar, and an amber lamp before the current item.

### On-screen Indicator
A sliver of display glass over the game: a 32dp pill, 92% opaque, 16dp from the chosen screen corner, at most 360dp wide, with a 9dp lamp and the same status words as the main window in 10pt Bahnschrift SemiBold. It never takes focus, lets clicks pass through and is left out of screen captures.

## Do's and Don'ts

### Do:
- **Do** give each window exactly one amber key, the action it exists for (Start, Run, Confirm, Apply, OK), and make every other action a grey Mod key.
- **Do** use graphite Alpha keys for options and for small or repeated actions (the number row, the step-edit bar, unchosen segments).
- **Do** show the chosen option as the light cap held down inside a recessed cluster.
- **Do** put everything typed in a recessed well (#111215, 7dp) whose edge turns amber (1.5dp) while it is being edited.
- **Do** put live numbers, status and messages on display glass (#0B0C0E in an 11dp bezel), with a lamp for state.
- **Do** open every group with a silkscreen label (8pt Bahnschrift SemiBold, uppercase, 1.4dp tracking, #A3A19A) and a hairline (#2D2F35).
- **Do** draw every icon in `Icons.cs` on the 16-unit grid with one round-capped stroke.
- **Do** route every pixel metric through `Theme.Dp`/`Pad`/`Sz` and give every font size in points.
- **Do** keep keyboard operation, a visible amber focus mark and an accessible name and role on every painted control.

### Don't:
- **Don't** put a second amber key in a window or use amber as a fill, text color or decoration.
- **Don't** use coral for anything but stopping or deleting, or green, orange and red for anything but lamps and message text.
- **Don't** raise a group onto a filled, bordered card; groups live on the plate under a silkscreen label, and a hairline frame is only for a region that scrolls.
- **Don't** put a kicker or eyebrow above a heading.
- **Don't** use font glyphs or emoji as icons; they render differently on every Windows build and never match the legends.
- **Don't** set a legend, label or number in Segoe UI, or a sentence in Bahnschrift.
- **Don't** use a blue accent, RGB lighting effects or the brand kit's paper textures and collage art in the interface (the logo appears as itself, in the header).
- **Don't** animate state changes or float panels on drop shadows; depth comes only from the three heights.
- **Don't** set text below Dim Legend's contrast (#A3A19A, WCAG AA on the plate and on key faces).
