param([Parameter(Mandatory=$true)][string]$Results, [string]$Report)
$ErrorActionPreference='Stop'
[xml]$run=Get-Content -LiteralPath $Results -Raw
$passed=@($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
$prefix='SpeedLink.Tests.RomSaveIntegrityTests.'
$matrixTest=$prefix+'SaveIntegrityMatrixIncludesEverySupportedEditorDefinition'
if($passed -notcontains $matrixTest){throw 'The all-profile checksum coverage test did not pass.'}
$definitions=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../definitions/tuning') -Filter '*.xml' | Sort-Object BaseName
if(!$definitions.Count){throw 'No bundled editor definitions were found.'}
$rows=@()
foreach($definition in $definitions){
    $calibration=$definition.BaseName
    foreach($method in @('EveryEditedAndRepeatedSaveHasValidChecksumAndPreservesSource','NoEditSaveCannotBypassChecksumCorrectionOrNativeRejection')){
        $name=$prefix+$method+'(calibration: "'+$calibration+'")'
        if($passed -notcontains $name){throw "Missing mandatory passing ROM-save checksum test: $name"}
    }
    $rows+=@{calibration=$calibration;editedAndRepeatedSave=$true;noEditAndInvalidInput=$true;passed=$true}
}
if([int]$run.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$run.TestRun.ResultSummary.Counters.notExecuted -ne 0){throw 'The test run has failures or skipped tests.'}
if($Report){@{passed=$true;profiles=$rows;cases=($rows.Count*2+1)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8}
Write-Output "Mandatory ROM-save checksum tests passed for all $($rows.Count) supported editor profiles."
