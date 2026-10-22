# SpeedLink ROM editor

An offline editor for the seven bundled Mazdaspeed/MPS stock ROM layouts, built
into the same Windows app as the reader and logger. No adapter is needed to edit
a file. ECU programming remains disabled in this alpha.

## Room for the map

![A complete synthetic 16×16 map with both axes visible](images/fit-map.png)

**Fit map** (Ctrl+0) gives the table the available workspace. A full 16×16 map
and both axes fit without table scrollbars at 1024×700 and 1280×720 logical
window sizes with app navigation collapsed. Larger maps retain scrolling.
The ☰ button expands or collapses navigation; Ctrl+B hides or restores the
map browser. Use **View** for layout options and floating panels.

Axis names sit directly above the column breakpoints and beside the row
breakpoints, with value units beside the map tabs. The labels stay visible while
scrolling. Selecting cells highlights their row and column headers.

The 16×16 and MAF examples use synthetic layout/curve fixtures. The 16×16
example is a layout test, not an added stock calibration map. VVT screenshots
show an L38KEH000 ROM example. These are interface examples, not tuning advice.

## Long MAF curves

![A 129-point MAF calibration with a voltage/value list beside its curve](images/curve-editor.png)

Search or browse the map list, keep related maps in separate tabs, and edit
numeric cells directly. Curves use the definition's physical axis values.
Long MAF curves use a voltage/value list beside the graph. Click any point to
reach its value, use the wheel to zoom, and Shift+drag to pan. Static breakpoints
stay read-only. Arithmetic and interpolation work across selected points; copy
and paste retain full precision. Axis names and units label the list's two columns.

## Rotatable 3D surfaces

![Intake cam advance table with a compact 3D preview at the upper-right](images/table-preview.png)

**Table with 3D preview** places the surface at the upper-right of the grid,
keeping the table's full height. **Fit map** retains this preview while a full
16×16 grid fits at both tested sizes. The graph stays on the right, like the
MAF editor. Its controls and linked cell selection work the same way.

![An intake cam advance VVT surface showing its raised areas and transitions](images/surface-editor.png)

Axis lines, names and tick values sit directly on the map. Blue is the column
axis, green is the row axis, and amber is the value/height axis. They stay aligned
as you rotate. The preview keeps fewer ticks; the larger graph shows more detail.

Drag to rotate a surface in the direction of your mouse and use the wheel to zoom.
The preview and floating graph use the same controls. Click a point on the
graph to select its grid cell. Resizable panes let you give the grid or graph
more room. **Fit view** frames the entire surface. Pop the graph into a separate
window to use another monitor; closing that window restores the preview.

## Map information and changes

![Map information in a separate window](images/details-editor.png)

**View → Map information…** opens help without reducing the map's height.
Value units and input axes stay visible above the explanation. Help distinguishes
targets and thresholds from multipliers and signed corrections, with simple
examples. [Map-help conventions](../TUNING_HELP.md) explain the common units.
**Storage details** expands address, data type and dimensions; long content can
be scrolled. Cell tooltips and the status strip show comparison values.
Axis titles and breakpoint headers have a distinct blue-gray band and bright
borders separating them from editable data. Read-only MAF breakpoints use the
same treatment.
**View → Changes** opens a drawer
of maps that differ from the selected baseline. Select a map there to jump to it.

The **View** menu supports table and graph above/beside one another, plus
table-only and graph-only views. The browser can also pop into a separate
window. Layout preferences are remembered. Open maps and unsaved edits remain
in place when switching to Logger or Recordings.

## First edit

1. Open **Editor → File → Open ROM…**.
2. Choose a map. Double-click a numeric cell, enter a value and press Enter.
3. Review the original/last-save/comparison values and the curve or surface.
   Use Undo/Redo to restore complete operations.
4. Select **Save copy…**. Every save corrects and verifies the checksum, uses
   a new filename and verifies the written bytes.

For a rectangular selection, the toolbar supports Set, Add, Multiply, copy/paste
and horizontal, vertical or 2D interpolation. Mode switches use named choices;
each named DTC bit is exposed separately while neighboring bits are preserved.
See the [full editing guide](../EDITOR.md) for shortcuts and exact behavior.

## Compatibility and current limits

Supported profiles: **L33XEH000, L35HEH000, L38KEH000, L39SED000, L39SEE000,
L39SEF000 and L3M7EF000**.

The editor checks the calibration, boot, checksum layout and reference program
signature. Changes to defined tuning controls remain editable; other program
modifications are rejected. Extended/patched ROM layouts are unsupported.

Static axes are read-only. This version has no patch installer, smoothing,
tune-project history or log-to-map overlay. Offline software checks do not
establish vehicle behavior or the suitability of tuning changes.

[All documentation](../README.md) · [Logging](../LOGGER.md) ·
[Validation](../../dotnet/docs/Validation.md) · [Download SpeedLink](https://github.com/mrphyslaw/SpeedLink/releases)
