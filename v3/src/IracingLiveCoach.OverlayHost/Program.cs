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
using IracingLiveCoach.OverlayHost.Theme;
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
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const nint HTCAPTION = 2;
    private const nint HTCLIENT = 1;
    private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    private const nint IDC_SIZEALL = 32646;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_T = 0x54;
    private const int VK_V = 0x56;
    private const int VK_E = 0x45;

    private static bool _clickThrough = true;
    private static bool _editMode;
    private static readonly List<nint> OverlayWindows = [];
    private static bool _simulating;
    /// <summary>Same handler the real chequered-flag event uses (V key simulates a win).</summary>
    private static Action<IracingLiveCoach.Core.Telemetry.RaceFinish>? _raceFinishHandler;

    /// <summary>"Os overlays só devem ser renderizados quando estiver na pista. Fora dela só se
    /// estiver editando" -- driven by <see cref="TelemetryReader.OnTrackStateChanged"/> (already a
    /// real, SDK-confirmed signal; see that event's own doc comment), OR while <see cref="_editMode"/>
    /// is on (so unlocking from the Control Center to reposition works from the garage/menus too).
    /// Starts false: a fresh launch shows nothing until the SDK actually reports a state, matching
    /// the same "never assume, only real telemetry" rule used everywhere else in this file.</summary>
    private static bool _isOnTrack;
    private static double _lastHeartbeatMs;
    private static SessionKind? _sessionKind;
    private static string _playerClassKey = "";
    private static string _playerCarKey = "";
    private static string _appliedProfileKey = "";
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

    /// <summary>Width/height edits arrive on the IPC thread but a swap chain may only be resized on
    /// the render thread between frames -- they queue here and the render loop drains them.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Width, int Height)> PendingSizes = new();

    // Drag state belongs to the HWND under the cursor. Each widget has its own native window,
    // so movement never carries unrelated overlay content or transparent padding with it.
    private static nint _draggingWindow;

    public static int Main()
    {
        // Numbers in the overlay ("38.5 L", "+3.816") must not follow the Windows locale (pt-BR would
        // print "38,5"); set before any worker thread starts so they inherit it.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Console.WriteLine("V3 Phase 3: SPACE=click-through, E=edit mode (drag widgets), T=simulation, ESC=exit.");
        LoadPrivateFonts();

        // Each widget owns its own top-level GPU surface. The dimensions are content-oriented
        // starting values; Phase 5 will drive the same values through the Control Center.
        PlacementStore.Set(StandingsKey, new WidgetPlacement(0, 28, 30, PlacementAnchor.TopLeft, 800, 264, 1f, false, 0));
        PlacementStore.Set(RelativeKey, new WidgetPlacement(0, 1440, 740, PlacementAnchor.TopLeft, 470, 262, 1f, false, 1));
        // Heights match each rewritten widget's real content (4/3/full-scale/3 rows) -- previously
        // undersized for Radar (42px for a widget needing ~130px) and Start Helper (50px for what
        // is now 3 rows including the RPM readout the earlier pass omitted).
        PlacementStore.Set(WeatherKey, new WidgetPlacement(0, 1590, 30, PlacementAnchor.TopLeft, 300, 118, 1f, false, 2));
        PlacementStore.Set(FuelKey, new WidgetPlacement(0, 1270, 30, PlacementAnchor.TopLeft, 310, 118, 1f, false, 3));
        PlacementStore.Set(RadarKey, new WidgetPlacement(0, 900, 640, PlacementAnchor.TopLeft, 120, 190, 1f, false, 4));
        PlacementStore.Set(StartHelperKey, new WidgetPlacement(0, 820, 880, PlacementAnchor.TopLeft, 280, 90, 1f, false, 5));

        // Spec §3/§12: a saved layout from a previous session overrides the defaults above --
        // loaded AFTER the defaults are set, so a first-ever launch (no file yet) still has sane
        // starting positions for every widget.
        PlacementPersistence.Load(PlacementStore);
        PlacementStore.ClearHistory(); // spec §4: undo/redo covers changes made this session, not the seed+load above
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
        trackStateTelemetry.OnTrackStateChanged += onTrack =>
        {
            _isOnTrack = onTrack;
            Console.WriteLine($"[State] {DateTime.Now:HH:mm:ss} on-track -> {onTrack}");
        };
        trackStateTelemetry.SessionStatusUpdated += s =>
        {
            var kind = SessionKinds.Classify(s.SessionTypeText);
            if (kind != _sessionKind || s.CarClassShortName != _playerClassKey)
                Console.WriteLine($"[State] {DateTime.Now:HH:mm:ss} session='{s.SessionTypeText}' kind={kind?.ToString() ?? "unknown"} class='{s.CarClassShortName}' car='{s.PlayerCarName}'");
            _sessionKind = kind;
            _playerClassKey = s.CarClassShortName ?? "";
            _playerCarKey = s.PlayerCarName ?? "";
        };
        // Victory theme: the player's own chequered flag in a Race, finishing where the configured
        // rule says "win" (class or overall), plays the user's audio file once.
        using var victoryPlayer = new IracingLiveCoach.OverlayHost.Audio.VictoryPlayer();
        _raceFinishHandler = finish =>
        {
            var victory = PlacementStore.Victory;
            bool win = victory is { Enabled: true } && victory.IsWin(finish);
            Console.WriteLine($"[Race] finished overall P{finish.OverallPosition} class P{finish.ClassPosition} win={win}");
            if (win) victoryPlayer.Play(victory!.FilePath, victory.VolumePct);
        };
        trackStateTelemetry.PlayerFinishedRace += _raceFinishHandler;
        trackStateTelemetry.Start();

        using var standingsResources = DeviceResources.Create(standingsHwnd, (int)standingsPlacement.WidthDip, (int)standingsPlacement.HeightDip);
        using var relativeResources = DeviceResources.Create(relativeHwnd, (int)relativePlacement.WidthDip, (int)relativePlacement.HeightDip);
        using var weatherResources = DeviceResources.Create(weatherHwnd, (int)weatherPlacement.WidthDip, (int)weatherPlacement.HeightDip);
        using var fuelResources = DeviceResources.Create(fuelHwnd, (int)fuelPlacement.WidthDip, (int)fuelPlacement.HeightDip);
        using var radarResources = DeviceResources.Create(radarHwnd, (int)radarPlacement.WidthDip, (int)radarPlacement.HeightDip);
        using var startResources = DeviceResources.Create(startHwnd, (int)startPlacement.WidthDip, (int)startPlacement.HeightDip);
        var resourcesByKey = new Dictionary<string, DeviceResources>
        {
            [StandingsKey] = standingsResources, [RelativeKey] = relativeResources, [WeatherKey] = weatherResources,
            [FuelKey] = fuelResources, [RadarKey] = radarResources, [StartHelperKey] = startResources,
        };
        standingsResources.SetClickThrough(standingsHwnd, EffectiveClickThrough(standingsHwnd));
        relativeResources.SetClickThrough(relativeHwnd, EffectiveClickThrough(relativeHwnd));
        weatherResources.SetClickThrough(weatherHwnd, EffectiveClickThrough(weatherHwnd));
        fuelResources.SetClickThrough(fuelHwnd, EffectiveClickThrough(fuelHwnd));
        radarResources.SetClickThrough(radarHwnd, EffectiveClickThrough(radarHwnd)); startResources.SetClickThrough(startHwnd, EffectiveClickThrough(startHwnd));

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
        if (PlacementStore.RelativeRules is { } savedRelativeRules) relative.SetRelativeRules(savedRelativeRules);
        if (PlacementStore.ClassRankColors is { Count: > 0 } savedRankColors) PaletteTokens.SetRankColors(savedRankColors);
        using var rulesIpcServer = new RulesIpcServer();
        rulesIpcServer.MessageReceived += m =>
        {
            var options = new StandingsPresentationOptions(m.TopNPerClass, m.OwnClassRows, m.OtherClassRows, m.KeepPlayerWindow, m.TopNCountsTowardTotal);
            var relativeRules = new RelativeRules(m.RelativeAhead, m.RelativeBehind).Clamped();
            standings.SetPresentationOptions(options);
            relative.SetRelativeRules(relativeRules);
            PlacementStore.StandingsRules = options;
            PlacementStore.RelativeRules = relativeRules;
            PlacementPersistence.Save(PlacementStore);
        };

        // Sixth small typed channel: per-widget font-scale/row-height/row-spacing (spec §12).
        void ApplyAppearance(string widgetKey, WidgetAppearance appearance)
        {
            switch (widgetKey)
            {
                case StandingsKey: standings.SetAppearance(appearance); break;
                case RelativeKey: relative.SetAppearance(appearance); break;
                case WeatherKey: weather.SetAppearance(appearance); break;
                case FuelKey: fuel.SetAppearance(appearance); break;
                case RadarKey: radar.SetAppearance(appearance); break;
                case StartHelperKey: start.SetAppearance(appearance); break;
                default: return; // unknown widget key -- ignore rather than guess
            }
        }
        foreach (var (widgetKey, appearance) in PlacementStore.AppearanceOverrides)
            ApplyAppearance(widgetKey, appearance);
        using var appearanceIpcServer = new AppearanceIpcServer();
        appearanceIpcServer.MessageReceived += m =>
        {
            var appearance = new WidgetAppearance(m.FontScale, m.RowHeightDip, m.RowSpacingDip, m.FontWeight, m.PaddingHDip, m.FontFamily ?? FontCatalog.DefaultKey, m.BackgroundColor ?? "", m.BackgroundOpacity);
            ApplyAppearance(m.WidgetKey, appearance);
            PlacementStore.AppearanceOverrides[m.WidgetKey] = appearance;
            PlacementPersistence.Save(PlacementStore);
        };

        // Configurable header fields (spec §12): reorder + show/hide per widget, live and persisted.
        void ApplyHeaderFields(string widgetKey, List<HeaderFieldConfig> fields)
        {
            switch (widgetKey)
            {
                case StandingsKey: standings.SetHeaderFields(fields); break;
                case RelativeKey: relative.SetHeaderFields(fields); break;
                default: return; // only the two table widgets have a session header
            }
        }
        foreach (var (widgetKey, fields) in PlacementStore.HeaderOverrides)
            ApplyHeaderFields(widgetKey, fields);
        using var headerConfigIpcServer = new HeaderConfigIpcServer();
        headerConfigIpcServer.MessageReceived += m =>
        {
            var fields = m.Fields.Select(f => new HeaderFieldConfig(f.Key, f.Visible, f.FontFamily, f.FontWeight)).ToList();
            ApplyHeaderFields(m.Widget, fields);
            PlacementStore.HeaderOverrides[m.Widget] = fields;
            PlacementPersistence.Save(PlacementStore);
        };

        // Seventh small typed channel: iRating/Safety Rating display format, global across
        // Standings/Relative (spec §12).
        if (PlacementStore.NumberFormat is { } savedNumberFormat)
        {
            standings.SetNumberFormat(savedNumberFormat);
            relative.SetNumberFormat(savedNumberFormat);
        }
        using var numberFormatIpcServer = new NumberFormatIpcServer();
        numberFormatIpcServer.MessageReceived += m =>
        {
            if (!Enum.TryParse<IRatingFormat>(m.IRatingFormat, out var iRatingFormat)) return;
            if (!Enum.TryParse<SafetyRatingFormat>(m.SafetyRatingFormat, out var srFormat)) return;
            if (!Enum.TryParse<NameDisplayFormat>(m.NameFormat, out var nameFormat)) return;
            var config = new NumberFormatConfig(iRatingFormat, srFormat, nameFormat, m.ShowIRatingDelta);
            standings.SetNumberFormat(config);
            relative.SetNumberFormat(config);
            PlacementStore.NumberFormat = config;
            PlacementPersistence.Save(PlacementStore);
        };

        // Class colours (spec §16), live: the four speed-rank colours and the per-name overrides.
        using var classColorsIpcServer = new ClassColorsIpcServer();
        classColorsIpcServer.MessageReceived += m =>
        {
            PaletteTokens.SetRankColors(m.RankColors);
            PaletteTokens.ClearAllNameOverrides();
            foreach (var (className, hex) in m.NameOverrides)
                if (TryParseHexColor(hex, out var color)) PaletteTokens.SetNameOverride(className, color);
            PlacementStore.ClassRankColors = m.RankColors.ToList();
            PlacementStore.ClassColorOverrides.Clear();
            foreach (var (className, hex) in m.NameOverrides) PlacementStore.ClassColorOverrides[className] = hex;
            PlacementPersistence.Save(PlacementStore);
        };

        // Loads a whole profile from disk into the RUNNING overlay (profile switch / import): every
        // section is re-applied live, and anything the profile does not define falls back to the
        // widget's default rather than keeping a stale value from the previous profile.
        void ApplyProfileFromDisk()
        {
            PaletteTokens.ClearAllNameOverrides();
            var loaded = new WidgetPlacementStore();
            PlacementPersistence.Load(loaded);
            PlacementStore.CopyFrom(loaded);

            foreach (var (widgetKey, placement) in PlacementStore.All)
                SyncWindowToPlacement(widgetKey, placement);

            standings.SetColumns(PlacementStore.ColumnOverrides.TryGetValue(StandingsKey, out var sc) ? sc : StandingsWidget.BuildDefaultColumns());
            relative.SetColumns(PlacementStore.ColumnOverrides.TryGetValue(RelativeKey, out var rc) ? rc : RelativeWidget.BuildDefaultColumns());
            foreach (var widgetKey in new[] { StandingsKey, RelativeKey, WeatherKey, FuelKey, RadarKey, StartHelperKey })
                ApplyAppearance(widgetKey, PlacementStore.AppearanceOverrides.TryGetValue(widgetKey, out var ap) ? ap : WidgetAppearance.Default);
            ApplyHeaderFields(StandingsKey, PlacementStore.HeaderOverrides.TryGetValue(StandingsKey, out var sh) ? sh : HeaderFields.DefaultStandings());
            ApplyHeaderFields(RelativeKey, PlacementStore.HeaderOverrides.TryGetValue(RelativeKey, out var rh) ? rh : HeaderFields.DefaultRelative());

            var format = PlacementStore.NumberFormat ?? NumberFormatConfig.Default;
            standings.SetNumberFormat(format);
            relative.SetNumberFormat(format);
            standings.SetPresentationOptions(PlacementStore.StandingsRules ?? StandingsPresentationOptions.Default);
            relative.SetRelativeRules(PlacementStore.RelativeRules ?? RelativeRules.Default);
            PaletteTokens.SetRankColors(PlacementStore.ClassRankColors ?? ["#FFD400", "#5CC8FF", "#FF6EB4", "#3DDC84"]);
            _appliedProfileKey = ""; // let the class/car auto-profile re-evaluate against the new profile
            Console.WriteLine("Profile reloaded from disk.");
        }

        // Ninth small typed channel: session-type visibility + per-class/car layout profiles.
        using var profilesIpcServer = new ProfilesIpcServer();
        profilesIpcServer.MessageReceived += m =>
        {
            switch (m.Action)
            {
                case "setSessionVisibility":
                    PlacementStore.SessionVisibility = new SessionVisibilityConfig(m.HiddenIn ?? new Dictionary<string, List<string>>());
                    break;
                case "saveClassProfile" when !string.IsNullOrWhiteSpace(m.Key):
                    PlacementStore.ClassProfiles[m.Key.Trim()] = PlacementStore.All.ToDictionary(kv => kv.Key, kv => kv.Value);
                    break;
                case "deleteClassProfile":
                    PlacementStore.ClassProfiles.Remove(m.Key);
                    break;
                case "reloadFromDisk":
                    ApplyProfileFromDisk();
                    return;
                case "setVictory":
                    // Key carries the VictoryConfig as JSON (one small string, no dedicated channel).
                    try { PlacementStore.Victory = System.Text.Json.JsonSerializer.Deserialize<VictoryConfig>(m.Key); }
                    catch (System.Text.Json.JsonException) { return; }
                    break;
                case "testVictory":
                    if (PlacementStore.Victory is { } testConfig) victoryPlayer.Play(testConfig.FilePath, testConfig.VolumePct);
                    return;
                case "stopVictory":
                    victoryPlayer.Stop();
                    return;
                default: return;
            }
            PlacementPersistence.Save(PlacementStore);
        };

        // Eighth small typed channel: spec §4's undo/redo, exposed from the Control Center.
        // Placement is the only history the store tracks (columns/rules/fuel/appearance overrides
        // are simple last-write-wins, same as every profile field) -- an undone/redone key's window
        // is re-synced immediately so what's on screen never lags what PlacementStore now holds.
        using var undoRedoIpcServer = new UndoRedoIpcServer();
        undoRedoIpcServer.MessageReceived += m =>
        {
            string? changedKey = m.Action == "redo" ? PlacementStore.Redo() : PlacementStore.Undo();
            if (changedKey is null) return;
            if (PlacementStore.Get(changedKey) is { } placement) SyncWindowToPlacement(changedKey, placement);
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
            ApplyMatchingProfile();

            // 30s heartbeat so a "why are my widgets hidden?" report can be answered from the log:
            // it shows whether the overlay's own reader is receiving telemetry at all.
            if (now - _lastHeartbeatMs > 30000)
            {
                _lastHeartbeatMs = now;
                Console.WriteLine($"[State] {DateTime.Now:HH:mm:ss} heartbeat telemetry={trackStateTelemetry.HasRecentTelemetry} onTrack={_isOnTrack} edit={_editMode} kind={_sessionKind?.ToString() ?? "unknown"}");
            }

            // Live size changes (Control Center / undo / profile switch): resize the swap chain and
            // window here on the render thread, then re-read the placements the draw calls below use.
            if (!PendingSizes.IsEmpty)
            {
                foreach (var key in PendingSizes.Keys.ToArray())
                {
                    if (!PendingSizes.TryRemove(key, out var size)) continue;
                    if (!resourcesByKey.TryGetValue(key, out var res) || !WidgetWindows.TryGetValue(key, out var hwnd)) continue;
                    if (res.Resize(size.Width, size.Height))
                        SetWindowPos(hwnd, 0, 0, 0, size.Width, size.Height, SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
                standingsPlacement = PlacementStore.Get(StandingsKey)!;
                relativePlacement = PlacementStore.Get(RelativeKey)!;
                weatherPlacement = PlacementStore.Get(WeatherKey)!;
                fuelPlacement = PlacementStore.Get(FuelKey)!;
                radarPlacement = PlacementStore.Get(RadarKey)!;
                startPlacement = PlacementStore.Get(StartHelperKey)!;
            }

            // Unlocked for editing: every widget shows the full simulated situation, so it can be
            // positioned at its real size (live data would leave Radar/Start Helper empty).
            if (_simulating || _editMode)
            {
                var simSession = SimulationData.Session();
                standings.SetSimulatedRows(SimulationData.StandingsRows());
                standings.SetSimulatedSession(simSession, SimulationData.Player());
                relative.SetSimulatedRows(SimulationData.RelativeRows());
                relative.SetSimulatedSession(simSession, SimulationData.Player());
                weather.SetSimulatedStatus(SimulationData.Weather());
                double simTime = sw.Elapsed.TotalSeconds;
                fuel.SetSimulatedStatus(SimulationData.Fuel(simTime));
                fuel.SetSimulatedSession(simSession);
                radar.SetSimulatedStatus(SimulationData.Radar(simTime));
                start.SetSimulatedStatus(SimulationData.StartHelper(simTime));
            }
            else
            {
                standings.SetSimulatedRows(null);
                standings.SetSimulatedSession(null, null);
                relative.SetSimulatedRows(null);
                relative.SetSimulatedSession(null, null);
                weather.SetSimulatedStatus(null);
                fuel.SetSimulatedStatus(null);
                fuel.SetSimulatedSession(null);
                radar.SetSimulatedStatus(null);
                start.SetSimulatedStatus(null);
            }

            AutoFit(StandingsKey, standings.LastDrawnSize);
            AutoFit(RelativeKey, relative.LastDrawnSize);
            AutoFit(RadarKey, radar.LastDrawnSize);
            AutoFit(FuelKey, fuel.LastDrawnSize);

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
            radarResources.BeginFrame(); radar.Draw(radarResources.Context, 0, 0, radarPlacement.WidthDip);
            if (_editMode) DrawEditModeOutlines(radarResources.Context, (0, 0, radarPlacement.WidthDip, radarPlacement.HeightDip));
            radarResources.EndFrame();
            startResources.BeginFrame(); start.Draw(startResources.Context, 0, 0, startPlacement.WidthDip);
            if (_editMode) DrawEditModeOutlines(startResources.Context, (0, 0, startPlacement.WidthDip, startPlacement.HeightDip));
            startResources.EndFrame();

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
                var rect = new Vortice.Win32.Numerics.RectF(r.Left + 1, r.Top + 1, r.Left + r.Width - 1, r.Top + r.Height - 1);
                var rounded = new RoundedRect { rect = rect, radiusX = 7, radiusY = 7 };
                dc->DrawRoundedRectangle(&rounded, (ID2D1Brush*)brush.Get(), 2f, null);
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
        var current = PlacementStore.Get(message.Widget);
        if (current is null) return;

        // "Bloquear posição" freezes position, size and scale only: visibility, opacity and
        // click-through still follow the Control Center while a widget is locked (the store's own
        // lock check would otherwise swallow the whole message, e.g. hiding a locked widget).
        if (current.Locked && message.Locked)
        {
            var soft = current with { Visible = message.Visible, Opacity = message.Opacity, ClickThrough = message.ClickThrough };
            PlacementStore.ReplacePlacements([new KeyValuePair<string, WidgetPlacement>(message.Widget, soft)]);
            PlacementPersistence.Save(PlacementStore);
            SyncWindowToPlacement(message.Widget, soft);
            return;
        }

        var updated = current with
        {
            X = message.X,
            Y = message.Y,
            WidthDip = message.WidthDip,
            HeightDip = message.HeightDip,
            Scale = message.Scale,
            Locked = message.Locked,
            Visible = message.Visible,
            Opacity = message.Opacity,
            ClickThrough = message.ClickThrough,
            AutoSize = message.AutoSize
        };
        PlacementStore.Set(message.Widget, updated);
        PlacementPersistence.Save(PlacementStore); // spec §3: every applied edit survives the next launch
        SyncWindowToPlacement(message.Widget, updated);
    }

    /// <summary>Pushes one widget's already-stored placement onto its live window -- position and
    /// opacity only (see <see cref="ApplyPlacementMessage"/>'s doc comment on why width/height/scale
    /// don't yet resize the swap chain). Shared by every path that changes <see cref="PlacementStore"/>
    /// out from under a window without going through a fresh <see cref="PlacementMessage"/>, namely
    /// <see cref="UndoRedoMessage"/>.</summary>
    private static void SyncWindowToPlacement(string widgetKey, WidgetPlacement placement)
    {
        if (!WidgetWindows.TryGetValue(widgetKey, out var hwnd)) return;
        SetWindowPos(hwnd, 0, (int)placement.X, (int)placement.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        PendingSizes[widgetKey] = ((int)placement.WidthDip, (int)placement.HeightDip);
        // Show/hide is now owned exclusively by UpdateOverlayVisibility (on-track/edit-mode gate),
        // applied on the very next frame -- a direct ShowWindow here would fight that cache and
        // could leave the two out of sync.
        byte alpha = (byte)Math.Clamp(placement.Opacity * 255f, 0f, 255f);
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        if (!_editMode) DeviceResources.ApplyClickThrough(hwnd, EffectiveClickThrough(hwnd));
    }

    /// <summary>Spec §12 "passagem de cliques" per widget: the global Space toggle AND the widget's
    /// own setting must both allow click-through (either one off makes the widget interactive).</summary>
    private static bool EffectiveClickThrough(nint hwnd) =>
        _clickThrough && (!WidgetKeysByHandle.TryGetValue(hwnd, out var key) || (PlacementStore.Get(key)?.ClickThrough ?? true));

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
            c.DecimalPlaces,
            c.FontFamily,
            c.FontWeight,
            c.LapWindow)).ToList();

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
    /// <summary>"Largura: Automática": an auto-sized widget's window follows what it actually drew, so
    /// changing columns, Top N or rows never clips content or leaves an empty window. Not a user edit,
    /// so no undo entry; the swap-chain resize itself happens through the existing queue.</summary>
    private static void AutoFit(string key, (float Width, float Height) drawn)
    {
        var placement = PlacementStore.Get(key);
        if (placement is not { AutoSize: true }) return;
        float w = MathF.Ceiling(drawn.Width), h = MathF.Ceiling(drawn.Height);
        if (w < 16f || h < 16f) return;
        if (MathF.Abs(placement.WidthDip - w) < 1f && MathF.Abs(placement.HeightDip - h) < 1f) return;
        PlacementStore.SetSizeQuiet(key, w, h);
        PendingSizes[key] = ((int)w, (int)h);

        // A widget that grew past the right edge slides left so its content is never cut off.
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        if (placement is { Locked: false, MonitorIndex: 0 } && screenWidth > 0 && placement.X + w > screenWidth)
        {
            float x = MathF.Max(0f, screenWidth - w - 12f);
            PlacementStore.SetXQuiet(key, x);
            if (WidgetWindows.TryGetValue(key, out var hwnd))
                SetWindowPos(hwnd, 0, (int)x, (int)placement.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }

    /// <summary>Spec §12 "perfis por carro/classe": when the player's class (or, failing that, car)
    /// has a saved profile, its placements replace the live layout -- once per change of key, not
    /// every frame, and not persisted (the stored profile itself is only ever rewritten by an
    /// explicit "save profile" from the Control Center).</summary>
    private static void ApplyMatchingProfile()
    {
        string key =
            !string.IsNullOrEmpty(_playerClassKey) && PlacementStore.ClassProfiles.ContainsKey(_playerClassKey) ? _playerClassKey :
            !string.IsNullOrEmpty(_playerCarKey) && PlacementStore.ClassProfiles.ContainsKey(_playerCarKey) ? _playerCarKey : "";
        if (key == _appliedProfileKey) return;
        _appliedProfileKey = key;
        if (key.Length == 0) return;
        PlacementStore.ReplacePlacements(PlacementStore.ClassProfiles[key]);
        foreach (var (widgetKey, placement) in PlacementStore.ClassProfiles[key])
            SyncWindowToPlacement(widgetKey, placement);
        Console.WriteLine($"Applied layout profile '{key}'.");
    }

    private static bool TryParseHexColor(string hex, out Vortice.Win32.Numerics.Color4 color)
    {
        color = default;
        var span = hex.AsSpan().Trim().TrimStart('#');
        if (span.Length != 6) return false;
        if (!byte.TryParse(span[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(span.Slice(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(span.Slice(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        color = new Vortice.Win32.Numerics.Color4(r / 255f, g / 255f, b / 255f, 1f);
        return true;
    }

    private static void UpdateOverlayVisibility()
    {
        bool gate = _isOnTrack || _editMode;
        foreach (var (key, hwnd) in WidgetWindows)
        {
            var placement = PlacementStore.Get(key);
            // Per-session-type hiding (spec §12) never applies while editing -- unlocking must
            // always reveal every widget so it can be positioned.
            bool sessionAllows = _editMode || (PlacementStore.SessionVisibility?.IsVisible(key, _sessionKind) ?? true);
            bool effective = (placement?.Visible ?? true) && gate && sessionAllows;
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
            DeviceResources.ApplyClickThrough(hwnd, enabled ? false : EffectiveClickThrough(hwnd));
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
                        foreach (var overlay in OverlayWindows) DeviceResources.ApplyClickThrough(overlay, EffectiveClickThrough(overlay));
                    Console.WriteLine($"Click-through: {_clickThrough}");
                }
                else if ((int)wParam == VK_T)
                {
                    _simulating = !_simulating;
                    Console.WriteLine($"Simulation preview: {_simulating}");
                }
                else if ((int)wParam == VK_V)
                {
                    _raceFinishHandler?.Invoke(new IracingLiveCoach.Core.Telemetry.RaceFinish(1, 1));
                }
                else if ((int)wParam == VK_E)
                {
                    SetEditMode(!_editMode);
                    Console.WriteLine($"Edit mode: {_editMode}");
                }
                return 0;
            case WM_LBUTTONDOWN:
                if (_editMode && WidgetKeysByHandle.TryGetValue(hwnd, out var pressedKey)
                    && PlacementStore.Get(pressedKey) is not { Locked: true })
                {
                    // Windows' native move loop: the window follows the cursor exactly, even when the
                    // mouse leaves it, and WM_EXITSIZEMOVE arrives when the button is released.
                    _draggingWindow = hwnd;
                    ReleaseCapture();
                    SendMessageW(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
                return 0;
            case WM_NCHITTEST:
                // In edit mode the whole widget is a drag handle (a locked one stays put).
                if (_editMode)
                    return WidgetKeysByHandle.TryGetValue(hwnd, out var hitKey) && PlacementStore.Get(hitKey) is { Locked: true } ? HTCLIENT : HTCAPTION;
                break;
            case WM_NCLBUTTONDBLCLK:
                return 0; // a double-click on the "caption" must never maximize the widget
            case WM_SETCURSOR:
                if (_editMode) { SetCursor(LoadCursorW(0, IDC_SIZEALL)); return 1; }
                break;
            case WM_EXITSIZEMOVE:
                // Drag finished: store the new position (PlacementStore + profile file) so it survives
                // a restart and the Control Center shows it.
                if (WidgetKeysByHandle.TryGetValue(hwnd, out var draggedKey))
                {
                    GetWindowRect(hwnd, out var finalRect);
                    var current = PlacementStore.Get(draggedKey);
                    if (current is not null && (current.X != finalRect.Left || current.Y != finalRect.Top))
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
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint hCursor);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint hWnd, int nIndex);
    [DllImport("user32.dll")] internal static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceExW(string fileName, uint flags, nint reserved);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
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
