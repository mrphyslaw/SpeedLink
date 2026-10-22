# SpeedLink documentation

SpeedLink combines ROM reading, offline ROM editing and stock-ECU logging.
Start with the workflow you need. ECU programming remains disabled.

## Using SpeedLink

| Guide | Contents |
| --- | --- |
| [Dump a ROM / request support](DUMP_A_ROM.md) | Guided adapter setup, two-pass reading, verification and ROM contribution |
| [ROM editor overview](editor/README.md) | Screenshots, features and a short editing workflow |
| [ROM editor operations](EDITOR.md) | Selections, arithmetic, clipboard, interpolation, comparison, undo and saving |
| [Logger overview](logger/README.md) | Live-data and recorded-log screenshots, features and a quick start |
| [Logger](LOGGER.md) | Adapter setup, ROM binding, parameters, units, charts, CSV recording and log viewing |
| [General usage](USAGE.md) | Reading, backups, transport settings and file inspection |
| [Supported ROMs](SUPPORTED_ROMS.md) | Known inspection layouts and compatibility limits |
| [Adapter status and testing candidates](ADAPTERS.md) | Qualified adapter paths, planned hardware and the test checklist |
| [Definition import](DEFINITIONS.md) | Import editor/logger updates, persistence and bundled fallback |
| [Logger coverage](../definitions/logging/All_ROM_Coverage.md) | Included and excluded parameters across all seven profiles |

## Development and validation

- [Build the single Windows EXE](BUILD.md).
- [C# implementation and tests](../dotnet/README.md).
- [Validation results and pending ECU checks](../dotnet/docs/Validation.md).
- [Bundled tuning definitions](../definitions/tuning/README.md).
- [Contributing](../CONTRIBUTING.md) and [licenses/attributions](../ATTRIBUTIONS.md).

The reader can dump unknown compatible-family ROMs without a definition. Editing
and logging each have stricter profile requirements, described in their guides.
