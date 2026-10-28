param([string]$Output = 'TestResults/BenchDiagnostics')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.Bench/SpeedLink.Bench.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Bench diagnostics build failed.' }
$tool = Join-Path $PSScriptRoot 'SpeedLink.Bench/bin/Release/net10.0/win-x86/SpeedLink.Bench.exe'
foreach ($mode in @('normal', 'export-failure')) {
    $report = Join-Path $runRoot ($mode + '.json')
    $root = Join-Path $runRoot ($mode + '-diagnostics')
    if ($mode -eq 'export-failure') { [IO.File]::WriteAllText($report + '.support.zip', 'existing file must survive') }
    & $tool --mode diagnostics-self-test --diagnostics $root --report $report | Out-Null
    $exitCode = $LASTEXITCODE
    if (!(Test-Path -LiteralPath $report)) { throw 'Diagnostics helper lost the primary result.' }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    $version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    if (!$result.operationPassed -or $result.version -ne $version -or $result.tool -ne 'SpeedLink.Bench' -or $result.hardwareAccessed -or $result.diagnosticDroppedEvents -ne 0 -or $result.diagnosticStorageError) { throw 'Invalid diagnostics result or build identity.' }
    if ($mode -eq 'normal') {
        if ($exitCode -ne 0 -or !$result.passed -or !(Test-Path -LiteralPath $result.supportReport)) { throw 'Full-size diagnostics did not export.' }
        $segments = Get-ChildItem -LiteralPath $root -Recurse -Filter 'events-*.jsonl'
        if ($segments.Count -lt 2 -or ($segments | Where-Object Length -GT (2MB))) { throw 'Diagnostic rotation byte boundary failed.' }
    } else {
        if ($exitCode -ne 1 -or $result.passed -or !$result.supportReportError -or [IO.File]::ReadAllText($report + '.support.zip') -ne 'existing file must survive') { throw 'Secondary export failure lost the result or overwrote a file.' }
    }
}
Write-Output ('Bench diagnostics checks passed: ' + $runRoot)
# The intentional negative native-process case was verified above. Do not leave
# its exit code as the result of this successful test script in a CI wrapper.
exit 0
