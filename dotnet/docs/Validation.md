# C# validation

## Compact editor — 0.5.2-alpha.1 (local candidate)

- 259 C# tests pass, including all seven long MAF curves, clipboard bounds and
  breakpoint checks, atomic failures, exact undo, full-precision no-op edits,
  physical-axis interpolation, and layout settings persistence/recovery.
- Seven desktop layout workflows fit a synthetic 16×16 table at both 1024×700
  and 1280×720 logical window sizes with readable cells, visible axes and zero
  horizontal/vertical table scrolling. This fixture is not a stock tuning map.
- Layout checks exercise information, browser and graph windows, unchanged ROM
  edits while moving panels, fresh-window restoration, surface fit in wide/tall
  panes, and each 129-point MAF table's final point, scaling, interpolation,
  clipboard round trip, full-precision cell entry and checksum-verified export.
- Existing definition imports, Workbench navigation and logger/replay checks
  remain required. These tests use synthetic images and offline replies.

No physical adapter or ECU was accessed for this UI change.

## Definition import — 0.5.1-alpha.1

- 249 C# tests pass, including import persistence and fallback for all seven ROMs,
  stale replacement/concurrent-writer checks, corrupted-file recovery, parser
  bounds, protected storage, logger compatibility checks and corrected exports.
- Seven desktop import workflows cover both definition types, review metadata,
  retained open edits, fresh-window persistence and restoring bundled copies.
- Seven existing Workbench and fourteen normal/fault logger workflows pass.
- The importer does not add new ROM layouts or expand the logger request allowlist.
  Program guards and mandatory checksum correction remain application controlled.

These are offline software checks. No adapter or Mazda ECU was accessed for this
change, and existing ECU validation remains pending.

ECU access is read-only. Offline ROM editing is available.
Real Mazda ECU validation is pending; the physical tests below use two adapters
on an isolated, terminated bench and a RAM ECU model. The adapter roles are
specified for each version.

## 0.3.0-alpha.2 logger qualification and recorded-log viewer

- 165 offline C# tests pass, including all 52 original parity cases. The seven
  unchanged logger profiles retain 2,030 native-derived vectors and 258 channels.
- Fourteen desktop recording/failure workflows cover all seven profiles. Viewer
  checks cover quoted CSV, incomplete final rows, timestamps, sample age, gaps,
  spike preservation, selection limits and recorder round trips for every profile.
- The standalone EXE passes startup and ROM binding for all seven profiles, plus
  a separate saved-recording viewer exercise. Normal and minimum-size plots were
  visually checked; opening the viewer needs no adapter and leaves the CSV intact.
- Physical logger tests used vLinker FS and Tactrix on the isolated bench, each
  taking a turn as the reader. All seven profiles passed in both directions.
- Unsupported identifiers, transient negative responses, delayed replies, short
  replies and missing replies passed in both directions. An uncertain exchange
  stops the logger without retrying. Each reader completed 1,020 sustained queries.
- Actual desktop connect/start/stop and recording/replay checks passed with each
  adapter. Independent audits compare client replies with the peer's CAN trace,
  check request ordering and independently decode recorded values.
- Physical USB removal of each reader stopped the next query without retrying
  and retained the partial recording. After reconnection, each reader completed
  ten full scans (340 queries) with clean driver shutdown. Cleanup warnings while
  USB was absent remain in the evidence. The vLinker returned to its default
  115200 baud on USB reconnection; its original 16 ms latency was verified before
  the next connection. Active logging still uses 2 Mbaud and 1 ms FTDI latency.
- The independent audit matched 5,402 logger queries and decoded 4,443 recorded
  values. Separate desktop audits matched both UI recordings with the CAN peer.
- Seven regression cases cover the developer-only vLinker ECU peer parser. The
  peer now discards a previously consumed monitor line when it accompanies the
  next request; a legitimate repeated request remains distinct. This corrects the
  test fixture and does not change the production reader's retry policy.

