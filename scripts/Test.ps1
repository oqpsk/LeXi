[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & dotnet restore Lexi.sln --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & dotnet build Lexi.sln -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    $fixture = Join-Path $root '.cache/test-dictionary'
    New-Item -ItemType Directory -Force $fixture | Out-Null
    $runner = Join-Path $root 'Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll'
    & dotnet $runner build-data tests/fixtures/dictionary.csv $fixture
    if ($LASTEXITCODE) { throw 'Fixture creation failed' }
    & dotnet $runner check $fixture --fixture
    if ($LASTEXITCODE) { throw 'Dictionary checks failed' }
    & dotnet $runner stream-check
    if ($LASTEXITCODE) { throw 'Streaming checks failed' }
    & dotnet $runner extended-check (Join-Path $root '.cache/extended-checks')
    if ($LASTEXITCODE) { throw 'Extended dictionary/translation checks failed' }
    & dotnet $runner parallel-check (Join-Path $root '.cache/parallel-checks')
    if ($LASTEXITCODE) { throw 'Parallel dictionary checks failed' }
    & dotnet run --project tests/Lexi.PresentationChecks -c Release
    if ($LASTEXITCODE) { throw 'WPF resource construction checks failed' }
} finally { Pop-Location }
