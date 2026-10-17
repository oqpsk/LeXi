param([string]$PreviewPath = "")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$folder = Join-Path $PSScriptRoot 'Lexi.App/Assets'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$glyph = [System.Drawing.Drawing2D.GraphicsPath]::new()
$font = [System.Drawing.FontFamily]::new('Georgia')
$glyph.AddString('ξ', $font, [int][System.Drawing.FontStyle]::Bold, 180, [System.Drawing.PointF]::new(0,0), [System.Drawing.StringFormat]::GenericTypographic)
$bounds = $glyph.GetBounds()
$matrix = [System.Drawing.Drawing2D.Matrix]::new()
$matrix.Translate(-$bounds.X, -$bounds.Y)
$glyph.Transform($matrix); $matrix.Dispose()
$scale = 170 / $bounds.Height
$matrix = [System.Drawing.Drawing2D.Matrix]::new()
$matrix.Scale($scale,$scale); $glyph.Transform($matrix); $matrix.Dispose()
$bounds = $glyph.GetBounds()
$matrix = [System.Drawing.Drawing2D.Matrix]::new()
$matrix.Translate((256-$bounds.Width)/2,(256-$bounds.Height)/2)
$glyph.Transform($matrix); $matrix.Dispose()
$shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
$shape.AddArc(8,8,72,72,180,90); $shape.AddArc(176,8,72,72,270,90)
$shape.AddArc(176,176,72,72,0,90); $shape.AddArc(8,176,72,72,90,90); $shape.CloseFigure()
$background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#322632'))
$foreground = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFADD2'))
$sizes = @(16,24,32,48,64,128,256)
$frames = @()
foreach($size in $sizes) {
    $image = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size/256.0,$size/256.0)
    $graphics.FillPath($background,$shape); $graphics.FillPath($foreground,$glyph)
    $stream = [IO.MemoryStream]::new()
    $image.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256 -and $PreviewPath) { $image.Save([IO.Path]::GetFullPath($PreviewPath),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $image.Dispose()
}
$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $folder 'Lexi.ico')))
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset); $offset += $frames[$i].Length
}
foreach($bytes in $frames) { $writer.Write([byte[]]$bytes) }
$writer.Dispose()
# Export the same glyph outline for the repository logo, without a font dependency.
$points=$glyph.PathPoints; $types=$glyph.PathTypes; $parts=[Collections.Generic.List[string]]::new()
function PointText($point) { return $point.X.ToString('0.###',[Globalization.CultureInfo]::InvariantCulture)+','+$point.Y.ToString('0.###',[Globalization.CultureInfo]::InvariantCulture) }
for($i=0; $i -lt $points.Length; $i++) {
    $kind=$types[$i] -band 7
    if($kind -eq 0) { $parts.Add('M'+(PointText $points[$i])) }
    elseif($kind -eq 1) { $parts.Add('L'+(PointText $points[$i])) }
    elseif($kind -eq 3) { $parts.Add('C'+(PointText $points[$i])+' '+(PointText $points[$i+1])+' '+(PointText $points[$i+2])); $i+=2 }
    if(($types[$i] -band 128) -ne 0) { $parts.Add('Z') }
}
$svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="LeXi"><rect x="8" y="8" width="240" height="240" rx="36" fill="#322632"/><path fill="#FFADD2" d="'+($parts -join ' ')+'"/></svg>'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'docs') -Force | Out-Null
$svg | Set-Content (Join-Path $PSScriptRoot 'docs/brandmark.svg') -Encoding utf8NoBOM
$foreground.Dispose(); $background.Dispose(); $shape.Dispose(); $glyph.Dispose(); $font.Dispose()
