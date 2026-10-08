using System.Runtime.InteropServices;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Graphics.Direct2D.Apis;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost;
using IracingLiveCoach.OverlayHost.Assets;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Theme;
using IracingLiveCoach.OverlayHost.Widgets;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// The Control Center's "Preview ao vivo" (spec §12): a REAL Direct2D surface running the same
/// widget classes (<see cref="StandingsWidget"/>, <see cref="RelativeWidget"/>) the live overlay
/// uses -- not a second, hand-drawn WPF mockup of them (spec §3/§12: "Preview e overlay real devem
/// compartilhar o mesmo motor de layout e renderização. Não mantenha uma segunda implementação
/// visual divergente.")
///
/// NOT implemented as a WPF <c>HwndHost</c> child window: a first attempt at that failed at runtime
/// -- <c>IDCompositionDesktopDevice::CreateTargetForHwnd</c> returned E_INVALIDARG (0x80070057) for
/// a WS_CHILD window. OverlayHost.exe's own <see cref="Program"/> only ever targets DirectComposition
/// at WS_POPUP top-level windows created with WS_EX_NOREDIRECTIONBITMAP, and that combination is what
/// actually works here too. So instead this creates its own top-level, borderless popup window
/// OWNED by the Control Center's main window (so it minimizes/closes with it, per §12: "fechar o
/// painel não fecha os overlays" -- that rule is about the separate OverlayHost process, not this
/// preview surface, which legitimately should disappear with the panel) and repositioned every time
/// its WPF anchor element moves or resizes, so it visually tracks as if embedded.
///
/// Data shown is ALWAYS the spec §12-mandated simulated preset ("preview com dados fictícios
/// claramente identificado como simulação, disponível sem iRacing aberto") -- each widget's own
/// simulated-data setters always win over live telemetry in their own Draw() methods. All six
/// widgets are constructed with telemetry disabled, so the preview does not start SDK readers
/// even when iRacing is running or its device chain is recreated after a resize.
///
/// Also honest: layout/position/scale come from whatever <see cref="PlacementPersistence"/> last
/// saved to disk (read once at construction), not a live cross-process read of the running overlay.
/// </summary>
public sealed unsafe class OverlayPreviewHost : IDisposable
{
    private const string ClassName = "IracingLiveCoach.ControlCenter.PreviewSurface";
    private static bool _classRegistered;

