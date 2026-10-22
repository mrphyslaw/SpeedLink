# Adapter status and testing candidates

Hardware support is separate from ROM/profile coverage. Bench qualification uses
simulated ECU replies over real adapters; real Mazda ECU acceptance remains
pending. ECU access is read-only and flashing is disabled.

| Adapter | SpeedLink status |
| --- | --- |
| Vgate vLinker FS USB | Available; isolated-bench reader and logger checks passed |
| Tactrix Openport 2.0 | Available through its installed J2534 driver; isolated-bench reader and logger checks passed |
| Mongoose-Plus ISO/CAN3 | Next J2534 testing candidate; not enabled or qualified in the released app |

## Mongoose-Plus ISO/CAN3

This is a candidate for reusing the existing J2534 reader/logger transport.
Being a J2534 device does not by itself establish SpeedLink compatibility.

The current driver picker accepts registered Openport/Tactrix entries. A normal
Mongoose installation will not pass that name/vendor filter, and the connection
status and journals still identify the transport as Tactrix. Selecting Tactrix
in the current release does not enable Mongoose support.

The next implementation step is a registered-driver selection path for this
candidate, with capability checks and an explicit unverified-device status.
It must preserve the qualified Tactrix path and report the actual selected
adapter and driver in captures and journals. The application must continue to
load registered manufacturer libraries rather than DLLs supplied with ROM files.

For the existing Windows x86 app, qualification requires a compatible 32-bit
J2534-1 04.04 driver exposing ISO 15765. A 05.00-only entry is not interchangeable
with the existing binding. Use the manufacturer's driver package; drivers are
not bundled with SpeedLink.

## Qualification checklist

1. Record the exact model, connection type, firmware, driver/API versions and
   registered driver name. Confirm DLL architecture and ISO 15765 capability.
2. Check library loading, version reporting, open/close and cleanup without
   connecting an ECU. Preserve existing driver selection behavior.
3. On the isolated bench, check 500 kbit/s, 11-bit CAN on the standard OBD pair,
   request/response IDs 0x7E0/0x7E8, flow-control filtering and frame padding.
4. Exercise identification and short reads, then full-size multi-frame replies
   and two matching 1 MiB ROM reads. Check cancellation, negative/pending replies,
   timeouts, USB removal/reconnection and fresh-session recovery.
5. Exercise sustained logger requests and recording through the same transport.
   Measure rates and response timing; do not assume another adapter's settings.
6. Obtain real-ECU reader/logger acceptance before describing that combination
   as vehicle validated. Adapter failures must not trigger automatic resends.

No Mongoose hardware has been tested with SpeedLink yet. This checklist does
not enable ECU programming or qualify other Mongoose variants.

Manufacturer references: [driver downloads](https://opusivs-au.com/support/downloads/)
and [J2534 driver discovery](https://opusivs-au.com/support/oem-customer-support/how-to-program-with-opus-ivss-passthru-sae-j2534-dll/discovery-of-the-dll/).
