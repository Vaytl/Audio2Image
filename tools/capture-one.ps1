# Single screenshot capture tool
# Usage: .\tools\capture-one.ps1 -Name "screenshot-gallery" [-CaptureModal]
param(
    [Parameter(Mandatory=$true)]
    [string]$Name,
    [switch]$CaptureModal,
    [string]$ProcessName = "Audio2Image",
    [string]$OutputDir = "docs\screenshots"
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class SC
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint PW_RENDERFULLCONTENT = 2;

    public static Bitmap CaptureWindow(IntPtr hWnd)
    {
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return null;

        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
        }
        return bmp;
    }

    public static IntPtr FindMainWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint wpid;
            GetWindowThreadProcessId(hWnd, out wpid);
            if (wpid == pid && IsWindowVisible(hWnd))
            {
                if (GetWindowTextLength(hWnd) > 0)
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@ -ReferencedAssemblies System.Drawing -ErrorAction Stop

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Host "ERROR: $ProcessName not running" -ForegroundColor Red
    exit 1
}

$mainHwnd = [SC]::FindMainWindow([uint32]$proc.Id)

# Decide which window to capture
$targetHwnd = $mainHwnd

if ($CaptureModal) {
    Start-Sleep -Milliseconds 300
    $fgHwnd = [SC]::GetForegroundWindow()
    if ($fgHwnd -ne [IntPtr]::Zero -and $fgHwnd -ne $mainHwnd) {
        $targetHwnd = $fgHwnd
        Write-Host "Capturing foreground modal window" -ForegroundColor Cyan
    }
}

Start-Sleep -Milliseconds 500

$bmp = [SC]::CaptureWindow($targetHwnd)
if (-not $bmp) {
    Write-Host "ERROR: Failed to capture window" -ForegroundColor Red
    exit 1
}

# Crop title bar (31px) and 1px border
$cropTop = 31
$cropSide = 1
$cropBottom = 1
$newW = $bmp.Width - $cropSide * 2
$newH = $bmp.Height - $cropTop - $cropBottom
$rect = New-Object System.Drawing.Rectangle($cropSide, $cropTop, $newW, $newH)
$cropped = $bmp.Clone($rect, $bmp.PixelFormat)
$bmp.Dispose()

$outPath = Join-Path $OutputDir "$Name.png"
$cropped.Save((Join-Path (Get-Location) $outPath), [System.Drawing.Imaging.ImageFormat]::Png)

$kb = [math]::Round((Get-Item $outPath).Length / 1KB, 1)
Write-Host "OK: $outPath  $($cropped.Width)x$($cropped.Height)  $kb KB" -ForegroundColor Green
$cropped.Dispose()
