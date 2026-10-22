# SpeedLink ROM editor

This first editor supports the seven bundled MazdaSpeedDefs stock layouts.
No adapter is needed. ECU programming remains disabled. Existing reading and
logging functions are unchanged.

Use **Definitions → Import…** to install an editor XML update for a supported ROM.
Reopen the ROM to use the update; existing edits stay with their current definition.
See [definition import](DEFINITIONS.md) for supported files and bundled fallback.

## Open and browse

Open **Editor**, then **File → Open ROM…**. A complete
1 MiB image with a matching calibration, boot, checksum layout and program-area
signature is required. The program signature allows edits to the defined controls
and checksum word while rejecting other program changes. Patched/extended ROMs
are unsupported, even when they retain a stock calibration ID.
Unknown images can still be dumped with the reader. A recognized calibration
does not establish a file's factory provenance or vehicle compatibility.

Expand a category or search by map name, description or category. Numbered maps
are sorted naturally. Each selected map opens in a tab; use × to close its tab.
The map-information window explains the control in tuner terms. Static axes are display
metadata and cannot be edited. No extended-table patch is installed by this editor.

## Edit and view

Column-axis names sit directly above their breakpoints; row-axis names run
beside the row breakpoints. Labels stay visible when the table scrolls. Selected
cells highlight their row and column headers, and value units sit beside the map
tabs. Scalar and switch controls omit unused axis labels. Long MAF curves keep
their directly labeled breakpoint/value columns.

- Double-click a numeric cell, type a value using a decimal point, and press Enter.
- Drag or Shift-click to select a rectangle. Set, Add and × apply the amount in
  the adjoining box. Use a negative Add amount to subtract.
- Copy and Paste use tab-separated values without headers. Paste starts at the
  upper-left selected cell; it must fit completely. Spreadsheet values are accepted.
- Interpolation uses the physical axis values and selected endpoint cells.
  Horizontal works along X, Vertical along Y, and 2D uses the four corners.
- Switches use the named selector. Bit controls alter only their selected bit.
- Undo/Redo restore entire operations, including exact prior bytes. The latest
  200 operations are retained for the current open ROM.
- Curves and surfaces reflect the current values. Click a curve point or surface
  vertex to select its grid cell. Drag a surface to rotate it with your mouse,
  use the wheel to zoom,
  and use **Fit view** to restore the camera. Curves support wheel zoom and
  Shift+drag panning; selecting a list value brings that point into view.

3D maps carry their axis lines, labels and tick values directly on the surface
frame. Blue identifies the column axis, green the row axis, and amber the map's
value/height axis. The rulers stay aligned while you rotate. The compact preview
uses fewer tick labels; the floating graph shows more detail.

Long curves, including the 129-point MAF calibrations, use a vertical breakpoint
and value list alongside the graph. Only the value column is editable. Copy
includes both columns at full precision; paste accepts a value column, matching
breakpoint/value rows, or a horizontal row of three or more values. Breakpoints
must match when supplied, and the complete paste must fit. Interpolate operates
along the physical X axis between the first and last selected points.

Cells use compact numbers for readability. Double-click to see the full stored
value; accepting it unchanged preserves the original bytes. Cell tooltips show
full current, baseline and difference values. Copy also retains full precision.

Values are quantized to their actual storage resolution. Nonfinite values and
values outside encodable storage are rejected. Existing invalid values display
as Invalid. Storage bounds are not a recommendation for safe engine operation.
If two controls share storage, editing one updates every view of those bytes.

## Compare and save

Choose Original ROM, Last save or a separately loaded same-calibration Comparison
ROM. Orange cell borders and browser dots show differences from that baseline.
Tooltips and the compact selection status show reference values and differences.
Changed maps only filters the browser. **Edit → Reset selection to baseline**
restores the selected cells from that baseline.

**Save copy…** writes a new binary file, flushes and verifies its bytes, and refuses
to overwrite an existing file or the original. Choose a new filename for every
revision. Every save corrects the checksum and verifies it against the written
bytes. There is no checksum opt-out. Checksum correction is recorded as an undoable operation if
it changes bytes. The original ROM baseline never changes during the session.

Ctrl+S saves a new copy. Ctrl+Z / Ctrl+Y undo/redo table operations when a text
field is not being edited. Ctrl+C / Ctrl+V copy/paste the selected numeric cells.

This version does not include axis editing, patch installation, graph dragging
to change values, tune-project metadata, smoothing or log-to-map overlays.
Those are separate follow-up features. Validate tuning decisions with suitable
vehicle data; offline editor validation cannot verify engine behavior.

## Workbench layout

Choose **Editor** in the left navigation. Your open ROM, map tabs and edits stay
in place when you switch to Logger, Recordings or another workspace.

The ☰ button expands or collapses the app navigation. **Fit map** (Ctrl+0) hides
the map browser and changed-maps drawer and shows the complete table. With
navigation collapsed, a 16×16 map and both axes fit at 1024×700 and 1280×720
logical window sizes without table scrollbars. Larger maps retain scrolling;
the editor keeps readable cell sizes. Long curves keep their list/graph view.

**View** offers table above graph, table beside graph, table only and graph only.
**Table with 3D preview** keeps a small, rotatable surface at the upper-right of
the grid. **Fit map** uses this view for 3D tables, preserving the grid's full
height. The MAF layout keeps its value list on the left and curve on the right.
The preview shares the graph's fit-view and pop-out controls. Docking the graph
returns it to the preview; choose another View layout for a larger docked graph.
Drag the divider to resize the panes. **Hide map browser** (Ctrl+B) gives the
map more room; use the same command to restore it. Both the map browser and
graph can pop into separate windows, with dock commands in **View**. Closing
a floating window docks it again. Floating panels move between monitors.

Choose **View → Map information…** for tuner help in a separate window that
does not reduce the table's height. Value units and input axes are always shown
at the top of that window. Descriptions explain whether values are targets,
limits, multipliers or added/subtracted corrections, with sign and neutral-value
examples where established. Unconfirmed physical units are identified explicitly.
Expand **Storage details** for address, type and dimensions.
The axis-label bands and breakpoint headers use a blue-gray background with
bright separators; editable cells retain their value shading. The read-only
breakpoint column in long MAF curves uses the same visual treatment.
Dark scrollbars match the workspace. Axis bands stop before the scrollbars,
leaving their corners clear when a table needs horizontal or vertical scrolling.
The status strip and cell tooltips provide selected-value comparisons.

Navigation, browser visibility/width, curve-list width, table/graph view and
split, Fit map sizing, and floating panel positions are saved in
`data/editor-layout.json`. Off-screen saved windows reopen near the app.

**View → Changes** opens a list of maps that differ from the selected baseline. Select
an entry to jump to that map. The original ROM and every existing saved copy
remain protected; every new editor save corrects and verifies its checksum.