These tests qualify the USB/CAN transport and host logger against simulated
responses. All real-ECU channel checks remain pending. Bench throughput varies
with peer timing and does not establish ECU refresh rates or vehicle performance.
See the [protocol findings](../../docs/LOGGER_PROTOCOL.md) and
[logger guide](../../docs/LOGGER.md).

## 0.3.0-alpha.1 stock-ROM logger draft

- All seven ROMs with tuning definitions have logger profiles: 35–38 channels
  sharing 31–34 identifiers per ROM, including calculated boost and injector duty.
- 2,030 native instruction-derived vectors cover decoding and calculations. Native
  execution uses bounded ROM routines with synthetic RAM, not a complete ECU.
- 146 offline C# tests pass, including all 52 baseline regression cases and
  existing reader protections. Logging identifier requests are restricted to the
  bundled catalog; programming and unknown identifier requests remain blocked.
- Offline tests cover hidden dependencies, shared requests, signed values, unit
  conversion, calibration/identity guards, unsupported/transient rejections,
  malformed replies, disconnection, cancellation and calculated-input skew.
- Both adapter transports pass logger framing and negative-response handling
  with test fixtures. Their earlier physical read qualification remains separate.
- Fourteen actual desktop button workflows (record/stop and connection failure
  for each profile) pass full-profile recording, chart updates,
  remembered settings, stop/connection cleanup and injected connection failure.
  A partial failed scan is retained; the failed request is not retried.
- The logger table and recording controls remain accessible at the minimum
  window size. The standalone EXE includes all profiles and logger help.

**No real ECU or physical-adapter logger validation has been performed for this
release.** All 258 ROM/channel ECU checks remain pending. Host sample rates shown by
the app are measured responses, not proof of ECU variable refresh rates. Session
acceptance, physical units/polarity, sensor readings and sustained logging rates
need testing. No modified-ROM logging request, logging patch or ECU programming is added.

See the [logger guide](../../docs/LOGGER.md),
[coverage](../../definitions/logging/All_ROM_Coverage.md) and
[ECU test checklist](../../definitions/logging/All_ROM_ECU_Checklist.csv).

## 0.2.0-alpha.2 Tactrix reader

The new reader uses Tactrix Openport 2.0 through its installed 32-bit J2534
driver. The standalone Windows build is now x86 and also runs on 64-bit Windows.
The vLinker reader remains available. Registered Openport-compatible driver
entries remain listed and selectable.

- 95 passing offline tests, including all 52 baseline regression cases.
- Eight reversed-role physical probes passed, through a 3,968-byte ROM block.
- Production C# identification, 12 blocked requests, 15 read sizes and boundaries,
  and 1,000 repeated exchanges passed with Tactrix as the reader.
- A complete known-image backup passed: both 1 MiB reads matched the source.
- Actual desktop controls passed cancellation, disconnect, reconnect and a
  complete unknown-image backup. Both full unknown-image reads matched; no
  checksum layout was guessed.
- Short, negative and missing memory replies stopped after one request and
  created no verified backup. A pending reply followed by the final data passed.
- The x86 vLinker reader passed its read-size/boundary checks and a complete
  two-pass known-image backup, using Tactrix as the ECU peer.
- The single EXE passed startup, adapter selection, retained driver selection,
  bundled-help checks and fresh-process reopening of both known and unknown
  backups. No Simulator connection or programming is enabled.

The reversed bench uses vLinker FS as a RAM ECU peer. It sends explicit ISO-TP
frames and remains ready for the next tester request while returning its final
response frame. Its serial-command pacing affects bench timing. These results
do not establish read speed or compatibility with a real ECU.

Tactrix USB removal as the reader has not been physically repeated in this
version.

## Earlier 0.2.0-alpha.1 qualification

The following results describe the x64 vLinker reader with Tactrix as its ECU
peer. They are retained separately from the new Tactrix reader's coverage.

