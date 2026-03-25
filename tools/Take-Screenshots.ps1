<#
.SYNOPSIS
    Interactive screenshot capture for Audio2Image documentation.
    
.DESCRIPTION
    Semi-automated screenshot tool. For each screenshot, the script tells you
    what UI state to prepare, waits for you to press Enter, then captures the
    window using Win32 PrintWindow API.
    
    This approach is reliable with Avalonia apps (SendKeys doesn't work well
    with Avalonia's custom input system).
    
.NOTES
    Prerequisites:
    1. Audio2Image must be running with a populated library (at least a few tracks)
    2. Run this script from the project root directory
    3. PowerShell 5.1+ (Windows built-in) or PowerShell 7+
    
.EXAMPLE
    .\tools\Take-Screenshots.ps1
    
.EXAMPLE
    .\tools\Take-Screenshots.ps1 -ProcessName "Audio2Image" -Skip 3
#>

param(
    [string]$ProcessName = "Audio2Image",
    [string]$OutputDir = "docs\screenshots",
    [int]$Skip = 0
)

# --- Win32 API declarations ---
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class ScreenCapture
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public const int SW_RESTORE = 9;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    public static Bitmap CaptureWindow(IntPtr hWnd)
    {
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return null;

        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
        }
        return bmp;
    }

    public static IntPtr FindMainWindow(uint processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == processId && IsWindowVisible(hWnd))
            {
                int len = GetWindowTextLength(hWnd);
                if (len > 0)
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

# --- Helper functions ---

function Ensure-OutputDir {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
        Write-Host "[+] Created output directory: $OutputDir" -ForegroundColor Green
    }
}

function Find-AppWindow {
    $processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if (-not $processes) {
        Write-Host "[!] Process '$ProcessName' not found. Please start Audio2Image first." -ForegroundColor Red
        Write-Host "    Run: dotnet run --project src/Audio2Image.App/Audio2Image.App.csproj -c Release" -ForegroundColor Yellow
        exit 1
    }

    $proc = $processes | Select-Object -First 1
    Write-Host "[*] Found process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Cyan

    $hwnd = [ScreenCapture]::FindMainWindow([uint32]$proc.Id)
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host "[!] Could not find main window for PID $($proc.Id)" -ForegroundColor Red
        exit 1
    }

    Write-Host "[*] Window handle: 0x$($hwnd.ToString('X'))" -ForegroundColor Cyan
    return $hwnd
}

function Find-AllProcessWindows([uint32]$processId) {
    $windows = @()
    $callback = {
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        $pid = [uint32]0
        [ScreenCapture]::GetWindowThreadProcessId($hWnd, [ref]$pid) | Out-Null
        if ($pid -eq $processId -and [ScreenCapture]::IsWindowVisible($hWnd)) {
            $len = [ScreenCapture]::GetWindowTextLength($hWnd)
            if ($len -gt 0) {
                $sb = New-Object System.Text.StringBuilder($len + 1)
                [ScreenCapture]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
                $script:windows += [PSCustomObject]@{
                    Handle = $hWnd
                    Title  = $sb.ToString()
                }
            }
        }
        return $true
    }
    [ScreenCapture]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $windows
}

