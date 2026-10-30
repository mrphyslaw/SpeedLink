param([string]$Output = 'TestResults/MenuUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Menu harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$report = Join-Path $runRoot 'report.json'
$process = Start-Process -FilePath $harness -ArgumentList @('--menus-offline','--output',('"' + $runRoot + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Menu UI timed out.' }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
    throw "Menu UI failed; see $report"
}
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.passed -or $result.hardwareAccessed -or !$result.keyboardActivation -or !$result.escapeCloses -or !$result.hostColorsIndependent -or !$result.permanentMenus -or !$result.labeledNavigation -or !$result.navigationRemembered -or $result.menus.Count -ne 5) { throw 'Unexpected menu test result.' }
Write-Output ("Application menus and labeled navigation checks passed: " + $runRoot)
