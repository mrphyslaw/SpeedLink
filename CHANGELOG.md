# Changelog

## v0.5.5-alpha.5 — logger calibration matching

- Recognize known complete ECU calibration identities as well as short software
  IDs, including L3P4EM000L3T6051. Fix the false ROM mismatch when loading a
  matching ECU backup for logging. The ROM filename does not affect matching.
- Keep unknown build suffixes, different profiles and ambiguous IDs rejected.
  Show the selected ROM profile and reported ECU calibration when they differ.
- Add full-identity regression checks across all eight profiles and exercise
  complete IDs in the desktop logger checks. Logger parameters and ROM editing
  definitions are unchanged; live ECU logging validation remains pending.

## v0.5.5-alpha.4 — L3P4EM000 editor and logger

- Add the 2006 Australian Mazda 6 MPS L3P4EM000 stock-ROM definition: 895 maps
  and controls, ROM identity/boot/program checks and mandatory checksum correction
  on export. Existing definitions remain available.
- Add 35 stock logger parameters with matching ROM code guards, MAP calibration
  bindings and 277 native-derived vectors. Three unavailable baseline parameters
  are excluded. Live ECU logging and rate validation remain pending.
- Extend the editor/logger/import checks and coverage checklist to eight profiles.

## v0.5.5-alpha.3 — backup progress

- Show prominent backup progress on Connection, including the current pass,
  bytes read, overall percentage, elapsed time and estimated time remaining.
- Estimate from measured transfer speed across both passes. Keep verification
  below 100% until the backup and its completion records have been saved.
- Retain the last progress on Stop or failure, clearly marked not verified.

## v0.5.5-alpha.2 — J2534 receive compatibility

- Preserve validated messages returned with timeout-like J2534 statuses instead
  of treating every such return as empty. Keep fatal errors and malformed replies
  fatal, bounded response waits and no automatic request resends.
- Use the complete reported ISO15765 message length and record inconsistent
  extra-data indexes rather than truncating the diagnostic reply.
- Include returned-message counts, timeout-with-data counts and native-call
  timings in support diagnostics. Mongoose hardware validation remains pending.

## v0.5.5-alpha.1 — diagnostic reports

- Capture persistent, bounded diagnostics before the adapter opens: setup,
  requests, timing, errors, J2534 receive statistics and factory seed/key access.
- Export a support ZIP with a readable preview and session picker, including
  failed sessions after restarting. Personal information is redacted; ROM bytes
  stay in backups. Keep backup journals unchanged and export a sanitized copy.
- Add a reporting guide in Help and a guided GitHub problem-report form.
- Retain read-only ECU access, existing deadlines and no automatic resends.

## v0.5.4-alpha.10 — updated user guides

- Reorganize the editor guide around first edits, single values, small tables,
  long MAF curves, selections, comparisons, layout and checksum-verified exports.
- Explain preview resizing, Fit map versus Fit view, Map info and shortcuts.
- Align the included Getting started and Logger help with workspace navigation.
- Refresh the documentation index, editor overview, About text and release status.
- Retain the existing editor, definitions and ECU-access behavior.

## v0.5.4-alpha.9 — single values and resizable previews

- Replace the one-cell grid with a dedicated value editor: units, explanation,
  baseline and change together, with Apply/Enter, Cancel/Escape, reset and Undo/Redo.
- Preserve full stored precision, reject invalid or unencodable values inline,
  retain invalid drafts, and commit valid drafts before saving or switching maps.
- Use the same focused layout for single-state selectors. Size small grids and
  multi-state controls to their contents, and improve short-curve preview labels.
- Make the right-hand table/preview divider draggable. Remember its width across
  launches and docking; Fit map restores the automatic compact preview.
- Retain complete 16×16 fitting, long MAF editing and mandatory checksum correction.
- Update the editor guide and screenshots. Definitions and ECU access are unchanged.

## v0.5.3-alpha.1 — table preview

- Keep a compact 3D preview at the upper-right of the map grid, using the same
  right-hand graph position and controls as the MAF editor.
- Retain the full table height and fit a complete 16×16 grid at both tested
  logical window sizes while the preview remains visible.
- Add Table with 3D preview to View and make it the Fit map view for surfaces.
  Popping the graph out and docking it restores the compact preview.
- Use an intake-cam VVT map with clear changes in height for 3D screenshots.
- Prevent scrollbars from keeping one another visible when fitting a table
  after opening or resizing on a narrow desktop.

