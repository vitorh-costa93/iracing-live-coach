// Phase 0 technical proof (see docs/superpowers/plans/2026-09-17-v3-overlay-rearchitecture-plan.md).
// Goal: a genuinely GPU-composited, per-pixel-transparent, click-through-toggleable overlay window,
// replacing WPF's UpdateLayeredWindow software compositor with a real DirectComposition visual tree.
// Everything here is unsafe/raw COM interop because the actively maintained Vortice bindings that
// include DirectWrite (Vortice.Win32.Graphics.* family) are generated straight from Win32 metadata,
// with no managed convenience wrappers -- unlike the older Vortice.Direct2D1/DXGI/Direct3D11 family,
// which has none of these types at all (see the plan's Step 1 for how that was confirmed).
using System.Runtime.InteropServices;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D11;
using Vortice.Win32.Graphics.DirectComposition;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Graphics.Dxgi;
using Vortice.Win32.Graphics.Dxgi.Common;
using IracingLiveCoach.OverlayHost.Widgets;
using static Vortice.Win32.Apis;
using static Vortice.Win32.Graphics.Direct2D.Apis;
using static Vortice.Win32.Graphics.Direct3D11.Apis;
using static Vortice.Win32.Graphics.DirectComposition.Apis;
using static Vortice.Win32.Graphics.DirectWrite.Apis;
using static Vortice.Win32.Graphics.Dxgi.Apis;
using DWriteFactoryType = Vortice.Win32.Graphics.DirectWrite.FactoryType;
using D2DFactoryType = Vortice.Win32.Graphics.Direct2D.FactoryType;

namespace IracingLiveCoach.OverlayHost;

public static unsafe class Program
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int SW_SHOW = 5;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_KEYDOWN = 0x0100;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;

    private static bool _clickThrough = true;
    private static nint _hwnd;

    public static int Main()
    {
        Console.WriteLine("V3 Phase 3 Standings: SPACE toggles click-through, ESC exits.");

        nint hInstance = GetModuleHandleW(null);
        WndProcDelegate wndProc = WndProc;
        nint wndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProc);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = wndProcPtr,
            hInstance = hInstance,
            lpszClassName = "IracingLiveCoach.OverlayHost.StandingsWindow",
            hCursor = LoadCursorW(0, (nint)32512) // IDC_ARROW
        };
        ushort atom = RegisterClassExW(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");

        int width = 400, height = 320;
        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT,
            wc.lpszClassName,
            "V3 Standings",
            WS_POPUP | WS_VISIBLE,
            200, 200, width, height,
            0, 0, hInstance, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");

        ShowWindow(_hwnd, SW_SHOW);

        using var resources = DeviceResources.Create(_hwnd, width, height);
        resources.SetClickThrough(_hwnd, _clickThrough);

        using var standings = new StandingsWidget(resources.Context, resources.DWriteFactory);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var frameTimes = new List<double>(20000);
        double lastFrameMs = sw.Elapsed.TotalMilliseconds;
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iracing-live-coach", "v3-phase3-pacing.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        MSG msg = default;
        bool running = true;
        while (running)
        {
            while (PeekMessageW(ref msg, 0, 0, 0, 1))
            {
                if (msg.message == WM_DESTROY) { running = false; break; }
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            if (!running) break;

            double now = sw.Elapsed.TotalMilliseconds;
            double delta = now - lastFrameMs;
            lastFrameMs = now;
            frameTimes.Add(delta);

            resources.BeginFrame();
            standings.Draw(resources.Context, x: 8, y: 8);
            if (!resources.EndFrame())
                Console.WriteLine("Device lost detected -- recovered without restart.");

            if (frameTimes.Count >= 600) // ~10s @ 60Hz worth of samples per flush
            {
                WriteFindings(logPath, frameTimes);
                frameTimes.Clear();
            }
        }

        if (frameTimes.Count > 0)
            WriteFindings(logPath, frameTimes);

        return 0;
    }

    private static void WriteFindings(string path, List<double> frameTimesMs)
    {
        var sorted = frameTimesMs.OrderBy(x => x).ToList();
        double P(double pct)
        {
            int idx = (int)Math.Clamp(pct * (sorted.Count - 1), 0, sorted.Count - 1);
            return sorted[idx];
        }
        File.AppendAllText(path,
            $"{DateTime.UtcNow:O} samples={sorted.Count} p50={P(0.50):F2}ms p95={P(0.95):F2}ms p99={P(0.99):F2}ms max={sorted[^1]:F2}ms{Environment.NewLine}");
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_KEYDOWN:
                if ((int)wParam == VK_ESCAPE)
                {
                    DestroyWindow(hwnd);
                    PostQuitMessage(0);
                }
                else if ((int)wParam == VK_SPACE)
                {
                    _clickThrough = !_clickThrough;
                    DeviceResources.ApplyClickThrough(hwnd, _clickThrough);
                    Console.WriteLine($"Click-through: {_clickThrough}");
                }
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("kernel32.dll")] private static extern nint GetModuleHandleW(string? lpModuleName);
    [DllImport("user32.dll")] private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(ref MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint hInstance, nint lpCursorName);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint hWnd, int nIndex);
    [DllImport("user32.dll")] internal static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);
}