function Capture-Window([IntPtr]$hwnd, [string]$filename, [string]$description) {
    $filepath = Join-Path $OutputDir $filename
    
    Start-Sleep -Milliseconds 300
    
    $bmp = [ScreenCapture]::CaptureWindow($hwnd)
    if ($null -eq $bmp) {
        Write-Host "  [!] Failed to capture: $description" -ForegroundColor Red
        return $false
    }
    
    $bmp.Save($filepath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    
    $size = (Get-Item $filepath).Length / 1KB
    Write-Host "  [OK] $filename ($([math]::Round($size, 1)) KB)" -ForegroundColor Green
    return $true
}

function Capture-ForegroundOrMain([IntPtr]$mainHwnd, [string]$filename, [string]$description) {
    Start-Sleep -Milliseconds 300
    $fgHwnd = [ScreenCapture]::GetForegroundWindow()
    
    if ($fgHwnd -ne [IntPtr]::Zero -and $fgHwnd -ne $mainHwnd) {
        # A modal dialog is in front - capture it
        Write-Host "  [*] Detected separate window, capturing it" -ForegroundColor DarkCyan
        return Capture-Window $fgHwnd $filename $description
    } else {
        # Capture main window (overlay is part of main window)
        return Capture-Window $mainHwnd $filename $description
    }
}

function Wait-ForEnter([string]$prompt) {
    Write-Host ""
    Write-Host "  >> $prompt" -ForegroundColor Yellow
    Write-Host "  >> Press ENTER when ready (or 'S' to skip)..." -ForegroundColor DarkGray
    $input = Read-Host
    if ($input -eq 'S' -or $input -eq 's') {
        Write-Host "  [~] Skipped" -ForegroundColor DarkYellow
        return $false
    }
    return $true
}

# --- Screenshot definitions ---
$screenshots = @(
    @{
        Number      = 1
        Filename    = "screenshot-gallery.png"
        Description = "Main gallery view"
        Prompt      = "Show the GALLERY with several tracks loaded.`n     Make sure the viewer is CLOSED (press Esc).`n     The gallery should show thumbnails, tags, ratings, format badges."
        UseModal    = $false
    },
    @{
        Number      = 2
        Filename    = "screenshot-context-menu.png"
        Description = "Context menu"
        Prompt      = "RIGHT-CLICK on any gallery item to open the context menu.`n     Keep the menu OPEN and press Enter here."
        UseModal    = $false
    },
    @{
        Number      = 3
        Filename    = "screenshot-viewer.png"
        Description = "Spectrogram viewer"
        Prompt      = "CLICK on any gallery item to open the Spectrogram Viewer.`n     Wait for the spectrogram image to fully render."
        UseModal    = $false
    },
    @{
        Number      = 4
        Filename    = "screenshot-viewer-shortcuts.png"
        Description = "Viewer keyboard shortcuts (F1)"
        Prompt      = "With the viewer OPEN, press F1 to show the keyboard shortcuts overlay.`n     You should see the dark overlay with shortcut groups."
        UseModal    = $false
    },
    @{
        Number      = 5
        Filename    = "screenshot-gallery-shortcuts.png"
        Description = "Gallery keyboard shortcuts (F1)"
        Prompt      = "CLOSE the viewer (Esc), then press F1 in the gallery view.`n     You should see the gallery shortcuts overlay."
        UseModal    = $false
    },
    @{
        Number      = 6
        Filename    = "screenshot-settings.png"
        Description = "Settings window"
        Prompt      = "Open SETTINGS (Ctrl+, or click the gear icon in toolbar).`n     The Settings window should be visible with Analysis/Appearance/Storage groups."
        UseModal    = $true
    },
    @{
        Number      = 7
        Filename    = "screenshot-about.png"
        Description = "About window"
        Prompt      = "Close Settings, then open ABOUT (click the info icon in toolbar).`n     The About window should show version, author, tech stack."
        UseModal    = $true
    },
    @{
        Number      = 8
        Filename    = "screenshot-multiselect.png"
        Description = "Multi-select with batch actions"
        Prompt      = "Close any dialogs. In the gallery, SELECT MULTIPLE items:`n     Use Ctrl+A (select all) or Ctrl+Click on several items.`n     The batch action bar (Tag, Playlist, Delete) should appear at the top."
        UseModal    = $false
    },
    @{
        Number      = 9
        Filename    = "screenshot-search.png"
        Description = "Search filtering"
        Prompt      = "Deselect items (Esc), then press Ctrl+F and TYPE a search query`n     (e.g. 'rain' or 'drum'). The gallery should filter to matching results."
        UseModal    = $false
    },
    @{
        Number      = 10
        Filename    = "screenshot-playlists.png"
        Description = "Playlist sidebar"
        Prompt      = "Clear search (Esc), then click the PLAYLISTS button in the toolbar.`n     The playlist sidebar should appear on the right.`n     Create at least one playlist for a better screenshot."
        UseModal    = $false
    }
)

# --- Main execution ---

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Audio2Image - Interactive Screenshot Capture Tool       " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  This script will guide you through capturing 10 screenshots." -ForegroundColor White
Write-Host "  For each screenshot, it will tell you what to prepare in the app." -ForegroundColor White
Write-Host "  Prepare the state, then press Enter to capture." -ForegroundColor White
Write-Host "  Type 'S' + Enter to skip any screenshot." -ForegroundColor White
if ($Skip -gt 0) {
    Write-Host "  Skipping first $Skip screenshots (use -Skip N to change)." -ForegroundColor DarkYellow
}
Write-Host ""

Ensure-OutputDir
$hwnd = Find-AppWindow

$proc = Get-Process -Name $ProcessName | Select-Object -First 1
$processId = [uint32]$proc.Id

$captured = 0
$skipped = 0
$failed = 0

foreach ($shot in $screenshots) {
    $num = $shot.Number
    
    # Skip if requested
    if ($num -le $Skip) {
        Write-Host "[$num/10] $($shot.Description) - SKIPPED (--Skip)" -ForegroundColor DarkGray
        $skipped++
        continue
    }
    
    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    Write-Host "[$num/10] $($shot.Description)" -ForegroundColor White
    
    $ready = Wait-ForEnter $shot.Prompt
    if (-not $ready) {
        $skipped++
        continue
    }
    
    # Capture
    if ($shot.UseModal) {
        $ok = Capture-ForegroundOrMain $hwnd $shot.Filename $shot.Description
    } else {
        $ok = Capture-Window $hwnd $shot.Filename $shot.Description
    }
    
    if ($ok) {
        $captured++
    } else {
        $failed++
    }
}

# --- Summary ---
Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "  Screenshot capture complete!                           " -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Captured: $captured  |  Skipped: $skipped  |  Failed: $failed" -ForegroundColor White
Write-Host ""

$files = Get-ChildItem -Path $OutputDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name
if ($files) {
    Write-Host "  Files in $OutputDir`:" -ForegroundColor Cyan
    foreach ($f in $files) {
        $sizeKB = [math]::Round($f.Length / 1KB, 1)
        Write-Host "    $($f.Name)  ($sizeKB KB)" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "  TIP: Context menus render as separate popups in Avalonia." -ForegroundColor Yellow
Write-Host "  If the context menu screenshot is blank, use Win+Shift+S" -ForegroundColor Yellow
Write-Host "  to manually capture it with Windows Snipping Tool." -ForegroundColor Yellow
Write-Host ""
