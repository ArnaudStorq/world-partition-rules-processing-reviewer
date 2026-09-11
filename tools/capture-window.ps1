# Captures the WPRulesReviewer main window to a PNG, for visual checks during development.
param(
    [string]$ProcessName = 'WPRulesReviewer',
    [string]$Out = "$env:TEMP\wprules-shot.png"
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Error "Window not found for process '$ProcessName'."; exit 1 }

$h = $proc.MainWindowHandle
[void][Win32]::ShowWindow($h, 9)      # SW_RESTORE
[void][Win32]::BringWindowToTop($h)
[void][Win32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 700
[void][Win32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 400

# DWMWA_EXTENDED_FRAME_BOUNDS: the visible bounds, without the invisible resize border.
$r = New-Object Win32+RECT
[void][Win32]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)

# PW_RENDERFULLCONTENT (2) asks DWM for the composed surface, so the capture is correct even when
# another window overlaps ours. CopyFromScreen is only the fallback for the odd driver that refuses.
$hdc = $g.GetHdc()
$ok = [Win32]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size) }
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output $Out
