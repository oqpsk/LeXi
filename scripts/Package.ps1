[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Explicit invocation only: this script is not a release workflow and never uploads anything.
& (Join-Path $PSScriptRoot 'Test.ps1')
& (Join-Path $PSScriptRoot 'Check-Privacy.ps1')
& (Join-Path $PSScriptRoot 'Build.ps1') -SelfContained
$stage = Join-Path $root 'artifacts/Lexi-clean'
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$dist = Join-Path $root 'dist'; New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("LeXi-" + $version + "-win-x64.zip")
$temporary = Join-Path $dist ([Guid]::NewGuid().ToString('N') + '.zip')
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporary)
    $verify = Join-Path $root ('artifacts/verify-' + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($temporary, $verify)
        & (Join-Path $PSScriptRoot 'Check-Package.ps1') -Directory $verify -SelfContained
        Move-Item -LiteralPath $temporary -Destination $zip -Force
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($zip + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zip) + "`n")
        Write-Host "Portable ZIP prepared and checked: $zip"
    } finally {
        $resolved = [IO.Path]::GetFullPath($verify)
        $allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if ($resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
