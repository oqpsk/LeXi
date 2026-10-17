[CmdletBinding()]
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & dotnet restore Lexi.sln --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & dotnet build Lexi.sln -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    $output = Join-Path $root 'artifacts/Lexi'
    if ($SelfContained) {
        & dotnet publish Lexi.App -c Release -r win-x64 --self-contained true -o $output
    } else {
        & dotnet publish Lexi.App -c Release --no-restore --self-contained false -o $output
    }
    if ($LASTEXITCODE) { throw 'Publish failed' }
    Copy-Item README.md, THIRD_PARTY_NOTICES.md, CONTRIBUTING.md -Destination $output -Force
    Copy-Item docs -Destination $output -Recurse -Force
    if (Test-Path LICENSE) { Copy-Item LICENSE -Destination $output -Force }
    Write-Host "Build complete: $output"
} finally { Pop-Location }
