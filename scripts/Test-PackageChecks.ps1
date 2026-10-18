[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root ('.cache/package-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    foreach ($name in @('Lexi.exe','Lexi.dll','Lexi.Core.dll','Lexi.runtimeconfig.json','README.md','LICENSE','coreclr.dll')) { [IO.File]::WriteAllText((Join-Path $fixture $name),'synthetic public file') }
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'),'Public service documentation: https://docs.x.ai https://api.example.org/v1')
    & (Join-Path $PSScriptRoot 'Check-Package.ps1') -Directory $fixture -SelfContained
    foreach ($name in @('userdata','data','text-provider-keys')) {
        $path = Join-Path $fixture $name; New-Item -ItemType Directory -Path $path | Out-Null
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Check-Package.ps1') -Directory $fixture } catch { $rejected = $true }
        if (!$rejected) { throw 'Private directory escaped package checks' }
        Remove-Item -LiteralPath $path
    }
    foreach ($value in @(('sk-' + ('a' * 30)), ('C:' + [char]92 + 'Users' + [char]92 + 'Synthetic' + [char]92 + 'private'), ('https://ai.' + 'rwby.io/v1'))) {
        [IO.File]::WriteAllText((Join-Path $fixture 'Lexi.dll'),$value,[Text.Encoding]::Unicode)
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Check-Package.ps1') -Directory $fixture } catch { $rejected = $true }
        if (!$rejected) { throw 'Sensitive content escaped package checks' }
    }
    foreach ($script in @('Build.ps1','Package.ps1','Check-Package.ps1')) {
        $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $script),[ref]$null,[ref]$parseErrors)
        if ($parseErrors.Count) { throw "Syntax error in $script" }
    }
    Write-Host 'PASS: package checker rejects empty private directories and sensitive assembly content; packaging scripts parse. No release built.'
} finally {
    $full = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root '.cache')) + [IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $full -Recurse -Force }
}
