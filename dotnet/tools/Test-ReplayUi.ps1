param([string]$Output='TestResults/ReplayUi')
$ErrorActionPreference='Stop'
$dotnetRoot=Split-Path $PSScriptRoot -Parent
$runRoot=Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Replay harness build failed.' }
$harness=Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$source=Join-Path $dotnetRoot 'tests/fixtures/Synthetic-Speed6-log.csv'
$before=(Get-FileHash -LiteralPath $source).Hash
$report=Join-Path $runRoot 'Replay.json'
$arguments=@('--replay-offline','--file',('"'+$source+'"'),'--report',('"'+$report+'"'),'--screenshot',('"'+(Join-Path $runRoot 'Replay.png')+'"'))
$process=Start-Process -FilePath $harness -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Replay UI timed out.' }
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $report -Raw; throw 'Replay UI failed.' }
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.passed -ne $true -or $result.channels -ne 8 -or $result.rows -ne 300 -or $result.plottedChannels -ne 6 -or (Get-FileHash -LiteralPath $source).Hash -ne $before) { throw 'Unexpected replay result or changed source.' }
Write-Output ('Replay UI passed: '+$runRoot)
