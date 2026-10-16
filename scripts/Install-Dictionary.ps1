[CmdletBinding()]
param([string]$AppDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/Lexi'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$revision = 'bc015ed2e24a7abef49fc6dbbb7fe32c1dadaf8b'
$expectedHash = '1a6947e04785db63613a92e14903cdae7954f7e84860b10e68e5c7cbb3f9c3cf'
$cache = Join-Path $root '.cache/downloads'
New-Item -ItemType Directory -Force $cache | Out-Null
$csv = Join-Path $cache 'ecdict.csv'
if (!(Test-Path -LiteralPath $csv)) {
    Invoke-WebRequest "https://raw.githubusercontent.com/skywind3000/ECDICT/$revision/ecdict.csv" -OutFile ($csv + '.partial')
    if ((Get-FileHash -LiteralPath ($csv + '.partial') -Algorithm SHA256).Hash -ine $expectedHash) { throw 'Downloaded ECDICT checksum does not match the pinned revision' }
    Move-Item -LiteralPath ($csv + '.partial') -Destination $csv -Force
}
if ((Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash -ine $expectedHash) { throw 'Cached ECDICT checksum does not match the pinned revision' }
Push-Location $root
try {
    & dotnet build Lexi.Tools -c Release --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'Dictionary builder failed' }
    $data = Join-Path $AppDirectory 'data'
    New-Item -ItemType Directory -Force $data | Out-Null
    & dotnet Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll build-data $csv $data
    if ($LASTEXITCODE) { throw 'Dictionary indexing failed' }
    & dotnet Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll check $data
    if ($LASTEXITCODE) { throw 'Dictionary validation failed' }
    $licenses = Join-Path $AppDirectory 'licenses'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    Copy-Item licenses/ECDICT-LICENSE.txt -Destination $licenses -Force
    Copy-Item THIRD_PARTY_NOTICES.md -Destination $AppDirectory -Force
    Write-Host "Offline dictionary installed: $data"
} finally { Pop-Location }