## Offline and desktop checks

- 68 passing C# tests, including all 52 baseline regression cases.
- All 25 original storage-failure subcases, plus framing, identity, unknown-ROM,
  checksum, cancellation, and ambiguous-response checks.
- All seven available known ROM layouts inspected and checksum-corrected copies
  verified to change only their correction word. Source images stay unchanged.
- An unknown layout remains readable and has no guessed checksum correction.
- Verified backup fixtures retain their canonical format and journal integrity.
- Self-contained Windows x64 EXE launches, loads its bundled help and notices,
  and reopens a hardware backup in a fresh desktop process.

## FTDI transport qualification

The independent C# D2XX backend passed the fastest-first gate at 2 Mbaud:
three consecutive verified, two-pass 1 MiB backups, followed by 1,000 full-size
3,968-byte reads at varied addresses. The backups include an unknown all-FF
image. No UART line, framing, timeout, data comparison or cleanup error was
reported. Each connection restored its original FTDI latency and returned the
adapter to 115200 baud. Lower rates were not needed after this gate passed.

The three full backups took 198.95, 201.71 and 198.33 seconds on this bench.
The 1,000-read stress test took 367.26 seconds. The independent CAN-side audit
matched all 2,622 qualification requests and accepted response hashes. These
measurements cover USB/CAN transfer and the application's durable backup files;
they do not measure a real ECU's timing or flash programming.

## Physical adapter checks

The measured setup uses vLinker FS firmware 2.3.02, Tactrix Openport firmware
1.16.4819, 500 kbit/s CAN, and a temporary 2 Mbaud host serial link.

| Check | Status |
| --- | --- |
| Known image: two complete 1 MiB reads match source | Passed |
| Unknown image: two complete 1 MiB reads match source | Passed |
| Unreported calibration: full backup still allowed | Passed |
| Fifteen read sizes and end-of-ROM boundaries | Passed |
| Twelve forbidden requests rejected before serial output | Passed |
| Cancellation retains partial captures and incomplete journal | Passed |
| Changed identity stops before session/security entry | Passed |
| Negative and short replies stop without retry | Passed |
| Different pass hashes never seal a verified backup | Passed |
| Lost reply stops once; pending reply followed by final is accepted | Passed |
| One thousand consecutive diagnostic exchanges | Passed |
| Actual desktop buttons: cancel, reconnect, full backup | Passed |
| Abrupt worker termination: fresh process detects incomplete journal | Passed |
| Full desktop read after interruption | Passed |
| vLinker physical USB removal and full read after reconnection | Passed |
| Tactrix physical USB removal and full read after reconnection | Passed, RAM peer restarted |

The protocol cases and desktop controls passed again with the FTDI backend.
The physical vLinker USB removal test also passed with FTDI: the first attempted
read after removal stopped, no retry reached CAN, and no verified backup was
created. Reconnection was followed by the actual desktop cancel/reconnect/full
backup sequence. Its 558 memory replies matched the reference bytes, and driver
settings were restored afterward.

The original process-kill test and Tactrix-side USB removal used the Win32
backend. Fresh full backups after the Tactrix restart passed with FTDI. A peer
restart reloads the reference image and does not establish persistence across
an actual ECU power failure. The final adapter check reported 115200 baud,
16 ms original/restored latency, a closed handle and no driver errors. The
Tactrix RAM peer was stopped cleanly.

The independent Tactrix-side trace is compared with accepted host response
hashes. These tests exercise the actual USB/CAN adapters, not MCU flash memory.
Programming/recovery state-machine cases are offline tests only; the C# app and
its read-only bench client cannot erase, program, reset, or clear DTCs.

## Serial investigation

Initial serial implementations reported overruns and truncated replies. Win32
COM reads also failed at 500 kbaud; reducing the baud rate alone did not resolve
the issue.

