param([string]$Output = 'TestResults/SupportUi', [string]$Executable)
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
if (!$Executable) {
    dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Support UI harness build failed.' }
    $Executable = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
    $mode = '--support-offline'; $reportArgument = '--report'
} else { $mode = '--support-smoke'; $reportArgument = '--smoke-report' }
$report = Join-Path $folder 'report.json'
$process = Start-Process -FilePath $Executable -ArgumentList @($mode,'--output',('"'+$folder+'"'),$reportArgument,('"'+$report+'"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(45000)) { $process.Kill(); throw 'Support report UI timed out.' }
if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }; throw 'Support report UI failed.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.passed -or $result.hardwareAccessed -or !$result.savedSessionAvailableOffline -or !$result.actualExportButton -or !$result.controlsLockedDuringExport -or $result.entries -notcontains 'summary.txt') { throw 'Unexpected support-report UI result.' }
Write-Output "Support report preview and ZIP export passed: $folder"