## v0.5.2-alpha.1 — compact editor

- Collapse app navigation to icons and reduce the editor's top and bottom bars.
- Add Fit map, a resizable/hideable/floating map browser, floating graph and
  separate map-information window. Remember panel sizes, views and positions.
- Fit a full 16×16 table and both axes without scrolling at 1024×700 and
  1280×720 logical window sizes using Fit map with compact navigation.
- Display long MAF curves as breakpoint/value rows beside a curve with zoom,
  pan and linked point selection. Keep static breakpoints read-only.
- Keep full precision during cell editing and clipboard operations; support
  long-curve value-column paste and validated breakpoint/value paste.
- Fit the 3D surface to wide and tall panes. Preserve mandatory checksum saves.

## v0.5.1-alpha.1 — definition import

- Import editor XML and logger JSON updates through the Definitions window.
- Keep imported copies in `data/definitions`, with explicit replacement review,
  version/source details, bundled fallback and retained previous imports.
- Preserve open ROM edits during imports; apply updates when a ROM is next loaded.
- Reject unsupported layouts, protected storage, unrecognized logger requests
  and invalid files while retaining mandatory checksum verification on exports.

## v0.5.0-alpha.1 — Workbench UI

- Added a single-window workspace with vertical navigation and graphite/amber styling.
- Embedded the ROM editor and recording viewer; switching workspaces retains edits.
- Added Focused mode, four grid/graph layouts, collapsible map details and a changed-maps drawer.
- Added up to six live logger plots sharing a 30-second time axis and remembered plot selections.
- Editor saves always correct and verify the checksum; removed the checksum opt-out from the UI and save API.
- Added seven-profile Workbench UI checks and checksum-repair regression tests.
- ECU access remains read-only; real Mazda ECU validation remains pending.

## v0.4.0-alpha.1 — offline ROM editor

- Added definition-driven editing for all seven existing tuning ROM profiles.
- Added searchable categories, naturally ordered related maps, map tabs, resizable
  panes, tuner help, a heat-map grid, 2D curves and rotatable/zoomable 3D surfaces.
- Added named switches and individual DTC bits with preservation of shared bytes.
- Added original/last-save/compatible-ROM comparison, changed-map filtering,
  selection arithmetic, spreadsheet clipboard, physical-axis interpolation,
  byte-exact undo/redo and reset to baseline.
- Saves create a new verified file, with checksum correction and original
  preservation. ECU programming remains disabled. No ROMs are bundled.
- Reject other program modifications with masked program-area signatures; retain
  unknown-ROM reading. Added synthetic editor desktop checks to CI.

## v0.3.0-alpha.2 — 2026-10-19

- Add a saved-log viewer with up to six synchronized plots, cursor inspection and zoom.
- Preserve recorded units, missing-data gaps and narrow peaks/dips during plot reduction.
- Show sample ages and unavailable counts; refuse stale cursor values.
- Read complete rows from an interrupted CSV tail without modifying the recording.
- Retain partial logger frames on cancellation even without a progress callback.
- Add replay parsing, malformed-data, timestamp, gap/spike and seven-profile round-trip tests.
- Document stock logger request scheduling and timing limits.
- Add developer-only physical logger fixtures; real-ECU logging validation remains pending.

## v0.3.0-alpha.1 — 2026-10-19

- Add stock-ROM logging for all seven defined Mazdaspeed/MPS ROMs: 35–38 channels over 31–34 identifiers per profile.
- Check each ROM's native encoder paths, calibration locations and available identifiers; exclude unverified channels rather than copying addresses.
- Select profiles automatically from a matching current ECU backup and calibration identity; clear calibration when switching profiles.
- Add parameter selection, display units, measured host sample rates and a rolling chart.
- Save CSV with per-channel timestamps/status, raw replies and recording metadata.
- Bind MAP calibration from a matching ECU backup and check live calibration identity.
- Keep unsupported readings unavailable and stop on transport failures without retries.
- Add 2,030 native-derived vectors, 146 offline C# tests and recording/fault desktop workflows for all seven profiles.
- Real-ECU logger readings and performance remain pending validation.

## v0.2.0-alpha.2 — 2026-10-19

- Add Tactrix Openport 2.0 ROM reading through J2534 alongside vLinker FS.
- Publish a self-contained Windows x86 EXE with 95 offline tests and isolated adapter qualification.
