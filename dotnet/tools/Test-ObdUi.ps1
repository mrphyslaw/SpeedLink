param([Parameter(Mandatory=$true)][string]$Output, [Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$report = Join-Path $folder 'report.json'
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -ArgumentList @('--obd-smoke','--output',('"'+$folder+'"'),'--smoke-report',('"'+$report+'"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(90000)) { $process.Kill(); throw 'Engine diagnostics UI check timed out.' }
if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }; throw 'Engine diagnostics UI check failed.' }
$r = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$r.passed -or $r.hardwareAccessed -or !$r.readCodes -or !$r.clearAndRescan -or !$r.beforeReportRetained -or !$r.permanentCodesRemain -or !$r.controlsLocked -or !$r.romNotRequired -or !$r.unknownOutcome -or !$r.navigationShortcut -or !$r.shortcutMigration -or $r.pages -ne 8) { throw 'Incomplete engine diagnostics UI evidence.' }
Write-Output ('Engine diagnostics UI and report workflow passed: '+$folder)
