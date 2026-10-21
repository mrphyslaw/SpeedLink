# SpeedLink ROM editor

An offline editor for the seven bundled Mazdaspeed/MPS stock ROM layouts, built
into the same Windows app as the reader and logger. No adapter is needed to edit
a file. ECU programming remains disabled in this alpha.

## Grid and 2D curves

![Injector base pulsewidth table and its 2D curve in SpeedLink](images/curve-editor.png)

Search or browse the map list, keep related maps in separate tabs, and edit
numeric cells directly. Curves use the definition's physical axis values.
Use **Details** for the map's purpose and selected-cell comparison. Units and
axis names remain visible beneath the title.

## Rotatable 3D surfaces

![Overboost protection table and its rotatable 3D surface in SpeedLink](images/surface-editor.png)

Drag to rotate a surface and use the mouse wheel to zoom. Click a point on the
graph to select its grid cell. Resizable panes let you give the grid or graph
more room. Screenshots show the L38KEH000 reference image; they are examples of
the editor, not recommended tuning values.

## Map information and changes

![Workbench map details with tuner help and selected-cell comparisons](images/details-editor.png)

**Details** opens the panel beneath the map. **Storage details** expands address,
data type and dimensions; long content can be scrolled. **Changes** opens a drawer
of maps that differ from the selected baseline. Select a map there to jump to it.

Use **Focused** to hide the browser. The view picker supports table and graph
below or beside one another, plus table-only and graph-only views. Open maps
and unsaved edits remain in place when switching to Logger or Recordings.

## First edit

1. Open **Editor → Open ROM**.
2. Choose a map. Double-click a numeric cell, enter a value and press Enter.
3. Review the original/last-save/comparison values and the curve or surface.
   Use Undo/Redo to restore complete operations.
4. Select **Save new copy**. Every save corrects and verifies the checksum, uses
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
