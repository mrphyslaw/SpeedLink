# Alpha validation scope

## Complete ECU calibration identities — 0.5.5-alpha.5

- Regression cases reproduce the former rejection of complete ECU identities
  for all eight profiles, including `L3P4EM000L3T6051`.
- Known short and complete IDs now select the same logger. Unknown build
  suffixes, truncated IDs, different profiles and ambiguous IDs remain rejected.
- The complete suite passes 369 unit cases. Sixteen offline desktop workflows
  bind the actual profile ROM with its complete ECU identity and exercise
  recording/stop and connection-failure handling. Source ROMs remain unchanged.
- These checks use simulated live replies. Real ECU logger values and rates
  remain pending; no new physical adapter or ECU test is claimed.

## L3P4EM000 support — 0.5.5-alpha.4

- Eight bundled editor/logger profiles. L3P4EM000 adds 895 maps and controls,
  35 logger parameters, its identity/boot/program guards and checksum layout.
- Its original bounded logger encoders and response-copy routine passed
  277 vectors and 217 independent scalar checks with synthetic RAM.
- The complete suite has 361 unit cases; all eight profiles are included
  in editor, layout, Workbench, import and logger UI checks.
- Existing physical bench results below cover the original seven profiles.
  No new adapter traffic or ECU logging is claimed for L3P4EM000.

## C# logger and saved-log viewer — v0.3.0-alpha.2

Date: 2026-10-19. Live programming remains disabled. Real ECU logging has not
been tested; all 258 channel/profile combinations retain their ECU-pending status.

The local offline suite passed 165 C# tests, including all 52 original parity
cases. The unchanged seven logger profiles retain their 2,030 native-derived
vectors. Fourteen desktop recording/failure workflows cover all seven profiles.
Replay checks cover CSV quoting and incomplete tails, malformed data, sample
ages, gaps, extrema preservation and round trips from every profile's recorder.
The viewer was exercised and visually inspected at normal and minimum size.
Seven developer ECU-peer parser regression cases also passed.

Physical logger qualification used the vLinker FS and Tactrix Openport 2.0 on an
isolated, powered, terminated bench. Each adapter acted as the reader while the
other carried simulated ECU replies. This exercised all seven profiles in both
directions, unsupported identifiers, transient negative responses, delayed
replies, short replies and missing replies. Each direction completed a sustained
1,020-query run. The actual desktop connect/start/stop controls and saved recording
were also exercised with each adapter. Raw client/peer traces and recorded values
were checked independently. Exploratory fixture failures were retained and their
causes corrected before successful repeat runs.

Physical USB interruption/reconnect checks passed for both readers. Each stopped
without retrying and retained the partial recording, then completed 340 fresh
queries after reconnection with clean shutdown. The final independent audit
matched 5,402 queries and decoded 4,443 recorded values. Cleanup warnings caused
by USB removal remain recorded; the fresh connections closed without warnings.

The saved-log viewer opens existing SpeedLink wide CSV files without an adapter.
Its checks verify synchronized selection, cursor/zoom, six-plot limit, bounded
loading, visible gaps, stale-value handling and unchanged source files. The single
EXE receives separate startup/resource checks for all seven ROM bindings and a
saved-recording viewer check. CI repeats offline checks; physical bench tests
are local and cannot run on a GitHub-hosted runner.

Bench rates describe the adapters and test peer. They do not establish actual
ECU response times, variable refresh rates, vehicle compatibility or road-use
reliability. Native code evidence does not establish sensor accuracy on a vehicle.
See [logger protocol research](LOGGER_PROTOCOL.md) and
[per-ROM pending ECU checks](../definitions/logging/All_ROM_ECU_Checklist.csv).

For current editor and Workbench checks, see [C# validation](../dotnet/docs/Validation.md).
