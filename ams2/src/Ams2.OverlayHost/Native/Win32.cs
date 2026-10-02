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

    public const int SW_SHOWNOACTIVATE = 4;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_NCHITTEST = 0x0084;
    public const nint MA_NOACTIVATE = 3;
    public const nint HTTRANSPARENT = -1;
    public const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000;

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
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
