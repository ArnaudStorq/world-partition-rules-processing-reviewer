# Generates a multi-resolution application icon (PNG-compressed .ico) for WPRulesReviewer.
# Run with Windows PowerShell (System.Drawing).  Output: src/WPRulesReviewer.App/Assets/app.ico
param(
    [string]$OutPath = "$PSScriptRoot/../src/WPRulesReviewer.App/Assets/app.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'

    # Rounded-square background with a diagonal purple gradient.
    $pad = [Math]::Max(1, [int]($s * 0.06))
    $rectF = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad))
    $radius = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rectF.X, $rectF.Y, $d, $d, 180, 90)
    $path.AddArc($rectF.Right - $d, $rectF.Y, $d, $d, 270, 90)
    $path.AddArc($rectF.Right - $d, $rectF.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rectF.X, $rectF.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 124, 92, 240)   # #7C5CF0
    $c2 = [System.Drawing.Color]::FromArgb(255, 63, 47, 150)    # #3F2F96
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, $c1, $c2, 55.0)
    $g.FillPath($brush, $path)

    # Mark: the partition grid itself. A 3x3 of rounded cells with one cell lit, which stays
    # readable at 16 px where a lens + handle + check mark turns to mush.
    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $dim = [System.Drawing.Color]::FromArgb(150, 255, 255, 255)

    $gridSpan = [single]($s * 0.50)
    $gridX = [single](($s - $gridSpan) / 2.0)
    $gridY = [single](($s - $gridSpan) / 2.0)
    $gap = [single]($gridSpan * 0.14)
    $cell = [single](($gridSpan - 2 * $gap) / 3.0)
    $cellRadius = [single]([Math]::Max(0.6, $cell * 0.26))

    # The lit cell is off-centre (row 1, column 2): it reads as "one partition under review".
    $litRow = 1
    $litCol = 2

    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 3; $col++) {
            $x = [single]($gridX + $col * ($cell + $gap))
            $y = [single]($gridY + $row * ($cell + $gap))

            $cellPath = New-Object System.Drawing.Drawing2D.GraphicsPath
            $cd = $cellRadius * 2
            if ($cd -ge 1.0) {
                $cellPath.AddArc($x, $y, $cd, $cd, 180, 90)
                $cellPath.AddArc($x + $cell - $cd, $y, $cd, $cd, 270, 90)
                $cellPath.AddArc($x + $cell - $cd, $y + $cell - $cd, $cd, $cd, 0, 90)
                $cellPath.AddArc($x, $y + $cell - $cd, $cd, $cd, 90, 90)
                $cellPath.CloseFigure()
            }
            else {
                $cellPath.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $cell, $cell)))
            }

            $isLit = ($row -eq $litRow -and $col -eq $litCol)
            $cellBrush = New-Object System.Drawing.SolidBrush($(if ($isLit) { $white } else { $dim }))
            $g.FillPath($cellBrush, $cellPath)
            $cellBrush.Dispose()
            $cellPath.Dispose()
        }
    }

    $g.Dispose()
    return $bmp
}

# Encode every size as PNG, then assemble a PNG-based .ico (Vista+).
$pngList = New-Object System.Collections.Generic.List[byte[]]
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngList.Add($ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$fs = [System.IO.File]::Open($OutPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)

$count = $sizes.Count
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$count) # image count

$offset = 6 + (16 * $count)
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $png = $pngList[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)   # width
    $bw.Write([byte]$dim)   # height
    $bw.Write([byte]0)      # palette
    $bw.Write([byte]0)      # reserved
    $bw.Write([UInt16]1)    # color planes
    $bw.Write([UInt16]32)   # bits per pixel
    $bw.Write([UInt32]$png.Length)
    $bw.Write([UInt32]$offset)
    $offset += $png.Length
}
foreach ($png in $pngList) { $bw.Write($png) }

$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "Icon written to $OutPath ($count sizes)"
