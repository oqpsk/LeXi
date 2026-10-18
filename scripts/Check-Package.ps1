[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory, [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $Directory).Path
$issues = [Collections.Generic.List[string]]::new()
$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
$nodes = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Force)
foreach ($node in $nodes) {
    $relative = [IO.Path]::GetRelativePath($packageRoot, $node.FullName).Replace('\','/')
    if ($node.Attributes -band [IO.FileAttributes]::ReparsePoint) { $issues.Add("${relative}: linked path") }
    if ($relative -match '(^|/)(userdata|data|cache|speech-cache|text-provider-keys|keys|\.git|\.codex|\.claude|bin|obj)(?:/|$)|(^|/)(settings|history|auth|credentials|window-placement|settings-window-placement)\.json$|\.(pdb|log|jsonl|bin|lexibackup|tmp)$|(^|/)\.env') {
        $issues.Add("${relative}: private or generated state")
    }
}
$patterns = @(
    '(?i)[A-Z]:[\\/]Users[\\/][^\\/\s<>]+', '(?:/Users/|/home/)[A-Za-z0-9_.-]+/',
    '\b(?:sk-(?:ant-)?[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b',
    '-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----', ('ai\.' + 'rwby\.io')
)
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\','/')
    # Scan application assemblies and text, not the framework's own arbitrary binary tables.
    if ($file.Name -notmatch '^(Lexi|lexi-host)' -and $file.Extension -notmatch '^\.(md|txt|json|ps1|xml|config)$') { continue }
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $contents = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))
    foreach ($pattern in $patterns) { if (@($contents | Where-Object { $_ -match $pattern }).Count) { $issues.Add("${relative}: sensitive content pattern"); break } }
}
foreach ($name in @('Lexi.exe','Lexi.dll','Lexi.Core.dll','Lexi.runtimeconfig.json','README.md','LICENSE')) {
    if (!(Test-Path -LiteralPath (Join-Path $packageRoot $name) -PathType Leaf)) { $issues.Add("${name}: missing program file") }
}
if ($SelfContained -and !(Test-Path -LiteralPath (Join-Path $packageRoot 'coreclr.dll'))) { $issues.Add('Missing bundled .NET runtime') }
if ($issues.Count) { $issues | ForEach-Object { Write-Host $_ }; throw "Package check failed: $($issues.Count) issue(s); matching values were not printed." }
Write-Host "Package check passed: $($files.Count) files; no user-state directories, debug symbols or configured sensitive-content patterns."
