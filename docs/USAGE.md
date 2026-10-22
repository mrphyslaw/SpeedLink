# Using SpeedLink

Select **Vgate vLinker FS** or **Tactrix Openport 2.0** and install the
manufacturer's Windows driver. Close other diagnostic software before connecting.

For Tactrix, select its installed driver and choose **Connect & identify**.
No COM port or serial baud selection is needed. Registered Openport-compatible
driver entries remain available in the driver list.

For Mongoose-Plus ISO/CAN3, select its **experimental** option and registered
32-bit 04.04 driver. Reading and logging are available for testing; hardware
validation is pending. Follow the [adapter testing guide](ADAPTERS.md).

For vLinker, select its COM port. The normal initial baud is 115200.
The adapter temporarily uses 2,000,000 baud while reading and normally returns
to 115200 on disconnect. If interrupted, the UI reports any restore error;
select 2000000 to reconnect when that temporary setting remains active.

For vLinker, the C# app uses the installed FTDI driver with 1 ms latency while connected.
It restores the previous latency setting when it closes the connection. After
reconnecting the adapter's USB cable, start with 115200 baud again.

## Reading

The current reader uses the Mazda MZR DISI CAN read sequence: 500 kbit/s,
11-bit request/response IDs 0x7E0/0x7E8, read session 0x87, stock three-byte
seed/key access, and service 0x23. It reads the 1 MiB range 0x000000-0x0FFFFF.
This is a Mazda-family reader, not a universal reader for arbitrary ECUs or sizes.

A recognized calibration, definition, boot hash or valid checksum is **not**
required to dump a ROM. Identification is recorded and checked again before
starting. Engine RPM must be confirmed as zero. Rejected or malformed responses
stop the operation; the app does not guess another protocol or resend requests.

Two complete reads must match before `rom.bin` and `backup.json` are sealed.
The original captures and durable journal are kept, including for unknown ROMs.
Matching reads show consistency, not whether the software is factory stock.

Stop finishes the current exchange and keeps the partial capture and journal.
Reconnect before another hardware read. An incomplete capture is not a verified
backup. Use Saved operations to inspect completed and interrupted work.

## Logging

The **Logger** tab supports seven stock-ROM profiles with 35–38 parameters each.
Load the current ECU backup for MAP calibration, select channels and units,
then start logging. The live calibration ID must match. Logging can run with
the engine running; ROM reads still require the engine stopped.

CSV recordings, raw replies and session details are saved under `data/logs`.
Rates are measured in the application; real-ECU readings and performance remain
untested. See the [logger guide](LOGGER.md) for setup and recording details.

## Offline ROM editing

Open **Editor**. No adapter is needed.
Browse maps, edit grid cells, compare revisions and inspect curves/surfaces.
Save new copy preserves the original and always corrects and verifies the checksum. Editing
requires one of the seven supported stock program layouts.
See the [editor overview](editor/README.md) and [editing guide](EDITOR.md).

## Files and inspection

Keep the whole backup folder. ROM checks / Open verified backup revalidates
the captures, manifest and journal. Backups can contain your VIN and local paths;
redact those before sharing reports. Firmware is never included in releases.

ROM checks / Inspect ROM accepts any complete 1 MiB image. Known layouts get
their own identification, boot, native checksum-layout and checksum checks.
Unknown layouts display their size and SHA-256 without applying a guessed checksum.
Save checksum-corrected copy is available only for a recognized guarded layout;
it writes a new local file and never programs an ECU.
