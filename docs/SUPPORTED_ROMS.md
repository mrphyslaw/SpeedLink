# ROM checks and compatibility

Definitions are optional for ECU reading. The reader can dump unknown calibration
IDs using the shared Mazda MZR DISI read protocol and the current 1 MiB range.
This list controls **optional file inspection and checksum correction**, not read
permission. Unknown images can be backed up and reopened normally.

## Known inspection layouts

| Calibration | Vehicle family | Checksum start | Correction word |
| --- | --- | --- | --- |
| L3P4EM000 | Mazda 6 MPS (2006 Australia) | 0x048000 | 0x048000 |
| L38KEH000 | Mazdaspeed6 / Mazda 6 MPS | 0x048000 | 0x048000 |
| L39SED000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L39SEE000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L39SEF000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L3M7EF000 | Mazdaspeed6 / Mazda 6 MPS | 0x03F000 | 0x0FFFF4 |
| L35HEH000 | Mazda3 MPS | 0x048000 | 0x048000 |
| L33XEH000 | Mazdaspeed3 / Axela | 0x048000 | 0x048000 |

These layouts correspond to the published or locally prepared MazdaSpeedDefs
definitions. File checks use each ROM's identity, boot fingerprint and checksum
layout. A different or modified layout can still be captured as a raw dump; its
checksum is not guessed. Read success does not require a passing known-ROM check.

## Stock-ROM logging

All eight layouts above also have a logger profile. L38KEH000 and the three
L39S variants have 38 parameters; L3M7EF000, L33XEH000 and L3P4EM000 have 35; L35HEH000 has 36.
Each profile has independent ROM evidence and its own MAP calibration bindings.
Some older ROMs do not expose every baseline identifier through the checked path.
See [coverage and exclusions](../definitions/logging/All_ROM_Coverage.md).
All real-ECU logging checks remain pending. Unknown ROMs remain readable.

## Validation limits

File-layout checks can be tested against reference images and synthetic data.
The shared reader has been tested through real adapters against a simulated ECU.
A user has reported a successful Mongoose ECU dump; formal adapter and
vehicle qualification remains incomplete. A definition's existence does not establish
on-car read compatibility or authorize flashing. Live programming stays disabled.
