using System;
using System.Runtime.InteropServices;

namespace IracingLiveCoach.App;

/// <summary>Toggles WS_EX_TRANSPARENT on the overlay's own HWND -- WPF has no managed API for
/// click-through, this is the standard Win32 way. When set, every mouse click passes straight
/// through the (already visually transparent, AllowsTransparency=True) window to whatever iRacing
/// itself is rendering underneath, instead of the overlay eating the click. Locked by default (see
/// MainWindow's tray-icon menu) so the overlay never gets in the way of actually driving; the
/// driver unlocks it only to drag/resize, then locks it again.</summary>
internal static class ClickThrough
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void Set(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        var next = enabled ? (style | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (style & ~WS_EX_TRANSPARENT);
        SetWindowLong(hwnd, GWL_EXSTYLE, next);
    }
}
