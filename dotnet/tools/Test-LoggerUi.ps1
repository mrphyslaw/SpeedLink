param([string]$Output = 'TestResults/LoggerUi')
$ErrorActionPreference = 'Stop'
$dotnetRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $dotnetRoot -Parent
$runRoot = Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$harnessProject = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj'
dotnet build $harnessProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Logger UI harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($definitionFile in (Get-ChildItem -LiteralPath (Join-Path $repoRoot 'definitions/logging') -Filter '*.logger.json' | Sort-Object Name)) {
    $definition = $definitionFile.FullName
    $calibration = $definitionFile.Name.Replace('.logger.json', '')
    $vectors = Join-Path $definitionFile.DirectoryName ($calibration + '.vectors.json')
foreach ($mode in @('record-stop', 'connection-failure')) {
    $folder = Join-Path $runRoot ($calibration + '-' + $mode)
    New-Item -ItemType Directory -Path $folder | Out-Null
    $report = Join-Path $folder 'report.json'
    $arguments = @('--logger-offline', '--definition', ('"' + $definition + '"'), '--vectors', ('"' + $vectors + '"'),
        '--vault', ('"' + (Join-Path $folder 'data') + '"'), '--report', ('"' + $report + '"'), '--screenshot', ('"' + (Join-Path $folder 'logger.png') + '"'))
    if ($mode -eq 'connection-failure') { $arguments += '--fault' }
    $process = Start-Process -FilePath $harness -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw "Offline logger UI timed out: $mode" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Offline logger UI failed: $calibration $mode; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.passed -ne $true -or $result.realEcuTested -ne $false) { throw "Unexpected offline UI result: $mode" }
    $reports += $result
}
}
$summary = @{ passed = $true; hardwareAccessed = $false; reports = $reports }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ("Offline logger UI checks passed: " + $runRoot)
