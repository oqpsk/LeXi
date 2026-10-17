$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $root '.cache'
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('shell-icon-' + [Guid]::NewGuid().ToString('N'))))
$shortcuts = Join-Path $fixture 'shortcuts'
$app = Join-Path $fixture 'app'
New-Item -ItemType Directory -Path $shortcuts, (Join-Path $app 'assets/shell') -Force | Out-Null
$shellObject = New-Object -ComObject WScript.Shell
try {
    $exe = Join-Path $app 'Lexi.exe'
    [IO.File]::WriteAllText($exe, 'not an executable; never launched')
    Copy-Item -LiteralPath (Join-Path $root 'Lexi.App/Assets/Lexi.ico') -Destination (Join-Path $app 'assets/shell/Lexi.ico')
    $matchPath = Join-Path $shortcuts 'Custom name.lnk'
    $otherPath = Join-Path $shortcuts 'Lexi.lnk'
    $match = $shellObject.CreateShortcut($matchPath)
    $match.TargetPath = $exe; $match.Arguments = '--preserved'; $match.WorkingDirectory = $app; $match.IconLocation = ',0'; $match.Save()
    $other = $shellObject.CreateShortcut($otherPath)
    $other.TargetPath = Join-Path $fixture 'unrelated.exe'; $other.Save()
    $otherHash = (Get-FileHash -LiteralPath $otherPath).Hash
    & (Join-Path $root 'scripts/Refresh-TaskbarIcon.ps1') -Executable $exe -ShortcutDirectory $shortcuts -RecoveryDirectory (Join-Path $fixture 'recovery')
    $updated = $shellObject.CreateShortcut($matchPath)
    if ($updated.TargetPath -ne $exe -or $updated.Arguments -ne '--preserved' -or $updated.WorkingDirectory -ne $app) { throw 'Shortcut launch settings changed.' }
    if ($updated.IconLocation -notmatch 'LeXi-[0-9a-f]{16}\.ico,0$') { throw 'Icon did not receive a content-addressed resource.' }
    $icon = $updated.IconLocation.Substring(0, $updated.IconLocation.Length - 2)
    if ((Get-FileHash -LiteralPath $icon).Hash -ne (Get-FileHash -LiteralPath (Join-Path $app 'assets/shell/Lexi.ico')).Hash) { throw 'Shell icon bytes differ from packaged artwork.' }
    if ($otherHash -ne (Get-FileHash -LiteralPath $otherPath).Hash) { throw 'An unrelated shortcut was modified.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $fixture 'recovery') -File).Count -ne 1) { throw 'Shortcut recovery copy is missing.' }
    Write-Host 'PASS: taskbar icon refresh matches the executable, preserves launch settings and unrelated shortcuts, and backs up the changed link.'
    $firstHash = (Get-FileHash -LiteralPath $matchPath).Hash
    & (Join-Path $root 'scripts/Refresh-TaskbarIcon.ps1') -Executable $exe -ShortcutDirectory $shortcuts
    if ($firstHash -ne (Get-FileHash -LiteralPath $matchPath).Hash) { throw 'Repeated icon refresh changed an up-to-date shortcut.' }
    Write-Host 'PASS: repeated shell icon refresh leaves the current shortcut and icon cache identity unchanged.'
} finally {
    foreach ($com in @($match, $other, $updated, $shellObject)) {
        if ($null -ne $com) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) | Out-Null }
    }
    $resolvedFixture = (Resolve-Path -LiteralPath $fixture).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $fixtureRoot).Path
    if (!$resolvedFixture.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped the cache directory.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
