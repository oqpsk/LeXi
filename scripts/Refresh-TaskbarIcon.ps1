[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ShortcutDirectory = (Join-Path $env:APPDATA 'Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar'),
    [string]$RecoveryDirectory
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$appDirectory = Split-Path -Parent $executablePath
$iconSource = Join-Path $appDirectory 'assets/Lexi.ico'
if (!(Test-Path -LiteralPath $iconSource -PathType Leaf)) { throw 'Packaged LeXi icon is missing.' }
# A changed icon gets a different shell cache identity; unchanged artwork stays stable.
$digest = (Get-FileHash -LiteralPath $iconSource -Algorithm SHA256).Hash.Substring(0,16).ToLowerInvariant()
$iconPath = Join-Path $appDirectory ('assets/LeXi-' + $digest + '.ico')
if (!(Test-Path -LiteralPath $iconPath)) { Copy-Item -LiteralPath $iconSource -Destination $iconPath }
$iconLocation = $iconPath + ',0'
if (!(Test-Path -LiteralPath $ShortcutDirectory -PathType Container)) { Write-Host 'No taskbar shortcuts to refresh.'; return }
if (-not ('LeXiShellIconRefresh' -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LeXiShellIconRefresh {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
}
'@
}
$shellObject = New-Object -ComObject WScript.Shell
$updated = 0
try {
    foreach ($file in Get-ChildItem -LiteralPath $ShortcutDirectory -Filter '*.lnk' -File) {
        $shortcut = $shellObject.CreateShortcut($file.FullName)
        try {
            if (![string]::Equals($shortcut.TargetPath, $executablePath, [StringComparison]::OrdinalIgnoreCase)) { continue }
            if ($shortcut.IconLocation -eq $iconLocation) { continue }
            if ($RecoveryDirectory) {
                New-Item -ItemType Directory -Path $RecoveryDirectory -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $RecoveryDirectory ($file.BaseName + '-' + [Guid]::NewGuid().ToString('N') + '.lnk'))
            }
            $shortcut.IconLocation = $iconLocation
            $shortcut.Save()
            # Notify only the changed LeXi shortcut. Do not restart Explorer or purge caches.
            [LeXiShellIconRefresh]::SHChangeNotify(0x2000, 0x0005, $file.FullName, [IntPtr]::Zero)
            $updated++
        } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null }
    }
} finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shellObject) | Out-Null }
Write-Host "Refreshed $updated LeXi taskbar shortcut(s)."
