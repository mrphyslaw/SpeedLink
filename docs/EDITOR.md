# SpeedLink ROM editor guide

Edit a supported ROM without an adapter. SpeedLink keeps the original file and
exports each revision as a new file with a corrected, verified checksum.
ECU programming remains disabled.

This guide is also included in the app: choose **File → Editor guide**, or
choose **Help** and select **Editor**. **Map info** beside the map tabs, or
**F1**, opens help for the selected map instead.

## Make your first edit

1. Choose **Editor** in the left navigation, then **File → Open ROM…**.
2. Find a map in the category list or search by name, description or category.
   Click **Map info** to check its units, purpose and correction direction.
3. Edit the control using the instructions below for its shape. Compare with
   the baseline and use Undo/Redo to review or reverse an operation.
4. Choose **Save copy…** or press Ctrl+S. Pick a new filename. Every export
   corrects the checksum and verifies the written bytes.

### Single values, such as rev limits

The value card groups the name, explanation, units, baseline and change.
Click the numeric field, type the value in the displayed units, then choose
**Apply value** or press Enter. **Cancel** or Escape discards the typed value.
**Reset to baseline** restores the selected reference value.

Focusing the field shows full stored precision. Accepting the value unchanged
preserves its bytes. A valid pending value is applied when saving or switching
maps. Invalid or unencodable input stays visible and prevents saving, switching
maps or changing the baseline until corrected or canceled.

Single-state controls use a named selector in the same card. Choosing a state
applies it immediately. These controls do not need a grid or graph.

### Tables and short curves

Double-click a numeric cell, enter a value using a decimal point, and press
Enter. Drag or Shift-click to select a rectangle. Axis names label the column
and row breakpoints directly; their distinct header bands stay visible while
scrolling. The selected cells highlight their row and column headers.
Static breakpoints are read-only.

Small maps use a grid proportional to their contents. A short curve has a curve
preview; a small surface has a 3D preview beside the grid. Multi-state controls
keep labeled rows and a named selector without an unused graph. Select the row
or cell you want to change, then choose its state. Bit controls preserve the
other bits sharing the byte.

### Long curves, including MAF calibrations

Long curves use a vertical breakpoint/value list beside a 2D graph. Only the
value column is editable. Click a graph point to select its list value; selecting
a list value also brings the point into view. Use the wheel to zoom and
Shift+drag to pan. The first and last points remain reachable in a 129-point curve.

Copy includes both columns at full precision. Paste accepts a value column,
matching breakpoint/value rows, or one horizontal row of three or more values.
Supplied breakpoints must match, and the whole paste must fit. Interpolate uses
the physical X-axis values between the first and last selected points.

## Work on a selection

| Operation | Result |
| --- | --- |
| Set | Replace selected values with the amount in the box. |
| Add | Add the amount to each selected value. Use a negative amount to subtract. |
| × | Multiply each selected value by the amount. For example, 1.1 applies 110%. |
| Copy / Paste | Use tab-separated values without headers. Paste starts at the upper-left selected cell and must fit completely. |
| Interpolate | Fill between selected endpoints using physical axis values: Horizontal along X, Vertical along Y, or 2D between four corners. |
| Reset selection to baseline | Restore selected cells from the chosen comparison baseline. |

Undo/Redo restores complete operations and exact prior bytes. The latest 200
operations are retained for the open ROM. Shared-storage controls update together.

Cells display compact numbers. Double-click a cell to see its full stored value;
accepting it unchanged preserves the original bytes. Copy and tooltips retain
full precision. Entered values are quantized to the ROM's storage resolution.
Nonfinite and out-of-storage-range values are rejected; existing invalid values
display as **Invalid**. Storage limits do not establish a suitable tuning range.

## Compare revisions

Use **Edit → Compare ROM…** to load a comparison file with the same calibration.
Choose **Original ROM**, **Last save** or **Comparison ROM** in the baseline
selector. The single-value card has its own baseline selector.

Orange cell borders and browser dots identify differences from that baseline.
Cell tooltips and the selection status show current, baseline and difference
values. **Changed maps only** filters the browser. **View → Changes** opens a
list of changed maps; select an entry to jump to it. These are comparison views,
not a record of the order in which edits were made.

## Arrange the workspace

Open maps stay in tabs when you change maps or switch to Logger, Recordings or
another workspace. Numbered maps are sorted naturally in the browser. Tabs
scroll horizontally and keep the active map visible; use the mouse wheel over
the tab strip to reach other tabs. Closing a tab with × keeps the ROM's edits.

