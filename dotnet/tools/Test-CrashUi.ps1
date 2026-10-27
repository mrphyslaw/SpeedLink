param([string]$Output = 'TestResults/CrashUi', [Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$quoted = '"' + $folder + '"'
$probe = Start-Process -FilePath $Executable -ArgumentList @('--crash-probe','--output',$quoted) -WindowStyle Hidden -PassThru
if (!$probe.WaitForExit(30000)) { $probe.Kill(); throw 'Crash capture timed out.' }
if ($probe.ExitCode -ne 70) { throw 'Unexpected crash probe exit code.' }
$report = Join-Path $folder 'report.json'
$review = Start-Process -FilePath $Executable -ArgumentList @('--crash-review','--output',$quoted,'--smoke-report',('"'+$report+'"')) -WindowStyle Hidden -PassThru
if (!$review.WaitForExit(30000)) { $review.Kill(); throw 'Crash report restart check timed out.' }
if ($review.ExitCode -ne 0) { throw 'Crash report restart check failed.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.passed -or $result.hardwareAccessed -or !$result.priorProcessReport -or !$result.acknowledged -or !$result.exported) { throw 'Unexpected crash check result.' }
Write-Output 'Unexpected UI failure, durable capture, restart review and support ZIP passed.'
