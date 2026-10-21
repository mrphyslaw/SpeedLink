# SpeedLink ROM editor

This first editor supports the seven bundled MazdaSpeedDefs stock layouts.
No adapter is needed. ECU programming remains disabled. Existing reading and
logging functions are unchanged.

Use **Definitions → Import…** to install an editor XML update for a supported ROM.
Reopen the ROM to use the update; existing edits stay with their current definition.
See [definition import](DEFINITIONS.md) for supported files and bundled fallback.

## Open and browse

Open **Editor**, then **Open ROM**. A complete
1 MiB image with a matching calibration, boot, checksum layout and program-area
signature is required. The program signature allows edits to the defined controls
and checksum word while rejecting other program changes. Patched/extended ROMs
are unsupported, even when they retain a stock calibration ID.
Unknown images can still be dumped with the reader. A recognized calibration
does not establish a file's factory provenance or vehicle compatibility.

Expand a category or search by map name, description or category. Numbered maps
are sorted naturally. Each selected map opens in a tab; use × to close its tab.
The information pane explains the control in tuner terms. Static axes are display
metadata and cannot be edited. No extended-table patch is installed by this editor.

## Edit and view

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
  vertex to select its grid cell. Drag a surface to rotate, use the wheel to zoom,
  and use Reset view to restore the camera.

Values are quantized to their actual storage resolution. Nonfinite values and
values outside encodable storage are rejected. Existing invalid values display
as Invalid. Storage bounds are not a recommendation for safe engine operation.
If two controls share storage, editing one updates every view of those bytes.

## Compare and save

Choose Original ROM, Last save or a separately loaded same-calibration Comparison
ROM. Orange cell borders and browser dots show differences from that baseline.
Tooltips and Selected cell show reference values and differences. Changed maps
only filters the browser. Reset restores the selected cells from that baseline.

Save new copy writes a new binary file, flushes and verifies its bytes, and refuses
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

**Focused** hides the map browser to give the current map more room. The view
picker offers **Split below**, **Split beside**, **Table only** and **Graph only**.
Choose **Details** for tuner help and the selected cells' current value, baseline
and difference. Expand **Storage details** for address, type and dimensions.
Units and axis names always stay beneath the map title.

**Changes** opens a list of maps that differ from the selected baseline. Select
an entry to jump to that map. The original ROM and every existing saved copy
remain protected; every new editor save corrects and verifies its checksum.
