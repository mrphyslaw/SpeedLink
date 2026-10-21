# C#/.NET attributions

SpeedLink application and test code is GPL-3.0-or-later, copyright (C) 2026 SpeedLink contributors. The repository LICENSE contains the full terms.

Known-ROM identity, checksum-layout metadata and the seven bundled tuning XML definitions follow [MazdaSpeedDefs](https://github.com/mrphyslaw/MazdaSpeedDefs), copyright (c) 2026 mrphyslaw, under MIT. The notice is retained in `third_party/MazdaSpeedDefs-LICENSE.txt` and `../definitions/tuning/LICENSE`.

The Windows executable bundles .NET 10 and WPF from Microsoft under their applicable MIT and third-party terms. Notices copied from the exact runtime/dependency packages are in `third_party/` and embedded under Help & licenses.

- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)

The FTDI backend independently implements [FTDI's public D2XX API](https://ftdichip.com/wp-content/uploads/2023/09/D2XX_Programmers_Guide.pdf) using bounded I/O and SafeHandle ownership. It loads the FTDI driver already installed in Windows; no FTDI DLL is distributed. A Win32 COM backend remains available in the source bench tools for comparison.

The Tactrix backend independently binds the SAE J2534-1 v04.04 API and uses
the installed Openport Windows driver for ISO 15765 framing and flow control.
No Tactrix driver or firmware is distributed. The vendor's driver installer is
available from [Tactrix](https://www.tactrix.com/index.php?Itemid=61&id=38&option=com_content&view=category).

xUnit, its Visual Studio adapter and Microsoft.NET.Test.Sdk are development dependencies and are excluded from the application. Offline ECU/recovery models and hardware harnesses are source test tools; none is a Simulator connection in the app.

No firmware, commercial assemblies, vendor drivers or customer backups are distributed. Protocol research acknowledgements remain in the repository ATTRIBUTIONS.md.