    private nint _hwnd;
    private int _width;
    private int _height;
    private DeviceResources? _device;
    private FlagBitmapCache? _flags;
    private StandingsWidget? _standings;
    private RelativeWidget? _relative;
    private WeatherWidget? _weather;
    private FuelWidget? _fuel;
    private ComPtr<ID2D1SolidColorBrush> _backdropBrush;
    private (int X, int Y, int Width, int Height) _lastRect;
    private readonly WidgetPlacementStore _placements = new();
    private WidgetPlacementStore? _profile;
    private static readonly string[] PreviewKeys = ["standings", "relative", "weather", "fuel", "radar", "start-helper"];
    private RadarWidget? _radar;
    private StartHelperWidget? _start;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public OverlayPreviewHost(nint ownerHwnd, int x, int y, int width, int height)
    {
        EnsureClassRegistered();
        Program.LoadPrivateFonts();

        // Same hardcoded starting values as OverlayHost's own Program.cs (necessarily duplicated --
        // separate processes, spec §3) so the preview shows something sane even before
        // OverlayHost.exe has ever run once to create v3-layout.json.
        _placements.Set("standings", new WidgetPlacement(0, 28, 30, PlacementAnchor.TopLeft, 800, 264, 1f, false, 0));
        _placements.Set("relative", new WidgetPlacement(0, 1440, 740, PlacementAnchor.TopLeft, 470, 262, 1f, false, 1));
        _placements.Set("weather", new WidgetPlacement(0, 1590, 30, PlacementAnchor.TopLeft, 300, 118, 1f, false, 2));
        _placements.Set("fuel", new WidgetPlacement(0, 1270, 30, PlacementAnchor.TopLeft, 310, 118, 1f, false, 3));
        _placements.Set("radar", new WidgetPlacement(0, 900, 640, PlacementAnchor.TopLeft, 120, 190, 1f, false, 4));
        _placements.Set("start-helper", new WidgetPlacement(0, 820, 880, PlacementAnchor.TopLeft, 280, 90, 1f, false, 5));
        PlacementPersistence.Load(_placements);

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        _hwnd = CreateWindowExW(
            WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW,
            ClassName, "", WS_POPUP | WS_VISIBLE,
            x, y, _width, _height, ownerHwnd, 0, 0, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        CreateDeviceChain();
    }

    private void CreateDeviceChain()
    {
        _device = DeviceResources.Create(_hwnd, _width, _height);
        _flags = new FlagBitmapCache(_device.Context);
        _standings = new StandingsWidget(_device.Context, _device.DWriteFactory, _flags, _device.FontCollection, startTelemetry: false);
        _relative = new RelativeWidget(_device.Context, _device.DWriteFactory, _flags, _device.FontCollection, startTelemetry: false);
        _weather = new WeatherWidget(_device.Context, _device.DWriteFactory, _device.FontCollection, startTelemetry: false);
        _fuel = new FuelWidget(_device.Context, _device.DWriteFactory, _device.FontCollection, startTelemetry: false);
        _radar = new RadarWidget(_device.Context, _device.DWriteFactory, _device.FontCollection, startTelemetry: false);
        _start = new StartHelperWidget(_device.Context, _device.DWriteFactory, _device.FontCollection, startTelemetry: false);
        var black = new Color4(0, 0, 0, 1);
        ComPtr<ID2D1SolidColorBrush> backdrop = default;
        if (_device.Context->CreateSolidColorBrush(&black, null, backdrop.GetAddressOf()).Success) _backdropBrush = backdrop;
        _standings.SetSimulatedRows(SimulationData.StandingsRows());
        _standings.SetSimulatedSession(SimulationData.Session(), SimulationData.Player());
        _relative.SetSimulatedRows(SimulationData.RelativeRows());
        _relative.SetSimulatedSession(SimulationData.Session(), SimulationData.Player());
        _weather.SetSimulatedStatus(SimulationData.Weather());
        _fuel.SetSimulatedStatus(SimulationData.Fuel(0));
        _fuel.SetSimulatedSession(SimulationData.Session());
        if (_profile is not null) ApplyProfile(_profile);
    }

    /// <summary>Pushes the Control Center's current settings (placements, columns, typography, formats,
    /// headers, rules, fuel) into the preview's widgets -- the same setters the live overlay uses, so the
    /// preview always shows what the overlay would draw with this configuration.</summary>
    public void ApplyProfile(WidgetPlacementStore store)
    {
        _profile = store;
        foreach (var key in PreviewKeys)
            if (store.Get(key) is { } placement) _placements.Set(key, placement);
        if (_standings is null || _relative is null || _weather is null || _fuel is null || _radar is null || _start is null) return;

        WidgetAppearance Appearance(string key) => store.AppearanceOverrides.TryGetValue(key, out var a) ? a : WidgetAppearance.Default;
        _standings.SetColumns(store.ColumnOverrides.TryGetValue("standings", out var sc) ? sc.ToList() : StandingsWidget.BuildDefaultColumns());
        _relative.SetColumns(store.ColumnOverrides.TryGetValue("relative", out var rc) ? rc.ToList() : RelativeWidget.BuildDefaultColumns());
        _standings.SetAppearance(Appearance("standings"));
        _relative.SetAppearance(Appearance("relative"));
        _weather.SetAppearance(Appearance("weather"));
        _fuel.SetAppearance(Appearance("fuel"));
        _radar.SetAppearance(Appearance("radar"));
        _start.SetAppearance(Appearance("start-helper"));

        var format = store.NumberFormat ?? NumberFormatConfig.Default;
        _standings.SetNumberFormat(format);
        _relative.SetNumberFormat(format);
        _standings.SetHeaderFields(store.HeaderOverrides.TryGetValue("standings", out var sh) ? sh : HeaderFields.DefaultStandings());
        _relative.SetHeaderFields(store.HeaderOverrides.TryGetValue("relative", out var rh) ? rh : HeaderFields.DefaultRelative());
        _standings.SetPresentationOptions(store.StandingsRules ?? StandingsPresentationOptions.Default);
        _relative.SetRelativeRules(store.RelativeRules ?? RelativeRules.Default);
        if (store.ClassRankColors is { Count: > 0 }) PaletteTokens.SetRankColors(store.ClassRankColors);
    }

    private void DisposeDeviceChain()
    {
        _backdropBrush.Dispose();
        _start?.Dispose(); _start = null;
        _radar?.Dispose(); _radar = null;
        _fuel?.Dispose(); _fuel = null;
        _weather?.Dispose(); _weather = null;
        _relative?.Dispose(); _relative = null;
        _standings?.Dispose(); _standings = null;
        _flags?.Dispose(); _flags = null;
        _device?.Dispose(); _device = null;
    }

    /// <summary>Repositions (and, if the size actually changed, rebuilds the device chain at the new
    /// size -- there is no incremental swap-chain resize path yet, matching the same gap already
    /// documented for the live overlay's own width/height/scale IPC messages) to track a WPF
    /// element's current screen rectangle.</summary>
    public void MoveTo(int x, int y, int width, int height)
    {
        if (_hwnd == 0 || width <= 0 || height <= 0) return;
        if ((x, y, width, height) == _lastRect) return;
        _lastRect = (x, y, width, height);
        bool sizeChanged = width != _width || height != _height;
        SetWindowPos(_hwnd, 0, x, y, width, height, SWP_NOACTIVATE | SWP_NOZORDER);
        if (sizeChanged)
        {
            _width = width;
            _height = height;
            DisposeDeviceChain();
            CreateDeviceChain();
        }
    }

    public void SetVisible(bool visible) => ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);

