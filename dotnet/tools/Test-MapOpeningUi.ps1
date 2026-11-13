param([string]$Output = 'TestResults/MapOpeningUi', [ValidateRange(1, 4)][int]$Parallelism = 2, [string]$Executable)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
if (!$Executable) {
    dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Map opening harness build failed.' }
    $Executable = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
}
$harness = (Resolve-Path -LiteralPath $Executable).Path
$definitions = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../definitions/tuning') -Filter '*.xml' | Sort-Object Name)
$reports = @()
for ($start = 0; $start -lt $definitions.Count; $start += $Parallelism) {
    $jobs = @()
    try {
        foreach ($definition in $definitions[$start..([Math]::Min($start + $Parallelism - 1, $definitions.Count - 1))]) {
            $folder = Join-Path $root $definition.BaseName
            New-Item -ItemType Directory -Path $folder | Out-Null
            $report = Join-Path $folder 'report.json'
            $process = Start-Process -FilePath $harness -ArgumentList @('--map-opening-offline','--calibration',$definition.BaseName,'--output',('"'+$folder+'"'),'--report',('"'+$report+'"')) -WindowStyle Hidden -PassThru
            $jobs += @{ Process=$process; Definition=$definition; Report=$report }
        }
        foreach ($job in $jobs) {
            $calibration = $job.Definition.BaseName
            if (!$job.Process.WaitForExit(300000)) { $job.Process.Kill(); throw "Map-opening UI timed out: $calibration" }
            if ($job.Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $job.Report)) {
                if (Test-Path -LiteralPath $job.Report) { Get-Content -LiteralPath $job.Report -Raw | Write-Output }
                throw "Map-opening UI failed: $calibration"
            }
            $result = Get-Content -LiteralPath $job.Report -Raw | ConvertFrom-Json
            $expected = @(([xml](Get-Content -LiteralPath $job.Definition.FullName -Raw)).roms.rom.table.name | Sort-Object)
            $actual = @($result.workflow.results.Name | Sort-Object)
            if (!$result.passed -or !$result.workflow.passed -or !$result.synthetic -or $result.hardwareAccessed -or $result.workflow.checkedMaps -ne $expected.Count -or
                $actual.Count -ne $expected.Count -or @(Compare-Object $expected $actual).Count -ne 0) { $result.workflow.failures | Select-Object -First 20 | Write-Output
                throw "Incomplete map-opening coverage: $calibration"
            }
            $reports += $result | ForEach-Object { [ordered]@{calibration=$_.calibration; checkedMaps=$_.workflow.checkedMaps; complete=$_.workflow.complete; scrolling=$_.workflow.scrolling} }
            Write-Output "$calibration opened $($result.workflow.checkedMaps) controls ($($result.workflow.complete) fully visible, $($result.workflow.scrolling) scrollable)."
        }
    } finally {
        foreach ($job in $jobs) {
            if (!$job.Process.HasExited) { $job.Process.Kill(); $job.Process.WaitForExit() }
            $job.Process.Dispose()
        }
    }
}
@{passed=$true; hardwareAccessed=$false; reports=$reports} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