Interoperability research identified a direct FTDI stream and a 1 ms latency
setting in the installed commercial application. SpeedLink independently uses
FTDI's public D2XX API, without copying or distributing commercial code. It
loads the installed Windows driver, checks UART line errors, bounds reads and
writes, and restores the captured latency setting when closing. Exact reply
lengths and the two-pass hash comparison remain enabled. The API and latency
changed together, so the successful qualification does not isolate either one
as the sole cause of the earlier failures.

## Reproduce

Run the commands in the main C# README for offline tests and a standalone build.
`tools/Verify-Regression.ps1` checks passing TRX results against the original case
list. The source-only bench clients require an explicit `--isolated-bench` flag
and COM port. Never connect a simulated ECU peer to a vehicle network.

Private captures, VINs, traces and ROM binaries are excluded from the repository.
# Editor validation — 0.4.0-alpha.1

ECU access remains read-only. This editor alpha adds offline editing only.

- 211 C# tests passed, including the previous reader/logger tests and 46 editor
  cases across all seven ROM profiles. Zero skipped or failed tests.
- Independent XML/binary decoding agreed with the editor on all 174,741
  displayed numeric cells and individual named switches across 6,185 controls.
  These local reference images had no nonfinite defined numeric values.
- Every defined control was exercised for no-op preservation, localized edits
  and byte-exact undo. Bit groups changed only the named bits within shared
  storage. Source ROM hashes remained unchanged.
- No-edit binary exports were identical to their inputs. Edited exports had
  valid checksums; injected checksum-word faults were repaired and that repair
  could be undone. Existing-file/original-file overwrites were rejected.
- Standalone desktop acceptance passed for all seven profiles, including direct
  numeric cell commit, arithmetic, named-bit selection, undo/redo, comparison,
  changed-map filtering, grid/curve/surface rendering and no-edit export.
- The existing standalone main/logger startup smoke test passed. No adapter or
  ECU traffic was used for editor validation.

The definitions themselves retain their existing research confidence. These
checks verify software encoding and file behavior, not tuning safety or vehicle
behavior. Axis editing, code patches, smoothing, saved project metadata and
log-to-map overlays are outside this first editor version.

For local desktop checks with your own supported ROM:

```powershell
./publish/SpeedLink.exe --editor-smoke --file C:/roms/L38KEH000.bin --output C:/new-editor-evidence --smoke-report C:/new-editor-evidence/result.json
```

Use a new output directory each time. Developer checks write labeled local test
copies and screenshots. `tools/SpeedLink.EditorAudit` also accepts an independent
golden JSON array (`path`, `maps: [{name, values}]`) and a new evidence directory.
ROMs and lab evidence are excluded from the distributable source.

The release review added masked program-area signatures. A modified-program
reference image retains the stock identity and boot but is now rejected.
Seven synthetic profile tests cover this rejection and reopening valid table
edits. Synthetic editor UI workflows run in CI with no ROM files or hardware.

# Workbench validation — 0.5.0-alpha.1

The single-window Workbench retains the same ROM encoders, program signatures,
transport implementations and logger polling policy. Every editor save now
corrects and verifies the checksum; the save API has no opt-out.

- 218 C# cases include seven no-edit checksum-fault repair tests, one per profile.
- Seven embedded Workbench workflows check unsaved edits across workspace changes,
  the changes drawer, all four grid/graph layouts, Focused mode and minimum sizing.
- Seven standalone editor workflows and fourteen normal/fault logger workflows
  cover all profiles. Recording-viewer and standalone startup checks remain in CI.
- The Workbench retains native automation containers for its workspace contents.
  A manual native-input check verified editing and declining the unsaved-close prompt.
- Live plots have independent selections, capped at six, and a shared 30-second
  time axis. Chart selection never adds channels to the logger polling set.

No Mazda ECU was used for this redesign. Existing physical adapter evidence
covers the earlier transport/logger implementation; it is not a new vehicle or
hardware acceptance test for this UI release.
