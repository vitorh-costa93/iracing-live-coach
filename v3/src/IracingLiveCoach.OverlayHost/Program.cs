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
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Assets;
using IracingLiveCoach.OverlayHost.Layout;
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
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_T = 0x54;
    private const int VK_E = 0x45;

    private static bool _clickThrough = true;
    private static bool _editMode;
    private static readonly List<nint> OverlayWindows = [];
    private static bool _simulating;

    private const string StandingsKey = "standings";
    private const string RelativeKey = "relative";
    private const string WeatherKey = "weather";
    private const string FuelKey = "fuel";
    private const string RadarKey = "radar";
    private const string StartHelperKey = "start-helper";
    private static readonly WidgetPlacementStore PlacementStore = new();

    // Drag state belongs to the HWND under the cursor. Each widget has its own native window,
    // so movement never carries unrelated overlay content or transparent padding with it.
    private static nint _draggingWindow;
    private static (int X, int Y) _dragStartMouse;
    private static (int X, int Y) _dragStartWindow;

    /// <summary>Spec §12's "preview com dados fictícios claramente identificado como simulação" --
    /// enough synthetic rows to exercise every column this widget draws (leader with no gap,
    /// positive and negative iRating deltas, a faster and a slower lap-delta, the player's own
    /// row). Never used as a stand-in for real telemetry.</summary>
    private static List<StandingsRow> BuildSimulatedStandingsRows() =>
    [
        new StandingsRow(1, "Max Verstappen", 12, 88.412, null, false, "🇳🇱", "A", null, 4820, 1,
            "RedBull", null, 14.2, -0.412, "GT3", "#FFD400", 1, null, null, null, null, false, "—"),
        new StandingsRow(2, "Lewis Hamilton", 12, 88.901, null, false, "🇬🇧", "A", null, 4650, 1,
            "Mercedes", 1.8, -3.6, 0.077, "GT3", "#FFD400", 2, 1.8, null, null, null, false, "—"),
        new StandingsRow(3, "Vitor Costa", 12, 89.150, null, true, "🇧🇷", "B", null, 3200, 1,
            "Ferrari", 3.1, 0.0, 0.0, "GT3", "#FFD400", 3, 1.3, null, null, null, false, "—"),
        new StandingsRow(4, "Charles Leclerc", 11, 89.740, null, false, "🇲🇨", "A", null, 4400, 1,
            "Ferrari", 12.6, 5.9, 0.590, "GT3", "#FFD400", 4, 9.5, null, null, null, false, "—"),
    ];

    /// <summary>Same rationale as <see cref="BuildSimulatedStandingsRows"/> -- a 7-row preset with
    /// the player centered, exercising positive and negative offsets and gaps.</summary>
    private static List<RelativeRow> BuildSimulatedRelativeRows() =>
    [
        new RelativeRow(-3, "Oliver Wilson", -8.912, null, null, null, null, false, "🇬🇧", "A", null, 4100, 1, "Aston Martin", false, 1, "GT3", "#FFD400"),
        new RelativeRow(-2, "Max Hoffmann", -5.201, null, null, null, null, false, "🇩🇪", "A", null, 3980, 1, "BMW", false, 2, "GT3", "#FFD400"),
        new RelativeRow(-1, "Vitor Costa", -1.892, null, null, null, null, false, "🇧🇷", "B", null, 3200, 1, "Ferrari", false, 3, "GT3", "#FFD400"),
        new RelativeRow(0, "Vitor Costa", 0, null, null, null, null, false, "🇧🇷", "B", null, 3200, 1, "Ferrari", true, 4, "GT3", "#FFD400"),
        new RelativeRow(1, "Daniel Walker", 1.304, null, null, null, null, false, "🇺🇸", "A", null, 3012, 1, "Mercedes", false, 5, "GT3", "#FFD400"),
        new RelativeRow(2, "Simon Wagner", 2.910, null, null, null, null, false, "🇩🇪", "A", null, 3455, 1, "Ford", false, 6, "GT3", "#FFD400"),
        new RelativeRow(3, "James Carter", 5.330, null, null, null, null, false, "🇺🇸", "B", null, 3298, 1, "McLaren", false, 7, "GT3", "#FFD400"),
    ];

    public static int Main()
    {
        Console.WriteLine("V3 Phase 3: SPACE=click-through, E=edit mode (drag widgets), T=simulation, ESC=exit.");
        LoadPrivateFonts();

        // Each widget owns its own top-level GPU surface. The dimensions are content-oriented
        // starting values; Phase 5 will drive the same values through the Control Center.
        PlacementStore.Set(StandingsKey, new WidgetPlacement(0, 200, 200, PlacementAnchor.TopLeft, 820, 260, 1f, false, 0));
        PlacementStore.Set(RelativeKey, new WidgetPlacement(0, 200, 470, PlacementAnchor.TopLeft, 760, 200, 1f, false, 1));
        PlacementStore.Set(WeatherKey, new WidgetPlacement(0, 980, 470, PlacementAnchor.TopLeft, 280, 116, 1f, false, 2));
        PlacementStore.Set(FuelKey, new WidgetPlacement(0, 980, 595, PlacementAnchor.TopLeft, 300, 138, 1f, false, 3));
        PlacementStore.Set(RadarKey, new WidgetPlacement(0, 980, 745, PlacementAnchor.TopLeft, 180, 42, 1f, false, 4));
        PlacementStore.Set(StartHelperKey, new WidgetPlacement(0, 980, 795, PlacementAnchor.TopLeft, 280, 50, 1f, false, 5));

        nint hInstance = GetModuleHandleW(null);
        WndProcDelegate wndProc = WndProc;
        nint wndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProc);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = wndProcPtr,
            hInstance = hInstance,
            lpszClassName = "IracingLiveCoach.OverlayHost.MainWindow",
            hCursor = LoadCursorW(0, (nint)32512) // IDC_ARROW
        };
        ushort atom = RegisterClassExW(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");

        var standingsPlacement = PlacementStore.Get(StandingsKey)!;
        var relativePlacement = PlacementStore.Get(RelativeKey)!;
        var weatherPlacement = PlacementStore.Get(WeatherKey)!;
        var fuelPlacement = PlacementStore.Get(FuelKey)!;
        var radarPlacement = PlacementStore.Get(RadarKey)!;
        var startPlacement = PlacementStore.Get(StartHelperKey)!;
        nint standingsHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Standings", standingsPlacement);
        nint relativeHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Relative", relativePlacement);
        nint weatherHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Weather", weatherPlacement);
        nint fuelHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Fuel", fuelPlacement);
        nint radarHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Radar", radarPlacement);
        nint startHwnd = CreateOverlayWindow(wc.lpszClassName, hInstance, "Live Coach — Start Helper", startPlacement);
        OverlayWindows.Add(standingsHwnd);
        OverlayWindows.Add(relativeHwnd);
        OverlayWindows.Add(weatherHwnd);
        OverlayWindows.Add(fuelHwnd);
        OverlayWindows.Add(radarHwnd); OverlayWindows.Add(startHwnd);

        using var standingsResources = DeviceResources.Create(standingsHwnd, (int)standingsPlacement.WidthDip, (int)standingsPlacement.HeightDip);
        using var relativeResources = DeviceResources.Create(relativeHwnd, (int)relativePlacement.WidthDip, (int)relativePlacement.HeightDip);
        using var weatherResources = DeviceResources.Create(weatherHwnd, (int)weatherPlacement.WidthDip, (int)weatherPlacement.HeightDip);
        using var fuelResources = DeviceResources.Create(fuelHwnd, (int)fuelPlacement.WidthDip, (int)fuelPlacement.HeightDip);
        using var radarResources = DeviceResources.Create(radarHwnd, (int)radarPlacement.WidthDip, (int)radarPlacement.HeightDip);
        using var startResources = DeviceResources.Create(startHwnd, (int)startPlacement.WidthDip, (int)startPlacement.HeightDip);
        standingsResources.SetClickThrough(standingsHwnd, _clickThrough);
        relativeResources.SetClickThrough(relativeHwnd, _clickThrough);
        weatherResources.SetClickThrough(weatherHwnd, _clickThrough);
        fuelResources.SetClickThrough(fuelHwnd, _clickThrough);
        radarResources.SetClickThrough(radarHwnd, _clickThrough); startResources.SetClickThrough(startHwnd, _clickThrough);

        using var standingsFlags = new FlagBitmapCache(standingsResources.Context);
        using var relativeFlags = new FlagBitmapCache(relativeResources.Context);
        using var standings = new StandingsWidget(standingsResources.Context, standingsResources.DWriteFactory, standingsFlags);
        using var relative = new RelativeWidget(relativeResources.Context, relativeResources.DWriteFactory, relativeFlags);
        using var weather = new WeatherWidget(weatherResources.Context, weatherResources.DWriteFactory);
        using var fuel = new FuelWidget(fuelResources.Context, fuelResources.DWriteFactory);
        using var radar = new RadarWidget(radarResources.Context, radarResources.DWriteFactory);
        using var start = new StartHelperWidget(startResources.Context, startResources.DWriteFactory);
        standingsResources.DeviceRecovered += () => standingsFlags.Recreate(standingsResources.Context);
        relativeResources.DeviceRecovered += () => relativeFlags.Recreate(relativeResources.Context);

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

            if (_simulating)
            {
                standings.SetSimulatedRows(BuildSimulatedStandingsRows());
                relative.SetSimulatedRows(BuildSimulatedRelativeRows());
            }
            else
            {
                standings.SetSimulatedRows(null);
                relative.SetSimulatedRows(null);
            }

            standingsResources.BeginFrame();
            standings.Draw(standingsResources.Context, 0, 0);
            if (_editMode)
                DrawEditModeOutlines(standingsResources.Context, (0, 0, standingsPlacement.WidthDip, standingsPlacement.HeightDip));
            if (!standingsResources.EndFrame())
                Console.WriteLine("Device lost detected -- recovered without restart.");

            relativeResources.BeginFrame();
            relative.Draw(relativeResources.Context, 0, 0);
            if (_editMode)
                DrawEditModeOutlines(relativeResources.Context, (0, 0, relativePlacement.WidthDip, relativePlacement.HeightDip));
            if (!relativeResources.EndFrame())
                Console.WriteLine("Device lost detected -- recovered without restart.");

            weatherResources.BeginFrame();
            weather.Draw(weatherResources.Context, 0, 0, weatherPlacement.WidthDip);
            if (_editMode)
                DrawEditModeOutlines(weatherResources.Context, (0, 0, weatherPlacement.WidthDip, weatherPlacement.HeightDip));
            if (!weatherResources.EndFrame())
                Console.WriteLine("Device lost detected -- recovered without restart.");

            fuelResources.BeginFrame();
            fuel.Draw(fuelResources.Context, 0, 0, fuelPlacement.WidthDip);
            if (_editMode) DrawEditModeOutlines(fuelResources.Context, (0, 0, fuelPlacement.WidthDip, fuelPlacement.HeightDip));
            if (!fuelResources.EndFrame()) Console.WriteLine("Device lost detected -- recovered without restart.");
            radarResources.BeginFrame(); radar.Draw(radarResources.Context, 0, 0, radarPlacement.WidthDip); radarResources.EndFrame();
            startResources.BeginFrame(); start.Draw(startResources.Context, 0, 0, startPlacement.WidthDip); startResources.EndFrame();

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

    /// <summary>Spec §4: "exibindo limites e alças apenas durante edição" -- a thin outline around
    /// each widget's current bounds, visible only while edit mode is on. Creates its brush per call
    /// rather than caching one: this only runs while a human is actively dragging in edit mode, far
    /// below any frame-budget concern, and keeps this debug-only path self-contained.</summary>
    private static void DrawEditModeOutlines(ID2D1DeviceContext* dc, params (float Left, float Top, float Width, float Height)[] rects)
    {
        var outline = Theme.PaletteTokens.FocusOutline;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        if (dc->CreateSolidColorBrush(&outline, null, brush.GetAddressOf()).Failure) return;
        try
        {
            foreach (var r in rects)
            {
                var rect = new Vortice.Win32.Numerics.RectF(r.Left - 2, r.Top - 2, r.Left + r.Width + 2, r.Top + r.Height + 2);
                dc->DrawRectangle(&rect, (ID2D1Brush*)brush.Get(), 1.5f, null);
            }
        }
        finally { brush.Dispose(); }
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

    private static void SetEditMode(bool enabled)
    {
        _editMode = enabled;
        // Edit mode needs real mouse input to reach the window, so click-through is forced off
        // while editing and restored to its prior state on exit (spec §4's "modo corrida com
        // click-through... atalho para entrar/sair do modo edição").
        foreach (var hwnd in OverlayWindows)
            DeviceResources.ApplyClickThrough(hwnd, enabled ? false : _clickThrough);
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_KEYDOWN:
                if ((int)wParam == VK_ESCAPE)
                {
                    foreach (var overlay in OverlayWindows.ToArray()) DestroyWindow(overlay);
                    PostQuitMessage(0);
                }
                else if ((int)wParam == VK_SPACE)
                {
                    _clickThrough = !_clickThrough;
                    if (!_editMode)
                        foreach (var overlay in OverlayWindows) DeviceResources.ApplyClickThrough(overlay, _clickThrough);
                    Console.WriteLine($"Click-through: {_clickThrough}");
                }
                else if ((int)wParam == VK_T)
                {
                    _simulating = !_simulating;
                    Console.WriteLine($"Simulation preview: {_simulating}");
                }
                else if ((int)wParam == VK_E)
                {
                    SetEditMode(!_editMode);
                    Console.WriteLine($"Edit mode: {_editMode}");
                }
                return 0;
            case WM_LBUTTONDOWN:
                if (_editMode)
                {
                    int mx = unchecked((short)(lParam & 0xFFFF));
                    int my = unchecked((short)((lParam >> 16) & 0xFFFF));
                    GetWindowRect(hwnd, out var rect);
                    _draggingWindow = hwnd;
                    _dragStartMouse = (mx, my);
                    _dragStartWindow = (rect.Left, rect.Top);
                }
                return 0;
            case WM_MOUSEMOVE:
                if (_editMode && _draggingWindow == hwnd)
                {
                    int mx = unchecked((short)(lParam & 0xFFFF));
                    int my = unchecked((short)((lParam >> 16) & 0xFFFF));
                    float dx = mx - _dragStartMouse.X;
                    float dy = my - _dragStartMouse.Y;
                    SetWindowPos(hwnd, 0, _dragStartWindow.X + (int)dx, _dragStartWindow.Y + (int)dy, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
                return 0;
            case WM_LBUTTONUP:
                _draggingWindow = 0;
                return 0;
            case WM_DESTROY:
                OverlayWindows.Remove(hwnd);
                if (OverlayWindows.Count == 0) PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static nint CreateOverlayWindow(string className, nint hInstance, string title, WidgetPlacement placement)
    {
        nint hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT,
            className, title, WS_POPUP | WS_VISIBLE,
            (int)placement.X, (int)placement.Y, (int)placement.WidthDip, (int)placement.HeightDip,
            0, 0, hInstance, 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
        ShowWindow(hwnd, SW_SHOW);
        return hwnd;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint hWnd, int nIndex);
    [DllImport("user32.dll")] internal static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceExW(string fileName, uint flags, nint reserved);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    /// <summary>Registers the bundled, OFL-licensed Barlow Semi Condensed files privately for
    /// this process only. No system font installation or global Windows state is changed.</summary>
    private static void LoadPrivateFonts()
    {
        const uint FR_PRIVATE = 0x10;
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        foreach (string file in new[] { "BarlowSemiCondensed-Regular.ttf", "BarlowSemiCondensed-SemiBold.ttf" })
        {
            string path = Path.Combine(directory, file);
            if (File.Exists(path)) AddFontResourceExW(path, FR_PRIVATE, 0);
        }
    }
}
