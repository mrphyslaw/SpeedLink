param([string]$Output = 'TestResults/AxisUi', [ValidateRange(1, 4)][int]$Parallelism = 2)
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Axis harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
$definitionRoot = Join-Path $PSScriptRoot '../../definitions/tuning'
$definitions = @(Get-ChildItem -LiteralPath $definitionRoot -Filter '*.xml' | Sort-Object Name)
# These checks render app-owned controls without pointer/keyboard automation or
# clipboard access. Independent processes can safely share the desktop.
for ($start = 0; $start -lt $definitions.Count; $start += $Parallelism) {
 $jobs = @()
 try {
 foreach ($definition in $definitions[$start..([Math]::Min($start + $Parallelism - 1, $definitions.Count - 1))]) {
    $calibration = $definition.BaseName
    $expectedTables = ([xml](Get-Content -LiteralPath $definition.FullName -Raw)).roms.rom.table.Count
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $stderr = Join-Path $folder 'stderr.log'
    $process = Start-Process -FilePath $harness -ArgumentList @('--axes-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -RedirectStandardError $stderr -PassThru
    $jobs += @{ Process = $process; Calibration = $calibration; ExpectedTables = $expectedTables; Report = $report; Stderr = $stderr }
 }
 foreach ($job in $jobs) {
    $process = $job.Process; $calibration = $job.Calibration; $expectedTables = $job.ExpectedTables; $report = $job.Report; $stderr = $job.Stderr
    if (-not $process.WaitForExit(360000)) { $process.Kill(); throw "Every-table axis check timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 30 | Write-Output }
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Axis UI failed: $calibration; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.hardwareAccessed -or !$result.synthetic -or !$result.workflow.passed -or
        $result.workflow.tableCount -ne $expectedTables -or $result.workflow.checkedViews -ne 2 * $expectedTables -or
        !$result.workflow.shortTablesCentered -or !$result.workflow.resizedAndDetached -or !$result.workflow.fittingColumnResize -or !$result.workflow.romBytesUnchanged) {
        throw "Incomplete every-table axis coverage: $calibration"
    }
    $expectedNames = @(([xml](Get-Content -LiteralPath (Join-Path $definitionRoot ($calibration + '.xml')) -Raw)).roms.rom.table | ForEach-Object name | Sort-Object)
    foreach ($width in @(1280, 1024)) {
        $names = @($result.workflow.checkedTables | Where-Object requestedWidth -eq $width | ForEach-Object table | Sort-Object)
        if ($names.Count -ne $expectedTables -or @(Compare-Object $expectedNames $names).Count -ne 0) {
            throw "Missing or duplicated table coverage: $calibration at $width"
        }
    }
    $reports += $result
    Write-Output ("Axis labels passed: $calibration, $expectedTables tables, " + $result.workflow.checkedViews + ' views')
 }
 } finally {
    foreach ($job in $jobs) { if (!$job.Process.HasExited) { $job.Process.Kill(); $job.Process.WaitForExit() }; $job.Process.Dispose() }
 }
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ('All bundled definitions passed every-table axis checks: ' + $runRoot)
