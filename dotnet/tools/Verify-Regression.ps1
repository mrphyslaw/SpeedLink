param([Parameter(Mandatory=$true)][string]$Results)
$ErrorActionPreference = 'Stop'
[xml]$result = Get-Content -LiteralPath $Results -Raw
$passed = @($result.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
$matrix = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../docs/Regression_Cases.json') -Raw | ConvertFrom-Json
foreach ($entry in $matrix) {
    if ($passed -notcontains $entry.test) { throw "Missing passing regression case: $($entry.test)" }
}
if ([int]$result.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$result.TestRun.ResultSummary.Counters.notExecuted -ne 0) { throw 'The test run has failures or skipped tests.' }
Write-Output "All $($matrix.Count) baseline regression cases passed."
