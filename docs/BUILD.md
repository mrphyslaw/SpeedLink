# Building SpeedLink for Windows

The current app uses C#/.NET 10 and WPF. Build on 64-bit Windows using the SDK
pinned in `dotnet/global.json`. From the repository root:

```powershell
Set-Location dotnet
dotnet restore SpeedLink.slnx --locked-mode
dotnet test SpeedLink.slnx -c Release --no-restore
./tools/Test-LoggerUi.ps1
./tools/Test-ReplayUi.ps1
./tools/Test-EditorUi.ps1
./tools/Test-DefinitionImportUi.ps1
./tools/Publish-Windows.ps1 -Output publish
```

The result is **`dotnet/publish/SpeedLink.exe`**, a self-contained Windows x86
executable. Users do not need to install .NET. Runtime libraries may extract to
a temporary folder, while backups stay in `data` beside the EXE. Help, licenses
and dependency notices are embedded. This 32-bit build runs on 64-bit Windows
and loads the installed 32-bit Tactrix J2534 driver directly. Vgate uses its
installed FTDI driver. No adapter drivers or ROMs are bundled. The EXE is unsigned.

The build script checks that its output folder contains one EXE and prints its
SHA-256. Release assets include a checksum file and matching source. Pinned SDK
and dependency lockfiles make the inputs explicit; builds do not promise
byte-identical executables.

See [C# validation](../dotnet/docs/Validation.md) for offline and isolated bench
coverage. A build or offline ECU model does not establish real ECU behavior.

## Release retention

The **Release retention** workflow runs after a release is published, with a
daily catch-up at 06:23 UTC. It keeps
the five newest published releases, including prereleases, ordered by
publication time. Older release entries and their attached downloads are
deleted. Drafts do not count toward the five and are left alone. Git tags and
source history are preserved, so tagged source remains available.

For a preview, open **Actions → Release retention → Run workflow**, leave
**dry_run** checked, and run it from the default branch. Uncheck it to perform
cleanup manually. The run log and summary report the outcome. Automated runs
perform cleanup without an additional prompt.

The workflow tests the retention logic before cleanup, uses a repository-scoped
token, serializes cleanup runs and verifies that existing tags retain their
original references. Run the offline checks with:

```sh
node --test .github/scripts/release-retention.test.mjs
```

The daily catch-up also covers releases created using another workflow's
`GITHUB_TOKEN`, which does not trigger follow-on release workflows. See
[workflow trigger rules](https://docs.github.com/en/actions/how-tos/writing-workflows/choosing-when-your-workflow-runs/triggering-a-workflow).