    /// <summary>Draws one frame. The caller (MainWindow, via <see cref="System.Windows.Media.CompositionTarget.Rendering"/>)
    /// decides the cadence -- this class has no timer of its own.</summary>
    public void RenderFrame()
    {
        if (_device is null || _standings is null || _relative is null || _weather is null || _fuel is null || _radar is null || _start is null) return;

        // 1920x1080-relative preview scaled into whatever size the panel actually granted this
        // surface (spec §12/§17: "Preview inclui modo 1920×1080 em escala real").
        float scale = _width / 1920f;

        _device.BeginFrame();
        DrawBackdrop(scale);
        // Time-driven fictitious data: a car passing alongside (radar), a standing start
        // (clutch / throttle / RPM) and fuel being burned, so those widgets can be judged in the preview.
        double t = _clock.Elapsed.TotalSeconds;
        _radar.SetSimulatedStatus(SimulationData.Radar(t));
        _start.SetSimulatedStatus(SimulationData.StartHelper(t));
        _fuel.SetSimulatedStatus(SimulationData.Fuel(t));
        var standingsPlacement = _placements.Get("standings");
        var relativePlacement = _placements.Get("relative");
        // Scale the whole drawing, not just the positions: widgets are laid out in real 1920x1080
        // DIPs, so scaling only their origins drew them at full size in a box a third as wide and
        // made neighbouring widgets overlap.
        var weatherPlacement = _placements.Get("weather");
        var fuelPlacement = _placements.Get("fuel");
        DrawWidget(standingsPlacement, scale, (x, y) => _standings.Draw(_device.Context, x, y));
        DrawWidget(relativePlacement, scale, (x, y) => _relative.Draw(_device.Context, x, y));
        DrawWidget(weatherPlacement, scale, (x, y) => _weather.Draw(_device.Context, x, y));
        DrawWidget(fuelPlacement, scale, (x, y) => _fuel.Draw(_device.Context, x, y));
        DrawWidget(_placements.Get("radar"), scale, (x, y) => _radar.Draw(_device.Context, x, y));
        DrawWidget(_placements.Get("start-helper"), scale, (x, y) => _start.Draw(_device.Context, x, y));
        var identity = System.Numerics.Matrix3x2.Identity;
        _device.Context->SetTransform(&identity);
        _device.EndFrame();
    }

    /// <summary>Draws one widget the way the overlay window would: at its position, with its own scale
    /// (about its top-left corner) and opacity, all inside the preview's 1920x1080 -> panel scaling.</summary>
    private void DrawWidget(WidgetPlacement? placement, float previewScale, Action<float, float> draw)
    {
        if (placement is not { Visible: true } || _device is null) return;
        var transform = System.Numerics.Matrix3x2.CreateScale(placement.Scale, new System.Numerics.Vector2(placement.X, placement.Y))
                        * System.Numerics.Matrix3x2.CreateScale(previewScale);
        _device.Context->SetTransform(&transform);
        bool faded = placement.Opacity < 0.995f;
        if (faded)
        {
            var layer = new LayerParameters1
            {
                contentBounds = new RectF(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity),
                geometricMask = null,
                maskAntialiasMode = AntialiasMode.PerPrimitive,
                maskTransform = System.Numerics.Matrix3x2.Identity,
                opacity = Math.Clamp(placement.Opacity, 0f, 1f),
                opacityBrush = null,
                layerOptions = LayerOptions1.None
            };
            _device.Context->PushLayer(&layer, null);
        }
        draw(placement.X, placement.Y);
        if (faded) _device.Context->PopLayer();
        var identity = System.Numerics.Matrix3x2.Identity;
        _device.Context->SetTransform(&identity);
    }

    /// <summary>A quiet navy gradient (sky to asphalt) so the widgets read as sitting over a game view
    /// rather than on a flat black box.</summary>
    private void DrawBackdrop(float scale)
    {
        if (_backdropBrush.Get() is null) return;
        const int bands = 54;
        float bandHeight = 1080f * scale / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (bands - 1f);
            var color = new Color4((0x10 + (0x03 - 0x10) * t) / 255f, (0x2A + (0x0C - 0x2A) * t) / 255f, (0x40 + (0x14 - 0x40) * t) / 255f, 1f);
            _backdropBrush.Get()->SetColor(&color);
            var rect = new RectF(0, i * bandHeight, _width, (i + 1) * bandHeight + 1f);
            _device!.Context->FillRectangle(&rect, (ID2D1Brush*)_backdropBrush.Get());
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASSW
        {
            lpfnWndProc = DefWindowProcPtr,
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName
        };
        ushort atom = RegisterClassW(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"RegisterClassW failed: {Marshal.GetLastWin32Error()}");
        _classRegistered = true;
    }

    private static readonly nint DefWindowProcPtr = GetProcAddress(LoadLibraryW("user32.dll"), "DefWindowProcW");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
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
    }

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASSW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibraryW(string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern nint GetProcAddress(nint module, string procName);

    public void Dispose()
    {
        DisposeDeviceChain();
        if (_hwnd != 0) { DestroyWindow(_hwnd); _hwnd = 0; }
    }
}
