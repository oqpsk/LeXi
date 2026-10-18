[CmdletBinding()]
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/Lexi-clean'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if (!$output.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid build target' }
# Never reuse an installation folder or silently erase state, even under artifacts.
if (Test-Path -LiteralPath $output) {
    foreach ($name in @('userdata','data','settings.json','history.json')) {
        if (Test-Path -LiteralPath (Join-Path $output $name)) { throw 'Build target contains user data; choose a clean artifact folder.' }
    }
    if (Get-ChildItem -LiteralPath $output -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Build target contains links' }
    & (Join-Path $PSScriptRoot 'Check-Package.ps1') -Directory $output
    Remove-Item -LiteralPath $output -Recurse -Force
}
Push-Location $root
try {
    $arguments = @('publish','Lexi.App','-c','Release','-o',$output,'-p:DebugType=none','-p:DebugSymbols=false','-p:ContinuousIntegrationBuild=true')
    if ($SelfContained) { $arguments += @('-r','win-x64','--self-contained','true','--source','https://api.nuget.org/v3/index.json') }
    else { $arguments += @('--self-contained','false') }
    & dotnet @arguments
    if ($LASTEXITCODE) { throw 'Publish failed' }
    # Explicit public files, plus only tracked documentation. Never copy an installation.
    Copy-Item README.md, THIRD_PARTY_NOTICES.md, LICENSE -Destination $output -Force
    Copy-Item scripts/Refresh-TaskbarIcon.ps1 -Destination $output -Force
    $documents = @(& git -c core.quotepath=false ls-files -- docs licenses)
    if ($LASTEXITCODE) { throw 'Cannot enumerate public documentation' }
    foreach ($document in $documents) {
        $target = Join-Path $output $document
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $document) -Destination $target
    }
    & ./scripts/Check-Package.ps1 -Directory $output -SelfContained:$SelfContained
    Write-Host "Clean program build: $output"
} finally { Pop-Location }
