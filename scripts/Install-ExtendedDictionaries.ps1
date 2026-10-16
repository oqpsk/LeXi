[CmdletBinding()]
param([string]$AppDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/Lexi'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$dataRoot = Join-Path $AppDirectory 'data'
$cache = Join-Path $root '.cache/dictionaries'
New-Item -ItemType Directory -Force $dataRoot,$cache | Out-Null
Push-Location $root
try {
    & dotnet build Lexi.Tools -c Release --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'Dictionary builder failed' }
    $packages = Get-Content dictionaries/catalog.json -Raw | ConvertFrom-Json
    foreach ($package in $packages) {
        if ($package.metadata.id -notmatch '^[a-z0-9-]+$' -or $package.file -match '[/\\]') { throw 'Invalid dictionary package path' }
        $download = Join-Path $cache $package.file
        if (!(Test-Path -LiteralPath $download)) {
            & curl.exe --fail --location --connect-timeout 20 --max-time 900 --retry 2 --output ($download + '.partial') $package.url
            if ($LASTEXITCODE) { throw "Download failed: $($package.metadata.id)" }
            Move-Item -LiteralPath ($download + '.partial') -Destination $download -Force
        }
        $hash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($package.sha256 -and $hash -ne $package.sha256) { throw "Checksum mismatch: $($package.file). Review an upstream update before replacing the pinned hash." }
        if ($package.format -eq 'cedict') {
            $gzip = [IO.Compression.GZipStream]::new([IO.File]::OpenRead($download), [IO.Compression.CompressionMode]::Decompress)
            $reader = [IO.StreamReader]::new($gzip)
            try { $header = (1..40 | ForEach-Object { $reader.ReadLine() }) -join "`n" } finally { $reader.Dispose() }
            if ($header -notmatch 'https://creativecommons.org/licenses/by-sa/4.0/') { throw 'Review the CC-CEDICT license before installing this version' }
            $package.metadata.version = "snapshot $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')) / SHA256 $($hash.Substring(0,12))"
        }
        $temporary = Join-Path $dataRoot ('.install-' + $package.metadata.id + '-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory $temporary | Out-Null
        $metadata = Join-Path $temporary 'dictionary.json'
        $package.metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metadata -Encoding utf8
        & dotnet Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll import-corpus $package.format $download $temporary $metadata
        if ($LASTEXITCODE) { throw "Import failed: $($package.metadata.id)" }
        $index = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $temporary 'lexicon.idx')))
        try { $magic = $index.ReadString(); $count = $index.ReadInt32() } finally { $index.Dispose() }
        if ($magic -ne 'LEXI1' -or $count -lt $package.minimumEntries) { throw "Incomplete dictionary: $($package.metadata.id)" }
        @{ source = $package.url; sha256 = $hash; importedUtc = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content (Join-Path $temporary 'provenance.json') -Encoding utf8
        Copy-Item THIRD_PARTY_NOTICES.md -Destination $temporary
        if ($package.format -eq 'moe') {
            Copy-Item -LiteralPath $download -Destination (Join-Path $temporary 'original-source.zip')
            $usage = Join-Path $cache 'moe-usage.pdf'
            if (!(Test-Path $usage)) { & curl.exe --fail --location --connect-timeout 20 --max-time 120 --output $usage $package.usageUrl; if ($LASTEXITCODE) { throw 'MOE usage notice download failed' } }
            if ((Get-FileHash $usage).Hash -ine $package.usageSha256) { throw 'MOE usage notice checksum mismatch' }
            Copy-Item -LiteralPath $usage -Destination (Join-Path $temporary 'MOE-USAGE.pdf')
        }
        $target = [IO.Path]::GetFullPath((Join-Path $dataRoot $package.metadata.id))
        if (!$target.StartsWith($dataRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target must stay inside the dictionary folder' }
        if (Test-Path -LiteralPath $target) {
            $backup = Join-Path $dataRoot ('.previous-' + $package.metadata.id + '-' + [guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $target -Destination $backup
            try { Move-Item -LiteralPath $temporary -Destination $target } catch { Move-Item -LiteralPath $backup -Destination $target; throw }
        } else { Move-Item -LiteralPath $temporary -Destination $target }
        Write-Host "Installed $($package.metadata.name): $count lookup entries. Restart Lexi to load new dictionaries."
    }
} finally { Pop-Location }
