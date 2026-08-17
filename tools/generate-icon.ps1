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

    # Magnifying glass = review lens.
    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $penW = [single]([Math]::Max(1.0, $s * 0.075))
    $pen = New-Object System.Drawing.Pen($white, $penW)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'

    $lensD = [single]($s * 0.42)
    $lensX = [single]($s * 0.24)
    $lensY = [single]($s * 0.22)
    $g.DrawEllipse($pen, $lensX, $lensY, $lensD, $lensD)

    # Handle.
    $hx1 = [single]($lensX + $lensD * 0.86)
    $hy1 = [single]($lensY + $lensD * 0.86)
    $hx2 = [single]($s * 0.80)
    $hy2 = [single]($s * 0.80)
    $g.DrawLine($pen, $hx1, $hy1, $hx2, $hy2)

    # Check mark inside the lens = "reviewed / ok".
    if ($s -ge 24) {
        $penC = New-Object System.Drawing.Pen($white, [single]([Math]::Max(1.0, $s * 0.06)))
        $penC.StartCap = 'Round'; $penC.EndCap = 'Round'; $penC.LineJoin = 'Round'
        $cx = $lensX + $lensD / 2.0
        $cy = $lensY + $lensD / 2.0
        $pts = @(
            (New-Object System.Drawing.PointF([single]($cx - $lensD * 0.22), [single]($cy + $lensD * 0.02))),
            (New-Object System.Drawing.PointF([single]($cx - $lensD * 0.05), [single]($cy + $lensD * 0.18))),
            (New-Object System.Drawing.PointF([single]($cx + $lensD * 0.26), [single]($cy - $lensD * 0.20)))
        )
        $g.DrawLines($penC, $pts)
        $penC.Dispose()
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
