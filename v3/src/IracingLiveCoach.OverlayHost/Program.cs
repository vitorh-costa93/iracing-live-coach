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
using IracingLiveCoach.OverlayHost.Ipc;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
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
    private const int SW_HIDE = 0;
    private const uint LWA_ALPHA = 0x2;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
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

    /// <summary>"Os overlays só devem ser renderizados quando estiver na pista. Fora dela só se
    /// estiver editando" -- driven by <see cref="TelemetryReader.OnTrackStateChanged"/> (already a
    /// real, SDK-confirmed signal; see that event's own doc comment), OR while <see cref="_editMode"/>
    /// is on (so unlocking from the Control Center to reposition works from the garage/menus too).
    /// Starts false: a fresh launch shows nothing until the SDK actually reports a state, matching
    /// the same "never assume, only real telemetry" rule used everywhere else in this file.</summary>
    private static bool _isOnTrack;
    private static readonly Dictionary<string, bool> _lastAppliedVisibility = new();

    private const string StandingsKey = "standings";
    private const string RelativeKey = "relative";
    private const string WeatherKey = "weather";
    private const string FuelKey = "fuel";
    private const string RadarKey = "radar";
    private const string StartHelperKey = "start-helper";
    private static readonly WidgetPlacementStore PlacementStore = new();
    private static readonly Dictionary<string, nint> WidgetWindows = new();
    private static readonly Dictionary<nint, string> WidgetKeysByHandle = new();

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

    /// <summary>Same spec §12 simulation rationale as the standings/relative rows above -- exercises
    /// the dynamic weather icon's three states isn't possible from one fixed snapshot, but at least
    /// proves the "damp track, no rain" branch (wetness 3, no precipitation) renders correctly.</summary>
    private static WeatherStatus BuildSimulatedWeatherStatus() =>
        new(AirTempC: 24.5, TrackTempC: 31.2, PrecipitationPct: 0, TrackWetness: 3, WeatherDeclaredWet: false,
            TrackRubberState: "MODERATE", CarPositions: [], WindSpeedMs: 3.2, WindDirectionDeg: 210);

    private static FuelStatus BuildSimulatedFuelStatus() =>
        new(FuelLevelLiters: 38.5, FuelUsePerHourLiters: 62.0, AverageFuelPerLapLiters: 2.24,
            LapsRemaining: 17.2, TimeRemainingSeconds: 21 * 60 + 14, FuelNeededForFinishLiters: -2.7);

    public static int Main()
    {
        Console.WriteLine("V3 Phase 3: SPACE=click-through, E=edit mode (drag widgets), T=simulation, ESC=exit.");
        LoadPrivateFonts();

        // Each widget owns its own top-level GPU surface. The dimensions are content-oriented
        // starting values; Phase 5 will drive the same values through the Control Center.
        PlacementStore.Set(StandingsKey, new WidgetPlacement(0, 200, 200, PlacementAnchor.TopLeft, 820, 260, 1f, false, 0));
        PlacementStore.Set(RelativeKey, new WidgetPlacement(0, 200, 470, PlacementAnchor.TopLeft, 760, 200, 1f, false, 1));
        // Heights match each rewritten widget's real content (4/3/full-scale/3 rows) -- previously
        // undersized for Radar (42px for a widget needing ~130px) and Start Helper (50px for what
        // is now 3 rows including the RPM readout the earlier pass omitted).
        PlacementStore.Set(WeatherKey, new WidgetPlacement(0, 980, 470, PlacementAnchor.TopLeft, 280, 132, 1f, false, 2));
        PlacementStore.Set(FuelKey, new WidgetPlacement(0, 980, 610, PlacementAnchor.TopLeft, 300, 104, 1f, false, 3));
        PlacementStore.Set(RadarKey, new WidgetPlacement(0, 980, 722, PlacementAnchor.TopLeft, 180, 130, 1f, false, 4));
        PlacementStore.Set(StartHelperKey, new WidgetPlacement(0, 980, 860, PlacementAnchor.TopLeft, 280, 82, 1f, false, 5));

        // Spec §3/§12: a saved layout from a previous session overrides the defaults above --
        // loaded AFTER the defaults are set, so a first-ever launch (no file yet) still has sane
        // starting positions for every widget.
        PlacementPersistence.Load(PlacementStore);
        ValidatePhysicalLimits();

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
        WidgetWindows[StandingsKey] = standingsHwnd;
        WidgetWindows[RelativeKey] = relativeHwnd;
        WidgetWindows[WeatherKey] = weatherHwnd;
        WidgetWindows[FuelKey] = fuelHwnd;
        WidgetWindows[RadarKey] = radarHwnd;
        WidgetWindows[StartHelperKey] = startHwnd;
        foreach (var (key, handle) in WidgetWindows) WidgetKeysByHandle[handle] = key;

        // Phase 5: the Control Center talks to this process only through this typed, versioned
        // pipe (spec §3) -- it never reaches into PlacementStore or any window handle directly.
        using var ipcServer = new PlacementIpcServer();
        ipcServer.MessageReceived += ApplyPlacementMessage;

        // Second small typed channel: a global lock/unlock so the Control Center can flip edit mode
        // remotely (the same toggle "E" already does on the overlay window itself).
        using var editModeIpcServer = new EditModeIpcServer();
        editModeIpcServer.MessageReceived += m => SetEditMode(m.Enabled);

        // Dedicated reader purely for the on-track signal -- each widget already owns its own
        // TelemetryReader for its own data; this one is never drawn from, only used to gate
        // visibility, so it doesn't couple visibility to any single widget's lifecycle.
        using var trackStateTelemetry = new TelemetryReader();
        trackStateTelemetry.OnTrackStateChanged += onTrack => _isOnTrack = onTrack;
        trackStateTelemetry.Start();

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
        using var standings = new StandingsWidget(standingsResources.Context, standingsResources.DWriteFactory, standingsFlags, standingsResources.FontCollection);
        using var relative = new RelativeWidget(relativeResources.Context, relativeResources.DWriteFactory, relativeFlags, relativeResources.FontCollection);
        using var weather = new WeatherWidget(weatherResources.Context, weatherResources.DWriteFactory, weatherResources.FontCollection);
        using var fuel = new FuelWidget(fuelResources.Context, fuelResources.DWriteFactory, fuelResources.FontCollection);
        using var radar = new RadarWidget(radarResources.Context, radarResources.DWriteFactory, radarResources.FontCollection);
        using var start = new StartHelperWidget(startResources.Context, startResources.DWriteFactory, startResources.FontCollection);
        standingsResources.DeviceRecovered += () => standingsFlags.Recreate(standingsResources.Context);
        relativeResources.DeviceRecovered += () => relativeFlags.Recreate(relativeResources.Context);

        // Third small typed channel: per-widget column configuration (spec §12's reorder/width/
        // decimals/alignment/visibility asks). Applies live and persists, same as placement edits.
        ApplyPersistedColumnConfig(standings, relative);
        using var columnConfigIpcServer = new ColumnConfigIpcServer();
        columnConfigIpcServer.MessageReceived += m => ApplyColumnConfigMessage(m, standings, relative);

        // Fourth small typed channel: Standings' Top N / rows-per-class selection rules.
        if (PlacementStore.StandingsRules is { } savedRules) standings.SetPresentationOptions(savedRules);
        using var rulesIpcServer = new RulesIpcServer();
        rulesIpcServer.MessageReceived += m =>
        {
            var options = new StandingsPresentationOptions(m.TopNPerClass, m.OwnClassRows, m.OtherClassRows, m.KeepPlayerWindow);
            standings.SetPresentationOptions(options);
            PlacementStore.StandingsRules = options;
            PlacementPersistence.Save(PlacementStore);
        };

        // Fifth small typed channel: Fuel's consumption-source/reserve/pit-exclusion config.
        if (PlacementStore.FuelConfig is { } savedFuelConfig) fuel.SetConfig(savedFuelConfig);
        using var fuelConfigIpcServer = new FuelConfigIpcServer();
        fuelConfigIpcServer.MessageReceived += m =>
        {
            if (!Enum.TryParse<FuelConsumptionSource>(m.Source, out var source)) return;
            var config = new FuelConfig(source, m.ManualLitersPerLap, m.ReserveLaps, m.ExcludePitLaps);
            fuel.SetConfig(config);
            PlacementStore.FuelConfig = config;
            PlacementPersistence.Save(PlacementStore);
        };

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

            UpdateOverlayVisibility();

            if (_simulating)
            {
                standings.SetSimulatedRows(BuildSimulatedStandingsRows());
                relative.SetSimulatedRows(BuildSimulatedRelativeRows());
                weather.SetSimulatedStatus(BuildSimulatedWeatherStatus());
                fuel.SetSimulatedStatus(BuildSimulatedFuelStatus());
            }
            else
            {
                standings.SetSimulatedRows(null);
                relative.SetSimulatedRows(null);
                weather.SetSimulatedStatus(null);
                fuel.SetSimulatedStatus(null);
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

    private static TimeSpan _lastCpuTime = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
    private static double _lastCpuSampleMs;

    /// <summary>Spec §13's "latência de frame/CPU/GPU/memória": frame pacing (p50/p95/p99/max) plus
    /// this process's own CPU% (TotalProcessorTime delta over the flush window, normalized by core
    /// count) and working-set memory. GPU utilization is honestly NOT captured here -- Windows has
    /// no cheap in-process API for it (the "GPU Engine" perf-counter category needs PDH and is
    /// unreliable across driver/OS versions); this is a real, documented gap, not silently
    /// skipped.</summary>
    private static void WriteFindings(string path, List<double> frameTimesMs)
    {
        var sorted = frameTimesMs.OrderBy(x => x).ToList();
        double P(double pct)
        {
            int idx = (int)Math.Clamp(pct * (sorted.Count - 1), 0, sorted.Count - 1);
            return sorted[idx];
        }

        var process = System.Diagnostics.Process.GetCurrentProcess();
        double nowMs = Environment.TickCount64;
        TimeSpan cpuNow = process.TotalProcessorTime;
        double wallMs = Math.Max(1, nowMs - _lastCpuSampleMs);
        double cpuPercent = 100.0 * (cpuNow - _lastCpuTime).TotalMilliseconds / wallMs / Environment.ProcessorCount;
        _lastCpuTime = cpuNow;
        _lastCpuSampleMs = nowMs;
        long workingSetMb = process.WorkingSet64 / (1024 * 1024);

        File.AppendAllText(path,
            $"{DateTime.UtcNow:O} samples={sorted.Count} p50={P(0.50):F2}ms p95={P(0.95):F2}ms p99={P(0.99):F2}ms max={sorted[^1]:F2}ms cpu={cpuPercent:F1}% mem={workingSetMb}MB gpu=not-captured{Environment.NewLine}");
    }

    /// <summary>Applies one Control Center update live -- spec §3: "aplicação de configurações sem
    /// reiniciar a corrida". Position/visibility/opacity/lock take effect immediately; width/height/
    /// scale are stored in <see cref="PlacementStore"/> but do not yet resize the live swap chain
    /// (that needs a DXGI ResizeBuffers path in DeviceResources -- honestly not built yet, tracked
    /// in the plan rather than silently ignored).</summary>
    private static void ApplyPlacementMessage(PlacementMessage message)
    {
        if (!WidgetWindows.TryGetValue(message.Widget, out var hwnd)) return;
        var current = PlacementStore.Get(message.Widget);
        if (current is null) return;

        var updated = current with
        {
            X = message.X,
            Y = message.Y,
            WidthDip = message.WidthDip,
            HeightDip = message.HeightDip,
            Scale = message.Scale,
            Locked = message.Locked,
            Visible = message.Visible,
            Opacity = message.Opacity
        };
        PlacementStore.Set(message.Widget, updated);
        PlacementPersistence.Save(PlacementStore); // spec §3: every applied edit survives the next launch

        SetWindowPos(hwnd, 0, (int)message.X, (int)message.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        // Show/hide is now owned exclusively by UpdateOverlayVisibility (on-track/edit-mode gate),
        // applied on the very next frame -- a direct ShowWindow here would fight that cache and
        // could leave the two out of sync.
        byte alpha = (byte)Math.Clamp(message.Opacity * 255f, 0f, 255f);
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
    }

    /// <summary>Applies a saved column-config profile (if any) to Standings/Relative at startup --
    /// only these two widgets have a column engine wired in; a persisted override for any other key
    /// is silently ignored rather than crashing.</summary>
    private static void ApplyPersistedColumnConfig(StandingsWidget standings, RelativeWidget relative)
    {
        if (PlacementStore.ColumnOverrides.TryGetValue(StandingsKey, out var standingsColumns))
            standings.SetColumns(standingsColumns);
        if (PlacementStore.ColumnOverrides.TryGetValue(RelativeKey, out var relativeColumns))
            relative.SetColumns(relativeColumns);
    }

    /// <summary>Spec §12: reorder/width/decimals/alignment/visibility, applied live and persisted --
    /// converts the wire DTO to the real <see cref="ColumnDefinition"/> the widgets consume.</summary>
    private static void ApplyColumnConfigMessage(ColumnConfigMessage message, StandingsWidget standings, RelativeWidget relative)
    {
        var columns = message.Columns.Select(c => new ColumnDefinition(
            c.Key,
            Enum.TryParse<ColumnWidthMode>(c.WidthMode, out var widthMode) ? widthMode : ColumnWidthMode.Fixed,
            c.WidthPx,
            c.MinWidthPx,
            Enum.TryParse<ColumnAlignment>(c.Alignment, out var alignment) ? alignment : ColumnAlignment.Left,
            c.PaddingLeftPx,
            c.PaddingRightPx,
            c.Visible,
            c.Order,
            c.DecimalPlaces)).ToList();

        switch (message.Widget)
        {
            case StandingsKey: standings.SetColumns(columns); break;
            case RelativeKey: relative.SetColumns(columns); break;
            default: return; // unknown widget key -- ignore rather than guess
        }

        PlacementStore.ColumnOverrides[message.Widget] = columns;
        PlacementPersistence.Save(PlacementStore);
    }

    /// <summary>Spec §17: no widget may exceed 25% of its target monitor's physical width, and a
    /// 7-driver table may not exceed 35% of its height. This is a warn-only check for now -- it logs
    /// every violation with the exact overage in pixels (spec's own "mostre quantos pixels faltam"
    /// language) but does not yet reject the configuration or fall back to a last-known-good layout
    /// in the Control Center, since that UI doesn't exist yet. Honest partial implementation of
    /// <see cref="WidgetLayoutEngine.ComputePhysicalLimits"/>, which previously had zero callers.</summary>
    private static void ValidatePhysicalLimits()
    {
        int screenWidthPx = GetSystemMetrics(SM_CXSCREEN);
        int screenHeightPx = GetSystemMetrics(SM_CYSCREEN);
        if (screenWidthPx <= 0 || screenHeightPx <= 0) return;

        var (maxWidthPx, maxHeightForSevenRowsPx) = WidgetLayoutEngine.ComputePhysicalLimits(screenWidthPx, screenHeightPx);
        foreach (var (key, placement) in PlacementStore.All)
        {
            float widthPx = placement.WidthDip * placement.Scale;
            if (widthPx > maxWidthPx)
            {
                Console.WriteLine($"[Spec §17] Widget '{key}' width {widthPx:0}px exceeds the {maxWidthPx:0}px ceiling (25% of {screenWidthPx}px) by {widthPx - maxWidthPx:0}px.");
            }
            // Only Standings/Relative render a variable-row table; the 35%-height ceiling is defined
            // for their real preset (7 drivers), not the other four fixed-content widgets.
            if ((key == StandingsKey || key == RelativeKey) && placement.HeightDip * placement.Scale > maxHeightForSevenRowsPx)
            {
                float heightPx = placement.HeightDip * placement.Scale;
                Console.WriteLine($"[Spec §17] Widget '{key}' height {heightPx:0}px exceeds the {maxHeightForSevenRowsPx:0}px ceiling (35% of {screenHeightPx}px) by {heightPx - maxHeightForSevenRowsPx:0}px.");
            }
        }
    }

    /// <summary>"Os overlays só devem ser renderizados quando estiver na pista. Fora dela só se eu
    /// estiver redimensionando no painel de controle" -- effective visibility is the widget's own
    /// configured <see cref="WidgetPlacement.Visible"/> AND (on track OR edit mode unlocked).
    /// ShowWindow is only called when the effective state actually changes, not every frame, since
    /// there's no need to re-issue the same Win32 call 60 times a second.</summary>
    private static void UpdateOverlayVisibility()
    {
        bool gate = _isOnTrack || _editMode;
        foreach (var (key, hwnd) in WidgetWindows)
        {
            var placement = PlacementStore.Get(key);
            bool effective = (placement?.Visible ?? true) && gate;
            if (_lastAppliedVisibility.TryGetValue(key, out var last) && last == effective) continue;
            _lastAppliedVisibility[key] = effective;
            ShowWindow(hwnd, effective ? SW_SHOW : SW_HIDE);
        }
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
                // In-game drags (as opposed to Control Center edits) only moved the HWND itself
                // until now -- PlacementStore and the on-disk profile never learned about them, so
                // a drag performed directly on the overlay silently reverted on next launch and
                // left the Control Center showing stale numbers. Persist the final position here,
                // on drag-end, the same way ApplyPlacementMessage does for IPC-driven changes.
                if (_editMode && _draggingWindow == hwnd && _draggingWindow != 0
                    && WidgetKeysByHandle.TryGetValue(hwnd, out var draggedKey))
                {
                    GetWindowRect(hwnd, out var finalRect);
                    var current = PlacementStore.Get(draggedKey);
                    if (current is not null)
                    {
                        PlacementStore.Set(draggedKey, current with { X = finalRect.Left, Y = finalRect.Top });
                        PlacementPersistence.Save(PlacementStore);
                    }
                }
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
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint hWnd, int nIndex);
    [DllImport("user32.dll")] internal static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceExW(string fileName, uint flags, nint reserved);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    /// <summary>Registers the bundled, OFL-licensed Barlow Semi Condensed files privately for
    /// this process only. No system font installation or global Windows state is changed. Public
    /// so the Control Center's embedded D2D preview (<c>OverlayPreviewHost</c>) can register the
    /// same fonts in its own process before creating any <c>IDWriteTextFormat</c>.</summary>
    public static void LoadPrivateFonts()
    {
        const uint FR_PRIVATE = 0x10;
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        foreach (string file in new[] { "BarlowSemiCondensed-Regular.ttf", "BarlowSemiCondensed-SemiBold.ttf" })
        {
            string path = Path.Combine(directory, file);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Fonts] Missing bundled font file: {path}");
                continue;
            }
            int added = AddFontResourceExW(path, FR_PRIVATE, 0);
            Console.WriteLine(added > 0
                ? $"[Fonts] Registered {file} ({added} face(s))."
                : $"[Fonts] AddFontResourceExW FAILED for {file} (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }
}
