# Engine diagnostics

Open **ECU → Diagnostics**, or use **Ctrl+8** (assignable under
**Tools → Keyboard shortcuts**). The shared adapter dropdown at the top contains
the COM port/baud or registered J2534 driver and **Refresh devices**. Choose
**Connect & read codes** to connect, identify and scan without leaving Diagnostics.
**Connect / Disconnect** stays available in the top bar in every section.
No ROM file or backup is required for standard OBD diagnostics.
If an older installation already assigned Ctrl+8 to another action, SpeedLink
preserves that binding. Assign a different Diagnostics shortcut in the settings.

## Read codes

After a ROM read, **Reconnect & read codes** opens a fresh connection and scans
from this page. Once connected, choose **Read codes** to refresh the engine ECU's stored, pending and permanent
emissions-related fault codes, check-engine light state and readiness monitors.
Unsupported services, rejected requests and communication failures are displayed
separately. A missing reply never means there are no faults.

The freeze-frame tab shows frame 0's trigger code and the supported basic
parameters captured by the ECU when that fault occurred. These are historical
values. Unsupported parameters are omitted or identified explicitly.

Descriptions come from the matching bundled Mazda definition where available.
Unknown codes remain visible with their code number. A code description identifies
the ECU's reported condition; it does not establish which component needs repair.

## Clear codes

Keep ignition on and the engine stopped. Stop logging before clearing.
SpeedLink asks for confirmation, reads the current diagnostic information and
saves a before-clear report under `data/obd`. It checks that engine speed is zero,
sends the clear request once and waits for the ECU's acknowledgement. It then
reads the diagnostic information again and retains an after-report.

Clearing removes stored/pending emissions-related diagnostic information and
freeze-frame data and resets readiness monitors. An existing fault can return.
Permanent codes, where supported, remain until the ECU's own diagnostic monitors
confirm the fault has been repaired. They cannot be erased by this clear command.

If the acknowledgement is missing or malformed, SpeedLink reports an unknown
outcome. Reconnect and read codes to check current status. The clear command is
never automatically repeated. Cancellation after acknowledgement does not undo
clearing; read again if the following scan was stopped.

## Reports and operation limits

**Save report…** creates a new JSON report without overwriting an existing file.
Reports may contain VIN/chassis ID and calibration information; review them before
sharing. **Open reports** opens the local folder containing clear-operation reports.
Starting a new adapter connection clears the displayed scan so results from a
previous ECU are not attributed to the new connection. Saved reports remain intact.

Only one adapter operation runs at a time. Reading, logging, flashing and recovery
cannot run concurrently with diagnostics. Disconnect and reconnect after a ROM
read to return to normal diagnostic operation.

This page addresses the engine ECU at CAN 7E0/7E8, 500 kbit/s. ABS, airbag and
other module scans are outside this feature. Gen 1 and Gen 2 OBD services are
independent of the Gen 1-only flashing restriction; actual supported services
depend on the ECU and market variant. Mongoose uses the experimental J2534 path.

## Protocol references

- [ELM Electronics ELM327 datasheet](https://www.elmelectronics.com/wp-content/uploads/2016/07/ELM327DS.pdf), OBD commands, trouble codes and CAN response formats.
- [OBD Solutions service modes](https://www.obdsol.com/knowledgebase/obd-software-development/obd-services-modes/).
- [Permanent diagnostic trouble codes](https://www.bar.ca.gov/obd-test-reference), California Bureau of Automotive Repair.
