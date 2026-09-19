# Generates Assets\app.ico (16-256 px, PNG-compressed frames) by drawing the icon as vectors
# at each size. Run with Windows PowerShell:  powershell -File tools\make-icon.ps1
# Pass -PreviewPath <file.png> to also write a contact sheet of every size.
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\Assets\app.ico'),
    [string]$PreviewPath
)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Color([string]$hex, [int]$alpha = 255) {
    [System.Drawing.Color]::FromArgb($alpha, [Convert]::ToInt32($hex.Substring(0, 2), 16),
        [Convert]::ToInt32($hex.Substring(2, 2), 16), [Convert]::ToInt32($hex.Substring(4, 2), 16))
}

function Fill-Rounded($g, $brush, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-RoundedRect $x $y $w $h $r
    $g.FillPath($brush, $p)
    $p.Dispose()
}

# Draws the icon in a 256x256 design space; the graphics transform scales it to $size.
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Blue tile
    $tile = New-Object System.Drawing.RectangleF 8, 8, 240, 240
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $tile, (New-Color '3D93F7'), (New-Color '1A4DBA'), 90.0
    Fill-Rounded $g $bg 8 8 240 240 54
    $bg.Dispose()

    $small = $size -le 32
    if ($small) { $rowH = 54; $gap = 12; $top = 36 } else { $rowH = 40; $gap = 14; $top = 54 }
    $white = New-Object System.Drawing.SolidBrush (New-Color 'FFFFFF' 240)
    $amber = New-Object System.Drawing.SolidBrush (New-Color 'FFC531')

    for ($i = 0; $i -lt 3; $i++) {
        $y = $top + $i * ($rowH + $gap)
        $selected = ($i -eq 1)

        # The selected row is a little wider and amber, so it reads as "being edited".
        if ($selected) { Fill-Rounded $g $amber 34 $y 188 $rowH 14 } else { Fill-Rounded $g $white 46 $y 164 $rowH 14 }

        if (-not $small) {
            $iconBrush = New-Object System.Drawing.SolidBrush (New-Color $(if ($selected) { '1A4DBA' } else { '2E74DC' }))
            $textBrush = New-Object System.Drawing.SolidBrush (New-Color $(if ($selected) { '7A5400' } else { '9DB6DD' }))
            $x0 = if ($selected) { 46 } else { 58 }
            Fill-Rounded $g $iconBrush $x0 ($y + 8) 24 24 6
            Fill-Rounded $g $textBrush ($x0 + 36) ($y + 15) 96 10 5
            $iconBrush.Dispose(); $textBrush.Dispose()
        }
    }
    $white.Dispose(); $amber.Dispose(); $g.Dispose()
    return $bmp
}

$frames = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray(); Bitmap = $bmp }
}

# ICO container: header, one directory entry per frame, then the PNG payloads.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()

$fullOut = [System.IO.Path]::GetFullPath($OutPath)
New-Item -ItemType Directory -Force (Split-Path $fullOut) | Out-Null
[System.IO.File]::WriteAllBytes($fullOut, $out.ToArray())
Write-Host "Wrote $fullOut ($($out.Length) bytes, sizes: $($sizes -join ', '))"

if ($PreviewPath) {
    # Contact sheet: every size at 1:1 plus a 2x zoom of the small ones, on light and dark backgrounds.
    $sheet = New-Object System.Drawing.Bitmap 760, 640
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 380, 640)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (New-Color '1F1F1F')), 380, 0, 380, 640)
    foreach ($half in 0, 380) {
        $g.DrawImage(($frames | Where-Object Size -eq 256).Bitmap, ($half + 62), 20, 256, 256)
        $x = $half + 20
        foreach ($f in $frames | Where-Object { $_.Size -le 64 }) {
            $g.DrawImage($f.Bitmap, $x, 300, $f.Size, $f.Size)
            $x += $f.Size + 12
        }
        $x = $half + 20
        foreach ($f in $frames | Where-Object { $_.Size -le 48 }) {
            $g.DrawImage($f.Bitmap, $x, 400, $f.Size * 2, $f.Size * 2)
            $x += $f.Size * 2 + 12
        }
    }
    $g.Dispose()
    $sheet.Save([System.IO.Path]::GetFullPath($PreviewPath), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote preview $PreviewPath"
}
foreach ($f in $frames) { $f.Bitmap.Dispose() }
