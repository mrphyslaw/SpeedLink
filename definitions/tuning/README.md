# Bundled tuning definitions

These seven XML files are unchanged snapshots from the local MazdaSpeedDefs
repository, incorporated under its MIT license (LICENSE in this folder). They
are embedded into the executable. No ROM image or commercial software is bundled.

Source: https://github.com/mrphyslaw/MazdaSpeedDefs

The first editor requires an exact definition identity, the existing SpeedLink
boot/checksum-layout checks and a program-area signature. Program signatures
cover the checksum region with defined numeric controls, named switch bits and
the correction word masked out. Table edits remain supported; other program
modifications are rejected. SHA-256 signatures contain no ROM payload. Static axes are display metadata. A
BitwiseSwitch's sizex counts named bits within shared storage, not sequential
data elements. All named bits are exposed individually and neighboring bits
are preserved. See ../../docs/EDITOR.md for usage and current limits.

Update these snapshots deliberately, retain the license, and rerun editor tests
and the local reference-ROM audit after definition changes.