Use the app's **☰** button to collapse navigation to icons. **View → Hide map
browser** (Ctrl+B) hides or restores the map list. Drag its edge to change its
width, or pop it into a separate window.

**Fit map** (Ctrl+0) restores automatic sizing and hides the browser, Changes
drawer and map-information window. With navigation collapsed, a full 16×16 map,
both axes and a compact preview fit at 1024×700 and 1280×720 logical window
sizes. Larger maps scroll at readable cell sizes; long curves retain their
list/graph layout. Windows display scaling affects the available logical size.

| View option | Use it for |
| --- | --- |
| Table with 3D preview / Table with curve preview | A compact graph at the right while keeping the full table height. The label follows the selected map. |
| Table above graph | A larger graph beneath the table, separated by a draggable horizontal divider. |
| Table beside graph | Side-by-side panes with a draggable vertical divider. Long MAF curves use this layout. |
| Table only / Graph only | Give the selected view the available space. |

Drag the divider at the table's right edge to resize the preview. Its width is
remembered across launches and graph docking. **Fit map** resets that custom
width to automatic sizing. Small-map previews stay directly beside their grids.
Graph options are disabled for controls without a curve or surface.

Use the graph's **↗** button or **View → Pop out graph** for a larger floating
view. **Dock graph** or closing the floating window returns it to the workspace.
The map browser has equivalent pop-out/dock commands. Floating panels can move
to another monitor. Navigation, browser visibility/width, curve-list width,
preview width, view, split, Fit map sizing and floating-panel bounds are saved
beside the EXE in `data/editor-layout.json`.

### Read the graph

Drag a 3D surface to rotate it in the direction of the mouse; use the wheel to
zoom. The on-map rulers identify the column axis in blue, the row axis in green
and value/height in amber. Labels and tick values stay aligned while rotating.
Compact previews show fewer ticks than larger graphs.

Click a curve point or surface vertex to select its table value. Graph dragging
changes the view, not the ROM value. **Fit view** restores the camera; compact
previews use a **↺** button with the same function. Fit view changes the camera;
Fit map changes the workspace layout.

## Understand the active map

Click **Map info**, press F1, or choose **View → Map information…**. The same
window comes forward and follows the active tab without reducing table height.
It is unavailable until a map is selected.

Value units and input axes appear above the tuning explanation. Descriptions
distinguish targets, limits, multipliers and signed corrections, with examples
where established. Unconfirmed physical units are identified explicitly. Expand
**Storage details** for the address, type and dimensions. Imported definitions
supply their own descriptions and units. See [map-help conventions](TUNING_HELP.md)
for examples of interpreting corrections and coefficients.

## Save a revision

**Save copy…** writes a new binary, flushes it and verifies its bytes. It refuses
to overwrite the original or any existing file. Pick a new filename for every
revision. Every export corrects and verifies the checksum; there is no opt-out.
Checksum correction is undoable if it changes bytes. The Original ROM baseline
always remains the image first opened in this session.

Use a descriptive filename and keep the current ECU backup separately. Closing
SpeedLink with unsaved edits prompts before discarding them. There is no saved
tune-project format or recovery of unsaved work in this version.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open ROM. |
| Ctrl+S | Save a new copy. |
| Ctrl+0 | Fit map. |
| Ctrl+B | Hide/restore the map browser. |
| F1 | Open/restore help for the active map. |
| Ctrl+Z / Ctrl+Y | Undo/Redo ROM operations when a text field is not being edited. |
| Ctrl+C / Ctrl+V | Copy/paste selected table values; in a focused text field, edit its text. |
| Enter / Escape in a single-value field | Apply / cancel typing. |

Layout shortcuts apply when you are not typing in a text field.

## Compatible files and definition updates

Supported stock layouts: **L33XEH000, L35HEH000, L38KEH000, L39SED000,
L39SEE000, L39SEF000 and L3M7EF000**. Opening requires a complete 1 MiB image
with matching calibration, boot, checksum layout and program-area signature.
The program check allows defined tuning controls and checksum changes while
rejecting other program changes. Patched/extended layouts are unsupported.
Recognizing a calibration does not prove factory provenance or vehicle fit.
Unknown images can still be dumped with the reader and inspected in ROM checks.

Use **Definitions → Import…** to install an editor XML update for a supported
layout. Save open work before reopening the ROM to use it; importing does not
change existing edits or their definition. [Definition import](DEFINITIONS.md)
explains supported files and restoring a bundled copy.

Axis editing, patch installation, smoothing, tune-project history and log-to-map
overlays are not included. Offline checks do not validate engine behavior.

[Editor screenshots](editor/README.md) · [Documentation index](README.md)
