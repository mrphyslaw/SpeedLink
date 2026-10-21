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

Screenshots use synthetic layout/curve fixtures, not vehicle tuning values.
The 16×16 example is a layout test, not an added stock calibration map.

## Long MAF curves

![A 129-point MAF calibration with a voltage/value list beside its curve](images/curve-editor.png)

Search or browse the map list, keep related maps in separate tabs, and edit
numeric cells directly. Curves use the definition's physical axis values.
Long MAF curves use a voltage/value list beside the graph. Click any point to
reach its value, use the wheel to zoom, and Shift+drag to pan. Static breakpoints
stay read-only. Arithmetic and interpolation work across selected points; copy
and paste retain full precision. Units and axis names stay above the table.

## Rotatable 3D surfaces

![A 3D surface in its separate graph window](images/surface-editor.png)

Drag to rotate a surface and use the mouse wheel to zoom. Click a point on the
graph to select its grid cell. Resizable panes let you give the grid or graph
more room. **Fit view** frames the entire surface. Pop the graph into a separate
window to use another monitor; closing that window docks it again.

## Map information and changes

![Map information in a separate window](images/details-editor.png)

**View → Map information…** opens help without reducing the map's height.
**Storage details** expands address, data type and dimensions; long content can
be scrolled. Cell tooltips and the status strip show comparison values.
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
