# Contributing

SpeedLink supports ROM reading, logging and offline editing. ECU programming
remains disabled. Keep that scope explicit when proposing changes.

The current app uses C#/.NET and WPF. Use the SDK pinned in `dotnet/global.json`.
From `dotnet/`, run:

```powershell
dotnet restore SpeedLink.slnx --locked-mode
dotnet test SpeedLink.slnx -c Release --no-restore
./tools/Publish-Windows.ps1 -Output publish
```

Tests generate synthetic data in memory. They do not need a Mazda ROM, serial
adapter or vehicle, and they must not silently skip when private files are absent.
Fixture boot hashes are patched only within tests. Production profile recognition
must keep its independent pinned value.

Offline ECU models belong in `dotnet/tests/SpeedLink.Tests`. Keep them outside
production code so they
are not included in the executable or exposed as a user connection mode.

The physical transport has a request allowlist checked before any serial output.
Keep programming and DTC-clearing requests blocked. An offline protocol model or
simulator is not proof that a real ECU can be programmed or recovered.

Do not commit firmware dumps, customer backups, VINs, credentials, commercial
assemblies, vendor drivers or local research archives. Share a minimal synthetic
reproducer and redact local paths from logs. Never include an ECU image in a
release archive. Contributions use GPL-3.0-or-later.
