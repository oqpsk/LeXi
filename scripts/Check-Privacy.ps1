[CmdletBinding()]
param([switch]$Staged)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $paths = @(& git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE) { throw 'Run this check inside the Git repository' }
    $issues = [Collections.Generic.List[string]]::new()
    $rules = [ordered]@{
        'GitHub token' = '\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b'
        'Provider secret' = '\b(?:sk-(?:ant-)?[A-Za-z0-9_-]{20,}|xai-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{30,})\b'
        'Private key' = '-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----'
        'Personal Windows path' = '(?i)\b[A-Z]:[\\/]Users[\\/][^\\/\s<>]+'
        'Personal Unix path' = '(?:/Users/|/home/)[A-Za-z0-9_.-]+/'
        'Email address' = '\b[A-Za-z0-9._%+-]+@(?![A-Za-z0-9.-]*noreply\.github\.com\b|example\.(?:com|org)\b)[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b'
        'JWT' = '\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b'
        'Credential assignment' = '(?im)(?:access_token|refresh_token|client_secret|api_key)\s*["'':= ]+\s*["''][A-Za-z0-9_./+-]{20,}["'']'
    }
    foreach ($path in $paths) {
        if ($path -match '(^|/)(userdata|cache|\.cache|bin|obj|work|artifacts|dist|\.codex|\.claude|\.grok)/|(^|/)(auth\.json|settings\.json|\.env[^/]*|credentials[^/]*)$|\.(log|jsonl|exe|dll|pdb|zip|idx|dat)$') {
            $issues.Add("${path}: private state or generated artifact"); continue
        }
        if ($path -match '\.csv$' -and $path -notmatch '^tests/fixtures/') { $issues.Add("${path}: full dictionary data"); continue }
        if ($Staged) {
            $size = & git cat-file -s ":$path"
            if ($LASTEXITCODE) { throw "Cannot inspect staged file $path" }
        } else { $size = (Get-Item -LiteralPath $path).Length }
        if ([long]$size -gt 1MB) { $issues.Add("${path}: oversized source file"); continue }
        if ($path -eq 'Lexi.App/Assets/Lexi.ico') { continue }
        if ($Staged) { $content = (& git show ":$path") -join "`n" } else { $content = [IO.File]::ReadAllText((Join-Path $root $path)) }
        foreach ($rule in $rules.GetEnumerator()) { if ($content -match $rule.Value) { $issues.Add("${path}: $($rule.Key)") } }
    }
    if ($issues.Count) { $issues | ForEach-Object { Write-Host $_ }; throw "Privacy check failed: $($issues.Count) issue(s). Secret values were not printed." }
    Write-Host "Privacy check passed: $($paths.Count) tracked files."
} finally { Pop-Location }
