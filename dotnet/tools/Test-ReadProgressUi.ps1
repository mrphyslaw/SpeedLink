param([string]$Output = 'TestResults/ReadProgressUi')
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Read-progress UI harness build failed.' }
$executable = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
foreach ($scenario in @('success','cancel','timeout','short','mismatch','preflight')) {
    $case = Join-Path $folder $scenario
    New-Item -ItemType Directory -Path $case | Out-Null
    $report = Join-Path $case 'report.json'
    $process = Start-Process -FilePath $executable -ArgumentList @('--read-progress-offline','--scenario',$scenario,'--output',('"'+$case+'"'),'--report',('"'+$report+'"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(55000)) { $process.Kill(); throw "Read-progress UI timed out: $scenario" }
    if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }; throw "Read-progress UI failed: $scenario" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.hardwareAccessed -or !$result.actualReadButton -or !$result.progressReset -or !$result.controlsLocked -or !$result.unverifiedTransferCapped -or !$result.lateCallbacksIgnored -or !$result.minimumWindowVerified) { throw "Unexpected read-progress UI result: $scenario" }
    if ($scenario -in @('timeout','short') -and $result.memoryRequests -ne 15) { throw 'Failed read was retried.' }
    if ($scenario -in @('success','mismatch') -and $result.memoryRequests -ne 530) { throw 'Unexpected two-pass request count.' }
    if ($scenario -eq 'preflight' -and $result.memoryRequests -ne 0) { throw 'Preflight failure started a read.' }
    Write-Output "Read-progress UI passed: $scenario"
}
Write-Output "Read-progress UI results: $folder"
