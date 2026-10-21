param([string]$Output = 'publish')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/SpeedLink.Desktop/SpeedLink.Desktop.csproj'
dotnet publish $project -c Release --no-restore -o $Output -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$exe = Join-Path $Output 'SpeedLink.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Standalone executable is missing.' }
$unexpected = @(Get-ChildItem -LiteralPath $Output -File | Where-Object Name -ne 'SpeedLink.exe')
if ($unexpected.Count -ne 0) { throw "Unexpected supporting files: $($unexpected.Name -join ', ')" }
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "$hash  SpeedLink.exe"
