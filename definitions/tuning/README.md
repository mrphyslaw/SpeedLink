# Bundled tuning definitions

These twelve XML files are SpeedLink snapshots of the MazdaSpeedDefs definitions,
incorporated under its MIT license (LICENSE in this folder). They
are embedded into the executable. No ROM image or commercial software is bundled.

Source: https://github.com/mrphyslaw/MazdaSpeedDefs

The full upstream definitions at [2882a15](https://github.com/mrphyslaw/MazdaSpeedDefs/commit/2882a15020134bb2fc2e26cc3319eec894504d17)
include separate front wideband and rear O2 fuel-feedback gain and response-period
curves. These snapshots retain SpeedLink's existing help, labels and units for
other maps. Saved tabs reopen the renamed curves by their original address.
On L3CGEE000, the former generic labels pointed to the rear O2 curves.

The editor requires an exact definition identity, the SpeedLink
boot/checksum-layout checks and a program-area signature. Program signatures
cover the recognized program area with defined numeric controls, named switch bits and
the correction word masked out. Table edits remain supported; other program
modifications are rejected. SHA-256 signatures contain no ROM payload. Static axes are display metadata. A
BitwiseSwitch's sizex counts named bits within shared storage, not sequential
data elements. All named bits are exposed individually and neighboring bits
are preserved. See the [editor guide](../../docs/EDITOR.md) for usage and current
limits and the [tuning guide](../../docs/TUNING_HELP.md) for control explanations.

L3CGEE000 retains the supported native addresses and geometry. Its help clarifies
knock-reference arithmetic and encoded injector coefficients. Export verifies the
native code sum and four markers. The six MAP scaling controls are inside the
code sum and use the established correction word on save. Other editable
calibrations are outside that sum. Additional calibration integrity is unestablished.

Update these snapshots deliberately, retain the license, and rerun editor tests
and the local reference-ROM audit after definition changes.

L3M6EG000 includes 263 tuning tables and 600 diagnostic controls. Its native
program sum starts at 0x3F000; automatic correction uses reserved padding at
0xFFFF4 and preserves the instructions at the range start. Its boot fingerprint
normalizes only the established 128-byte per-ECU identification record. That
record is retained byte-for-byte in every exported ROM; the remaining boot bytes
and program/checksum guards remain checked.

L38KEG000 includes the complete 889-control upstream definition from
[e48da53](https://github.com/mrphyslaw/MazdaSpeedDefs/commit/e48da53fc6582fb77ac7a53c9aa24635d9990e81).
It identifies the reported 2007 Canadian Mazdaspeed6 and shares the
established L38KEH000 map geometry and native program region. Its own
complete identity and boot fingerprint are checked before editing or writing.

SpeedLink applies its existing L38KEH000 help, display labels and units to the
matching L38KEG000 controls. Storage addresses, dimensions, scaling arithmetic,
static breakpoints, switch values and checksum support follow the complete upstream XML.

L37JEF000 is the complete Mazda CX-7 2.3 DISI Turbo editor definition from
[cf70470](https://github.com/mrphyslaw/MazdaSpeedDefs/commit/cf704700e7060e0df24b114a473d4c22f8c021f7).
It includes separate in-gear and neutral idle targets, each indexed by current
coolant temperature and coolant temperature at startup. SpeedLink updates selected
help text and display labels. All 892 controls retain the upstream storage, scaling,
axes, and switch settings.
The editor checks its complete calibration identity, boot fingerprint and program
signature. Exports correct the established checksum word at `0x048000` and
verify the durable copy. This profile supports offline editing.
