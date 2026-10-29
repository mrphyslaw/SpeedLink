param([string]$Output = 'TestResults/MapWindowsUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Map windows harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $process = Start-Process -FilePath $harness -ArgumentList @('--map-windows-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Map window UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Map window UI failed: $calibration; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or !$result.workflow.passed -or !$result.synthetic -or $result.hardwareAccessed -or $result.workflow.cases.Count -lt 57) { throw "Unexpected map windows result: $calibration" }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ('Nine map window workflows passed: ' + $runRoot)
