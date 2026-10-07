using System.Runtime.InteropServices;

namespace Ams2.OverlayHost.Native;

/// <summary>P/Invoke mínimo para a janela do overlay (mesmo padrão do V3).</summary>
internal static class Win32
{
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const int GWL_EXSTYLE = -20;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const nint MA_NOACTIVATE = 3;
    public const nint HTTRANSPARENT = -1;
    public const nint HTCLIENT = 1;
    public const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000;
    public const int VK_MENU = 0x12;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public static readonly nint HWND_TOPMOST = -1;
    public const int IDC_SIZEALL = 32646, IDC_SIZENWSE = 32642;

    public delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int ptX, ptY; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    // Temporização de alta resolução e sincronismo com o vblank do DWM.
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
    [DllImport("kernel32.dll")] public static extern nint GetCurrentProcess();
    [StructLayout(LayoutKind.Sequential)] public struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] public static extern bool SetProcessInformation(nint process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(nint dc, int index);

    /// <summary>Taxa de atualização do monitor principal (Hz); 60 se não der para saber.</summary>
    public static int PrimaryRefreshHz()
    {
        nint dc = GetDC(0);
        if (dc == 0) return 60;
        int hz = GetDeviceCaps(dc, 116); // VREFRESH
        ReleaseDC(0, dc);
        return hz is >= 24 and <= 1000 ? hz : 60;
    }

    /// <summary>Timer de 1 ms e sem "power throttling" do processo (no Windows 11 o timer fino é ignorado em processo em segundo plano
    /// se não houver este opt-out). Chamar uma vez antes do laço de render; desfazer com <see cref="EndHighResTimer"/>.</summary>
    public static void BeginHighResTimer()
    {
        timeBeginPeriod(1);
        var st = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1 | 4, StateMask = 0 }; // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION: desligados
        SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref st, System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
    }
    public static void EndHighResTimer() => timeEndPeriod(1);

    [DllImport("kernel32.dll")] public static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(int exStyle, string cls, string title, int style, int x, int y, int w, int h,
        nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(ref MSG msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(int pid);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern nint LoadCursorW(nint inst, nint name);
    [DllImport("user32.dll")] public static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
