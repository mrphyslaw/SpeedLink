# Stock logger protocol and timing

SpeedLink sends one `22 xxxx` read-identifier request at a time over
500 kbit/s ISO-TP, using CAN IDs 7E0/7E8. It reads stock-ROM parameters without
installing a patch or entering the ROM reading/programming session.

## Polling and rates

Selected parameters and the dependencies needed for calculated values form a
sequential polling plan. Shared identifiers are queried once per scan and
decoded into their associated channels. Target scans/s is a requested ceiling
for complete polling rounds, not a guaranteed per-channel refresh rate.

The logger records request timing, per-channel sample timestamps and achieved
host rates. A request/response rate does not establish how often the ECU
updates its underlying variable. Real ECU acceptance and vehicle logging rates
remain pending.

Calculated values require fresh inputs within the configured maximum skew.
Unavailable, malformed, negative, mismatched or stale data remains unavailable;
it is not replaced by a zero reading. Recordings preserve explicit gaps.

## Failures and cancellation

Unsupported identifiers are skipped until reconnecting. Condition rejections
can be queried again on a subsequent normal scan. Malformed responses, timeouts
and adapter failures stop logging without resending the uncertain exchange or
automatically reconnecting. Stop waits for the bounded exchange in progress.

The production transport retains its conservative timing policy. Adapter bench
results and offline decoder checks are recorded separately from real ECU
acceptance. See the [validation guide](../dotnet/docs/Validation.md) for scope.

## Definition and test evidence

Portable evidence is kept with the logger definitions:

- [Profile coverage and exclusions](../definitions/logging/All_ROM_Coverage.md).
- [All-profile ECU checklist](../definitions/logging/All_ROM_ECU_Checklist.csv).
- [Definition format and native validation scope](../definitions/logging/README.md).
- Per-profile `.evidence.json` and `.vectors.json` files in
  [definitions/logging](../definitions/logging/).

These files describe request/decoder validation and remaining ECU checks.
ROM images, private research paths and external software binaries are excluded
from this repository.

## Future scheduling work

Per-parameter minimum intervals may reduce unnecessary polling of slow signals.
Any scheduler change needs to preserve shared-query deduplication, actual sample
timestamps, fresh calculation inputs, explicit gaps and bounded cancellation.
Transport changes require adapter testing and separate real ECU validation.
