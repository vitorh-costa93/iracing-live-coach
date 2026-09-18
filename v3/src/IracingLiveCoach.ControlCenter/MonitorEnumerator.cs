using System.Runtime.InteropServices;

namespace IracingLiveCoach.ControlCenter;

public sealed record MonitorRect(int Left, int Top, int Width, int Height, bool Primary);

/// <summary>Lists physical monitors in virtual-desktop pixels via EnumDisplayMonitors.</summary>
public static class MonitorEnumerator
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT lprc, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc proc, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX info);

    public static List<MonitorRect> GetMonitors()
    {
        var result = new List<MonitorRect>();
        EnumDisplayMonitors(0, 0, (nint hMonitor, nint hdc, ref RECT rc, nint data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            bool primary = GetMonitorInfoW(hMonitor, ref info) && (info.dwFlags & 1) != 0;
            result.Add(new MonitorRect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, primary));
            return true;
        }, 0);
        return result.OrderByDescending(m => m.Primary).ThenBy(m => m.Left).ToList();
    }
}
