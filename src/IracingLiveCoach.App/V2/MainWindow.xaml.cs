using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Globalization;
using IracingLiveCoach.App;
using MahApps.Metro.IconPacks;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace IracingLiveCoach.V2;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly OverlayProfile _profile;
    private readonly StudioWindow _studio;
    private WidgetProfile? _dragging;
    private Point _dragOrigin;
    private double _widgetLeft, _widgetTop;
    private bool _resizing;
    private double _widgetWidth, _widgetHeight;
    private bool _editing;
    private bool _hasCar;
    private readonly TelemetryReader _telemetry = new();
    private readonly WidgetProfile _radarWidget;
    private readonly WidgetProfile _startHelperWidget;
    // The source remains intact while the visible Relative rows are rebuilt from its settings.
    private readonly List<DriverRow> _relativePreviewSource = new();
    private string _fuelLevelText = "43.9 L", _fuelAverageText = "2.05 L/LAP", _fuelRefuelText = "+26.1 L", _fuelLapsText = "21.4 laps";
    private string _fuelLastText = "2.10", _fuelFiveText = "2.14", _fuelMaxText = "2.30";
    private string _fuelPitByLapText = "PIT BY LAP 32", _fuelPitAddText = "+26.1 L", _fuelPitStopsText = "1 STOP";
    private int? _currentSessionLap, _sessionTotalLaps;
    private string _relativePlayerText = "13  •  V. COSTA", _relativeAheadText = "P. SANTOS   −1.4s", _p2pText = "OVERTAKE · AVAILABLE";
    private string _brakeBiasText = "BRAKE BIAS --", _trackTempText = "TRACK --";
    private string _weatherClimateText = "Limpo", _weatherTemperatureText = "31°C", _weatherRainText = "0%", _weatherGripText = "Moderado";
    private string _sessionHeaderText = "WAITING FOR SESSION";
    private string _radarSideText = "", _radarDistanceText = "+18 m", _clutchText = "62%", _throttleText = "48%";
    private double _clutchPct = 62, _throttlePct = 48;
    public ObservableCollection<DriverRow> PreviewDrivers { get; } = new()
    {
        new() { Position="11", PositionBackground=ClassPositionBrush(0), ClassName="GTP", Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="L. Martins", License="A 4.52", LicenseBrush=LicenseBrush("A"), IRating="4.1k +12", IRatingValue="4.1k", IRatingDelta="12", IRatingGain=true, IRatingDeltaBrush=IRatingDeltaBrush(12), IRatingArrow=IRatingArrow(12), Gap="Leader", P2P="Pronto", P2PState="Ready", P2PBrush=P2PBrush(false,false), P2PIcon=P2PIcon(false,false), P2PLevel=100, Pit="PIT 10s", PitBrush=PitBrush("PIT 10s"), Manufacturer="FERRARI", BrandImage=BrandAsset("FERRARI") },
        new() { Position="12", PositionBackground=ClassPositionBrush(1), ClassName="LMP2", Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="P. Santos", License="A 2.94", LicenseBrush=LicenseBrush("A"), IRating="3.6k +7", IRatingValue="3.6k", IRatingDelta="7", IRatingGain=true, IRatingDeltaBrush=IRatingDeltaBrush(7), IRatingArrow=IRatingArrow(7), Gap="+1.420", P2P="Ativo 12s", P2PState="Active", P2PBrush=P2PBrush(true,false), P2PIcon=P2PIcon(true,false), P2PLevel=70, Pit="L23 12s", PitBrush=PitBrush("L23 12s"), Manufacturer="MCLAREN", BrandImage=BrandAsset("MCLAREN") },
        new() { Position="13", PositionBackground=ClassPositionBrush(2), ClassName="GT3", IsPlayerClass=true, Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="V. Costa", License="A 2.58", LicenseBrush=LicenseBrush("A"), IRating="3.5k +18", IRatingValue="3.5k", IRatingDelta="18", IRatingGain=true, IRatingDeltaBrush=IRatingDeltaBrush(18), IRatingArrow=IRatingArrow(18), Gap="+2.104", P2P="Pronto", P2PState="Ready", P2PBrush=P2PBrush(false,false), P2PIcon=P2PIcon(false,false), P2PLevel=100, Manufacturer="MCLAREN", BrandImage=BrandAsset("MCLAREN"), IsPlayer=true },
        new() { Position="14", PositionBackground=ClassPositionBrush(3), ClassName="GT4", Flag="🇺🇸", FlagImage=FlagAsset("US"), Driver="A. Souza", License="A 3.12", LicenseBrush=LicenseBrush("A"), IRatingValue="3.2k", IRatingDelta="4", IRatingDeltaBrush=IRatingDeltaBrush(-4), IRatingArrow=IRatingArrow(-4), Gap="+3.011", P2P="Rec. 54", P2PState="Charging", P2PBrush=P2PBrush(false,true), P2PIcon=P2PIcon(false,true), P2PLevel=25, Manufacturer="PORSCHE", BrandImage=BrandAsset("PORSCHE") },
        new() { Position="15", PositionBackground=ClassPositionBrush(2), ClassName="GT3", IsPlayerClass=true, Flag="🇯🇵", FlagImage=FlagAsset("JP"), Driver="B. Rocha", License="B 2.76", LicenseBrush=LicenseBrush("B"), IRatingValue="3.1k", IRatingDelta="5", IRatingDeltaBrush=IRatingDeltaBrush(5), IRatingArrow=IRatingArrow(5), Gap="+4.201", P2P="Pronto", P2PState="Ready", P2PIcon=P2PIcon(false,false), P2PLevel=100, Manufacturer="BMW", BrandImage=BrandAsset("BMW") }
    };
    public ObservableCollection<DriverRow> RelativeDrivers { get; } = new()
    {
        new() { Position="11", PositionBackground=ClassPositionBrush(0), Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="L. Martins", License="A 4.52", LicenseBrush=LicenseBrush("A"), IRatingValue="4.1k", IRatingDelta="12", IRatingDeltaBrush=IRatingDeltaBrush(12), IRatingArrow=IRatingArrow(12), Gap="−1.4", P2P="Pronto", P2PState="Ready", P2PBrush=P2PBrush(false,false), P2PIcon=P2PIcon(false,false), P2PLevel=100, Manufacturer="FERRARI", BrandImage=BrandAsset("FERRARI") },
        new() { Position="12", PositionBackground=ClassPositionBrush(1), Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="P. Santos", License="A 2.94", LicenseBrush=LicenseBrush("A"), IRatingValue="3.6k", IRatingDelta="7", IRatingDeltaBrush=IRatingDeltaBrush(7), IRatingArrow=IRatingArrow(7), Gap="−0.6", P2P="Ativo 12", P2PState="Active", P2PBrush=P2PBrush(true,false), P2PIcon=P2PIcon(true,false), P2PLevel=70, Manufacturer="MCLAREN", BrandImage=BrandAsset("MCLAREN") },
        new() { Position="13", PositionBackground=ClassPositionBrush(2), Flag="🇧🇷", FlagImage=FlagAsset("BR"), Driver="V. Costa", License="A 2.58", LicenseBrush=LicenseBrush("A"), IRatingValue="3.5k", IRatingDelta="18", IRatingDeltaBrush=IRatingDeltaBrush(18), IRatingArrow=IRatingArrow(18), Gap="0.0", P2P="Pronto", P2PState="Ready", P2PBrush=P2PBrush(false,false), P2PIcon=P2PIcon(false,false), P2PLevel=100, Manufacturer="MCLAREN", BrandImage=BrandAsset("MCLAREN"), IsPlayer=true },
        new() { Position="14", PositionBackground=ClassPositionBrush(3), Flag="🇺🇸", FlagImage=FlagAsset("US"), Driver="A. Souza", License="A 3.12", LicenseBrush=LicenseBrush("A"), IRatingValue="3.2k", IRatingDelta="4", IRatingDeltaBrush=IRatingDeltaBrush(-4), IRatingArrow=IRatingArrow(-4), Gap="+0.7", P2P="Rec. 54", P2PState="Charging", P2PBrush=P2PBrush(false,true), P2PIcon=P2PIcon(false,true), P2PLevel=25, Manufacturer="PORSCHE", BrandImage=BrandAsset("PORSCHE") },
        new() { Position="15", PositionBackground=ClassPositionBrush(0), Flag="🇯🇵", FlagImage=FlagAsset("JP"), Driver="B. Rocha", License="B 2.76", LicenseBrush=LicenseBrush("B"), IRatingValue="3.1k", IRatingDelta="5", IRatingDeltaBrush=IRatingDeltaBrush(5), IRatingArrow=IRatingArrow(5), Gap="+1.5", P2P="Pronto", P2PState="Ready", P2PBrush=P2PBrush(false,false), P2PIcon=P2PIcon(false,false), P2PLevel=100, Manufacturer="BMW", BrandImage=BrandAsset("BMW") }
    };
    public ObservableCollection<WidgetProfile> Widgets => _profile.Widgets;
    public ObservableCollection<RadarDot> RadarDots { get; } = new();
    public bool IsEditing
    {
        get => _editing;
        private set
        {
            if (_editing == value) return;
            _editing = value;
            PropertyChanged?.Invoke(this, new(nameof(IsEditing)));
        }
    }
    public string FuelLevelText { get => _fuelLevelText; private set => Set(ref _fuelLevelText, value); }
    public string FuelAverageText { get => _fuelAverageText; private set => Set(ref _fuelAverageText, value); }
    public string FuelRefuelText { get => _fuelRefuelText; private set => Set(ref _fuelRefuelText, value); }
    public string FuelLapsText { get => _fuelLapsText; private set => Set(ref _fuelLapsText, value); }
    public string FuelLastText { get => _fuelLastText; private set => Set(ref _fuelLastText, value); }
    public string FuelFiveText { get => _fuelFiveText; private set => Set(ref _fuelFiveText, value); }
    public string FuelMaxText { get => _fuelMaxText; private set => Set(ref _fuelMaxText, value); }
    public string FuelPitByLapText { get => _fuelPitByLapText; private set => Set(ref _fuelPitByLapText, value); }
    public string FuelPitAddText { get => _fuelPitAddText; private set => Set(ref _fuelPitAddText, value); }
    public string FuelPitStopsText { get => _fuelPitStopsText; private set => Set(ref _fuelPitStopsText, value); }
    public string RelativePlayerText { get => _relativePlayerText; private set => Set(ref _relativePlayerText, value); }
    public string RelativeAheadText { get => _relativeAheadText; private set => Set(ref _relativeAheadText, value); }
    public string P2PText { get => _p2pText; private set => Set(ref _p2pText, value); }
    public string BrakeBiasText { get => _brakeBiasText; private set => Set(ref _brakeBiasText, value); }
    public string TrackTempText { get => _trackTempText; private set => Set(ref _trackTempText, value); }
    public string WeatherTemperatureText { get => _weatherTemperatureText; private set => Set(ref _weatherTemperatureText, value); }
    public string WeatherClimateText { get => _weatherClimateText; private set => Set(ref _weatherClimateText, value); }
    public string WeatherRainText { get => _weatherRainText; private set => Set(ref _weatherRainText, value); }
    public string WeatherGripText { get => _weatherGripText; private set => Set(ref _weatherGripText, value); }
    public string SessionHeaderText { get => _sessionHeaderText; private set => Set(ref _sessionHeaderText, value); }
    public string RadarSideText { get => _radarSideText; private set => Set(ref _radarSideText, value); }
    public string RadarDistanceText { get => _radarDistanceText; private set => Set(ref _radarDistanceText, value); }
    public double ClutchPct { get => _clutchPct; private set => Set(ref _clutchPct, value); }
    public double ThrottlePct { get => _throttlePct; private set => Set(ref _throttlePct, value); }
    public string ClutchText { get => _clutchText; private set => Set(ref _clutchText, value); }
    public string ThrottleText { get => _throttleText; private set => Set(ref _throttleText, value); }

    public MainWindow()
    {
        InitializeComponent();
        _profile = ProfileStore.Load();
        _radarWidget = _profile.Widgets.First(w => w.Kind == WidgetKind.Radar);
        _startHelperWidget = _profile.Widgets.First(w => w.Kind == WidgetKind.StartHelper);
        // 760px was an erroneous forced upgrade from the previous build. Restore the compact
        // working size; optional columns now scale instead of making the card wider.
        var standingsProfile = _profile.Widgets.FirstOrDefault(widget => widget.IsStandings);
        if (standingsProfile is not null && Math.Abs(standingsProfile.Width - 760d) < .5) standingsProfile.Width = 510;
        var fuelProfile = _profile.Widgets.FirstOrDefault(widget => widget.Kind == WidgetKind.Fuel);
        if (fuelProfile is not null && fuelProfile.Height < 170) fuelProfile.Height = 175;
        foreach (var widget in _profile.Widgets.Where(widget => widget.IsTimingWidget))
        {
            widget.DriverNameStyle = widget.DriverNameStyle switch { "Abreviado" => "Abbreviated", "Completo" => "Full", "Código" => "Code", _ => widget.DriverNameStyle };
            // Timing widgets are column-driven.  Older profiles could persist this as false,
            // which made the width slider crop the last column instead of resizing the card.
            widget.AutoFitToColumns = true;
            widget.ShowP2PColumn = true;
            if (widget.TimingColumns.Count == 0) widget.TimingColumns = TimingColumn.CreateDefaults();
            if (widget.TimingColumns.All(column => column.Key != "Pit")) widget.TimingColumns.Add(new TimingColumn { Key="Pit", Label="PIT", Width=62 });
            if (!widget.ColumnLayoutInitialized && widget.TimingColumns.All(column => column.IsVisible))
            {
                foreach (var column in widget.TimingColumns.Where(column => column.Key is "LastLap" or "BestLap" or "LapDelta" or "Tire")) column.IsVisible = false;
                widget.ColumnLayoutInitialized = true;
            }
            if (widget.HeaderFields.Count == 0) widget.HeaderFields = HeaderField.CreateDefaults();
            foreach (var field in widget.HeaderFields)
            {
                field.Label = HeaderLabel(field.Key);
                field.Icon = HeaderIcon(field.Key);
                // Only a genuine layout change (reordering/visibility) needs the whole driver list
                // rebuilt -- Value ticks on every telemetry update (SOF/lap/clock) and is already
                // picked up by its own direct binding, so rebuilding the full row collection for it
                // was needless per-tick churn (the actual cause of the standings flicker/lag).
                field.PropertyChanged += (_, args) => { if (args.PropertyName != nameof(HeaderField.Value)) RebuildPreviewMulticlass(); };
            }
            widget.HeaderFields.CollectionChanged += (_, _) => RebuildPreviewMulticlass();
            widget.TimingColumns.CollectionChanged += (_, _) => RebuildTimingLayout(widget);
            foreach (var column in widget.TimingColumns) column.PropertyChanged += (_, _) => RebuildTimingLayout(widget);
            widget.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(WidgetProfile.DriverNameStyle) || args.PropertyName is nameof(WidgetProfile.PlayerClassRows) or nameof(WidgetProfile.OtherClassRows) or nameof(WidgetProfile.ShowMulticlass)) { RebuildPreviewMulticlass(); return; }
                if (args.PropertyName is nameof(WidgetProfile.AutoFitToColumns) or nameof(WidgetProfile.FontScale) || args.PropertyName?.EndsWith("ColumnWidth", StringComparison.Ordinal) == true || args.PropertyName == nameof(WidgetProfile.ShowHeader)) RebuildTimingLayout(widget);
            };
        }
        foreach (var driver in PreviewDrivers) { driver.IsPreview = true; driver.RawDriverName = PreviewFullName(driver.Driver); driver.CarNumber = PreviewCarNumber(driver.RawDriverName); PopulateFields(driver, Widgets.First(w => w.IsStandings)); }
        foreach (var driver in RelativeDrivers) { driver.IsPreview = true; driver.RawDriverName = PreviewFullName(driver.Driver); driver.CarNumber = PreviewCarNumber(driver.RawDriverName); PopulateFields(driver, Widgets.First(w => w.IsRelative)); }
        _relativePreviewSource.AddRange(ExpandPreviewRows(PreviewDrivers));
        RefreshDriverNames(Widgets.First(w => w.IsStandings)); RefreshDriverNames(Widgets.First(w => w.IsRelative));
        RebuildPreviewMulticlass();
        // V2 opens in layout mode initially. This makes first-use positioning explicit;
        // the Studio's lock button restores click-through behaviour for driving.
        _profile.Locked = false;
        DataContext = this;
        _studio = new StudioWindow(_profile, PreviewDrivers, Save, SetEditing);
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            // An owned topmost Studio is guaranteed to remain above the full-screen transparent
            // overlay. Without an owner, Windows may keep the overlay in the foreground and make
            // a successfully-created control panel look as if the application never opened.
            _studio.Owner = this;
            _studio.Show();
            _studio.Activate();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Closed += (_, _) => { _telemetry.Dispose(); _studio.Close(); };
        SourceInitialized += (_, _) => SetEditing(!_profile.Locked);
        ConfigureTelemetry();
        _telemetry.Start();
    }

    private void Save() => ProfileStore.Save(_profile);


    // Coalesces bursts of telemetry-thread callbacks into a single UI-thread update per frame:
    // Post() from the telemetry thread never blocks (BeginInvoke, not Invoke), and if several
    // ticks arrive before the UI thread catches up, only the LATEST snapshot is applied -- the
    // telemetry thread is never held up waiting on WPF layout, and the UI never works through a
    // backlog of stale intermediate frames (root cause of "overtakes only show up a lap later").
    private sealed class UpdateCoalescer<T> where T : class
    {
        private readonly System.Windows.Threading.Dispatcher _dispatcher;
        private readonly Action<T> _apply;
        private T? _latest;
        private bool _queued;
        public UpdateCoalescer(System.Windows.Threading.Dispatcher dispatcher, Action<T> apply) { _dispatcher = dispatcher; _apply = apply; }
        public void Post(T value)
        {
            _latest = value;
            if (_queued) return;
            _queued = true;
            _dispatcher.BeginInvoke(() =>
            {
                _queued = false;
                var v = _latest;
                if (v is not null) _apply(v);
            });
        }
    }

    // Replaces an ObservableCollection's contents in place (Replace per changed index) instead of
    // Clear()+Add (Reset) -- Reset forces the bound ItemsControl to drop and regenerate every
    // container, which is what produced the visible flicker between ticks. A plain index Replace
    // only touches the row that actually changed.
    private static void ApplyRows<T>(ObservableCollection<T> target, IReadOnlyList<T> updated)
    {
        var shared = Math.Min(target.Count, updated.Count);
        for (var i = 0; i < shared; i++) target[i] = updated[i];
        for (var i = target.Count - 1; i >= updated.Count; i--) target.RemoveAt(i);
        for (var i = target.Count; i < updated.Count; i++) target.Add(updated[i]);
    }

    private void ConfigureTelemetry()
    {
        var standings = new UpdateCoalescer<List<StandingsRow>>(Dispatcher, ApplyStandings);
        var relative = new UpdateCoalescer<List<RelativeRow>>(Dispatcher, ApplyFullRelative);
        var sessionStatus = new UpdateCoalescer<SessionStatus>(Dispatcher, ApplySessionStatus);
        var fuel = new UpdateCoalescer<FuelStatus>(Dispatcher, ApplyFuel);
        var weather = new UpdateCoalescer<WeatherStatus>(Dispatcher, ApplyWeather);
        var playerStatus = new UpdateCoalescer<PlayerCarStatus>(Dispatcher, ApplyPlayerCarStatus);
        var radar = new UpdateCoalescer<RadarStatus>(Dispatcher, ApplyRadar);
        var raceStart = new UpdateCoalescer<RaceStartStatus>(Dispatcher, ApplyRaceStart);

        _telemetry.OnTrackStateChanged += hasCar => Dispatcher.BeginInvoke(() =>
        {
            _hasCar = hasCar;
            if (!IsEditing) foreach (var widget in Widgets) widget.SessionVisible = hasCar;
        });
        _telemetry.StandingsUpdated += standings.Post;
        _telemetry.FullRelativeUpdated += relative.Post;
        _telemetry.SessionStatusUpdated += sessionStatus.Post;
        _telemetry.FuelUpdated += fuel.Post;
        _telemetry.WeatherUpdated += weather.Post;
        _telemetry.PlayerCarStatusUpdated += playerStatus.Post;
        _telemetry.RadarUpdated += radar.Post;
        _telemetry.RaceStartUpdated += raceStart.Post;
    }

    private void ApplyStandings(List<StandingsRow> rows)
    {
        var widget = Widgets.First(w => w.Kind == WidgetKind.Standings);
        var renderedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var built = new List<DriverRow>();
        foreach (var row in SelectStandingsRows(rows, widget))
        {
            if (renderedClasses.Add(row.ClassShortName))
            {
                built.Add(new DriverRow
                {
                    IsClassHeader = true, ClassName = row.ClassShortName,
                    ClassHeaderText = row.ClassShortName, PositionBackground = ClassPositionBrush(ClassRank(row.ClassShortName)),
                    ClassHeaderPositionWidth = widget.PositionColumnWidth, ClassHeaderFields = widget.HeaderFields
                });
            }
            var driver = new DriverRow
            {
                Position = (row.ClassPosition > 0 ? row.ClassPosition : row.Position).ToString(CultureInfo.InvariantCulture), PositionBackground = ClassPositionBrush(ClassRank(row.ClassShortName)), Flag = row.FlagEmoji, RawDriverName=row.DriverCode, Driver = FormatWidgetDriver(row.DriverCode, widget.DriverNameStyle),
                License = row.LicString, LicenseBrush = LicenseBrush(row.LicString),
                IRating = row.IRating > 0 ? $"{row.IRating / 1000.0:0.0}k {row.EstimatedDeltaIRating:+0;-0;0}" : "--",
                IRatingValue = row.IRating > 0 ? $"{row.IRating / 1000.0:0.0}k" : "--", IRatingDelta = row.EstimatedDeltaIRating is double delta ? $"{Math.Abs(delta):0}" : "", IRatingGain = (row.EstimatedDeltaIRating ?? 0) >= 0, IRatingDeltaBrush = IRatingDeltaBrush(row.EstimatedDeltaIRating), IRatingArrow = IRatingArrow(row.EstimatedDeltaIRating),
                Gap = row.Position == 1 ? "LEADER" : row.GapToLeaderSeconds is double gap ? $"+{gap:0.000}" : "--",
                Manufacturer = row.ManufacturerBadge,
                FlagImage = FlagAsset(row.FlagEmoji), BrandImage = BrandAsset(row.ManufacturerBadge),
                P2P = FormatP2P(row.P2PActive, row.P2PUsesRemaining, row.P2PSecondsRemaining, row.P2PInCooldown),
                P2PState = P2PState(row.P2PActive, row.P2PInCooldown), P2PBrush = P2PBrush(row.P2PActive, row.P2PInCooldown), P2PIcon = P2PIcon(row.P2PActive, row.P2PInCooldown), P2PLevel = P2PLevel(row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown), IsPlayer = row.IsPlayer,
                Pit = row.PitStatus, PitBrush = PitBrush(row.PitStatus),
                GapToLeader = row.Position == 1 ? "LEADER" : row.GapToLeaderSeconds is double leaderGap ? $"+{leaderGap:0.000}" : "--",
                Interval = row.IntervalSeconds is double interval ? $"+{interval:0.000}" : "--",
                LastLap = FormatLap(row.LastLapTime), LapDelta = FormatSigned(row.LapDeltaVsPlayerSeconds),
                Tire = TireText(row.TireCompound), ClassName = row.ClassShortName, IsPlayerClass = row.IsPlayer || rows.FirstOrDefault(candidate => candidate.IsPlayer)?.ClassShortName == row.ClassShortName
            };
            PopulateFields(driver, widget);
            built.Add(driver);
        }
        ApplyRows(PreviewDrivers, built);
    }

    private void ApplyFullRelative(List<RelativeRow> rows)
    {
        var mine = rows.FirstOrDefault(row => row.IsPlayer);
        var ahead = rows.Where(row => row.PositionOffset < 0).OrderByDescending(row => row.PositionOffset).FirstOrDefault();
        if (mine is not null) RelativePlayerText = $"{mine.PositionOffset}  •  {mine.DriverCode}";
        if (ahead is not null) RelativeAheadText = $"{ahead.DriverCode}   {ahead.GapSeconds:+0.0;-0.0;0.0}s";
        P2PText = FormatP2PSummary(mine);
        var widget = Widgets.First(w => w.Kind == WidgetKind.Relative);
        var built = new List<DriverRow>();
        foreach (var row in SelectRelativeRows(rows, widget))
        {
            var driver = new DriverRow
            {
                Position = (row.ClassPosition > 0 ? row.ClassPosition : row.PositionOffset).ToString(CultureInfo.InvariantCulture), PositionBackground = ClassPositionBrush(ClassRank(row.ClassShortName)),
                Flag = row.FlagEmoji, FlagImage = FlagAsset(row.FlagEmoji),
                RawDriverName=row.DriverCode, Driver = FormatWidgetDriver(row.DriverCode, widget.DriverNameStyle),
                License = row.LicString, LicenseBrush = LicenseBrush(row.LicString),
                IRating = row.IRating > 0 ? $"{row.IRating / 1000.0:0.0}k" : "--", IRatingValue = row.IRating > 0 ? $"{row.IRating / 1000.0:0.0}k" : "--", IRatingDelta = "", IRatingGain = true,
                Gap = row.GapSeconds is double gap ? $"{gap:+0.0;-0.0;0.0}" : "--",
                P2P = FormatP2P(row.P2PActive, row.P2PUsesRemaining, row.P2PSecondsRemaining, row.P2PInCooldown),
                P2PState = P2PState(row.P2PActive, row.P2PInCooldown), P2PBrush = P2PBrush(row.P2PActive, row.P2PInCooldown), P2PIcon = P2PIcon(row.P2PActive, row.P2PInCooldown), P2PLevel = P2PLevel(row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown),
                Manufacturer = row.ManufacturerBadge,
                BrandImage = BrandAsset(row.ManufacturerBadge),
                IsPlayer = row.IsPlayer,
                GapToLeader = row.GapSeconds is double relativeGap ? $"{relativeGap:+0.000;-0.000;0.000}" : "--",
                Interval = row.GapSeconds is double intervalGap ? $"{intervalGap:+0.000;-0.000;0.000}" : "--",
                Tire = TireText(row.TireCompound)
            };
            PopulateFields(driver, widget);
            built.Add(driver);
        }
        ApplyRows(RelativeDrivers, built);
    }

    private void ApplySessionStatus(SessionStatus status)
    {
        _currentSessionLap = status.CurrentLap;
        _sessionTotalLaps = status.TotalLaps;
        var lap = status.CurrentLap is int current ? status.TotalLaps is int total ? $"LAP {current}/{total}" : $"LAP {current}" : "";
        var sof = status.StrengthOfField is double value ? $"SOF {value:0}" : "";
        SessionHeaderText = string.Join("  ·  ", new[] { status.CarClassShortName, status.SessionTypeText, lap, sof, status.DriverCount > 0 ? $"{status.DriverCount} DRIVERS" : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        SetHeaderValue("Class", status.CarClassShortName); SetHeaderValue("Session", status.SessionTypeText); SetHeaderValue("Lap", lap);
        SetHeaderValue("Sof", sof); SetHeaderValue("Drivers", status.DriverCount > 0 ? $"{status.DriverCount} DRIVERS" : "--"); SetHeaderValue("Clock", DateTime.Now.ToString("HH:mm"));
    }

    private void ApplyFuel(FuelStatus fuel)
    {
        FuelLevelText = $"{fuel.FuelLevelLiters:0.0} L";
        FuelAverageText = fuel.AverageFuelPerLapLiters is double avg ? $"{avg:0.00} L/LAP" : "CALIBRATING";
        FuelRefuelText = fuel.RefuelToFullLiters is double refill ? $"+{refill:0.0} L" : "--";
        FuelLapsText = fuel.LapsRemaining is double laps ? $"{laps:0.0} laps" : "no estimate";
        FuelPitByLapText = fuel.LapsRemaining is double remaining && _currentSessionLap is int currentLap
            ? $"PIT BY LAP {Math.Max(currentLap, currentLap + (int)Math.Floor(remaining))}"
            : "PIT WINDOW --";
        FuelPitAddText = fuel.RefuelToFullLiters is double pitFuel ? $"+{pitFuel:0.0} L" : "--";
        FuelPitStopsText = fuel.LapsRemaining is double tankLaps && _currentSessionLap is int lap && _sessionTotalLaps is int total && tankLaps > 0
            ? $"{Math.Max(0, (int)Math.Ceiling(Math.Max(0, total - lap) / tankLaps) - 1)} STOPS"
            : "-- STOPS";
        if (fuel.AverageFuelPerLapLiters is double measured)
        {
            FuelLastText = measured.ToString("0.00", CultureInfo.InvariantCulture);
            FuelFiveText = measured.ToString("0.00", CultureInfo.InvariantCulture);
            FuelMaxText = measured.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    private void ApplyWeather(WeatherStatus weather)
    {
        WeatherClimateText = weather.WeatherDeclaredWet ? "Chuvoso" : "Limpo";
        WeatherTemperatureText = $"{weather.TrackTempC:0}°C";
        WeatherRainText = $"{weather.PrecipitationPct:0}%";
        WeatherGripText = weather.TrackRubberState ?? TrackCondition(weather.TrackWetness, weather.WeatherDeclaredWet);
    }

    private void ApplyPlayerCarStatus(PlayerCarStatus status)
    {
        BrakeBiasText = status.BrakeBiasPct is double bias ? $"BRAKE BIAS {bias:0.0}%" : "BRAKE BIAS --";
        TrackTempText = status.TrackTempC is double temp ? $"TRACK {temp:0}°C" : "TRACK --";
        SetHeaderValue("BrakeBias", BrakeBiasText); SetHeaderValue("TrackTemp", TrackTempText);
    }

    private void ApplyRadar(RadarStatus radar)
    {
        RadarSideText = radar.BlindSpotLeft && radar.BlindSpotRight ? "DOS DOIS LADOS" : radar.BlindSpotLeft ? "ESQUERDA" : radar.BlindSpotRight ? "DIREITA" : "LIVRE";
        var nearest = radar.Blips.OrderBy(blip => Math.Abs(blip.DistanceMeters)).FirstOrDefault();
        RadarDistanceText = nearest is null ? "" : $"{nearest.DistanceMeters:+0;-0;0} m";
        var range = _radarWidget.RadarRange;
        var nearby = radar.Blips.Where(b => Math.Abs(b.DistanceMeters) <= range).OrderBy(b => Math.Abs(b.DistanceMeters)).Take(8).ToList();
        // The radar only earns its screen space while there's actually a car close enough to
        // matter (or something in the immediate blind spot) -- otherwise it disappears entirely.
        _radarWidget.DynamicGateOpen = nearby.Count > 0 || radar.BlindSpotLeft || radar.BlindSpotRight;
        var built = new List<RadarDot>();
        foreach (var blip in nearby)
        {
            // Forward is up. CarIdxLapDistPct gives the signed distance; iRacing only gives
            // an actual side for immediate overlap, so far cars remain in the centre lane.
            var top = Math.Clamp(74 - (blip.DistanceMeters / range * 60), 7, 127);
            var left = 67d;
            if (Math.Abs(blip.DistanceMeters) < 10)
                left = radar.BlindSpotLeft && !radar.BlindSpotRight ? 19 : radar.BlindSpotRight && !radar.BlindSpotLeft ? 115 : 67;
            built.Add(new RadarDot
            {
                Left = left,
                Top = top,
                Label = blip.DriverCode,
                // The SDK only gives an exact number for the longitudinal axis -- show it
                // directly on the dot instead of leaving the driver to guess from a color alone.
                DistanceLabel = $"{blip.DistanceMeters:+0;-0;0}m",
                Fill = blip.DistanceMeters >= 0 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 184, 74)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(182, 73, 255))
            });
        }
        ApplyRows(RadarDots, built);
    }

    private void ApplyRaceStart(RaceStartStatus start)
    {
        ClutchPct = start.ClutchPct; ThrottlePct = start.ThrottlePct;
        ClutchText = $"{start.ClutchPct:0}%"; ThrottleText = $"{start.ThrottlePct:0}%";
        // Only relevant while actually staged for a standing start -- hidden the rest of the race.
        _startHelperWidget.DynamicGateOpen = start.ShouldShow;
    }

    private void SetEditing(bool editing)
    {
        IsEditing = editing;
        foreach (var widget in Widgets) widget.SessionVisible = editing || _hasCar;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        const int GwlExStyle = -20, WsExTransparent = 0x20;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, editing ? style & ~WsExTransparent : style | WsExTransparent);
    }

    private static string? FlagAsset(string? country) => country?.ToUpperInvariant() switch
    {
        "BR" or "🇧🇷" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/br.png",
        "US" or "🇺🇸" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/us.png",
        "JP" or "🇯🇵" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/jp.png",
        _ => null
    };

    private static string? BrandAsset(string? maker)
    {
        var name = maker?.ToUpperInvariant() ?? string.Empty;
        var file = name switch
        {
            var n when n.Contains("ASTON") => "aston",
            var n when n.Contains("MERCEDES") => "mercedes",
            var n when n.Contains("MCLAREN") => "mclaren",
            var n when n.Contains("LAMBORGHINI") => "lamborghini",
            var n when n.Contains("CHEVROLET") || n.Contains("CORVETTE") => "chevrolet",
            var n when n.Contains("CADILLAC") => "cadillac",
            var n when n.Contains("PORSCHE") => "porsche",
            var n when n.Contains("FERRARI") => "ferrari",
            var n when n.Contains("FORD") => "ford",
            var n when n.Contains("BMW") => "bmw",
            var n when n.Contains("AUDI") => "audi",
            var n when n.Contains("ACURA") => "acura",
            var n when n.Contains("DALLARA") => "dallara",
            var n when n.Contains("HONDA") => "honda",
            var n when n.Contains("TOYOTA") => "toyota",
            _ => null
        };
        return file is null ? null : $"pack://application:,,,/IracingLiveCoach.App;component/Assets/Brands/Generated/{file}.png";
    }

    // 16/09/2026 UX fix: the countdown text must never go blank and must never show a number
    // disconnected from the real SF23 Overtake System constants -- previously this returned an
    // empty string during cooldown (making the number "disappear") and a hardcoded "200s" while
    // ready that matched neither OtsActiveSeconds nor OtsCooldownSeconds. Now every state always
    // shows a real number: counting down while active/cooling down, or the full available window
    // while ready -- only the color (see P2PBrush) should be what changes at a glance.
    private static string FormatP2P(bool? active, int? uses, double? seconds, bool cooldown)
    {
        if (active is null) return "--";
        if (active == true && seconds is double remaining) return $"{Math.Ceiling(remaining):0}s";
        if (cooldown && seconds is double recharge) return $"{Math.Ceiling(recharge):0}s";
        return $"{TelemetryReader.OtsActiveSeconds:0}s";
    }

    private static string FormatP2PSummary(RelativeRow? mine)
    {
        if (mine is null || mine.P2PActive is null) return "NOT AVAILABLE FOR THIS CAR";
        var count = mine.P2PUsesRemaining is int uses ? $" · {uses} RESTANTES" : string.Empty;
        if (mine.P2PActive == true) return $"USANDO · {mine.P2PSecondsRemaining:0}s{count}";
        if (mine.P2PInCooldown) return $"RECARREGANDO · {mine.P2PSecondsRemaining:0}s{count}";
        return $"DISPONÍVEL · {TelemetryReader.OtsActiveSeconds:0}s{count}";
    }
    private static string FormatLap(double? seconds) => seconds is double value && value > 0 ? TimeSpan.FromSeconds(value).ToString(@"m\:ss\.fff") : "--";
    private static string FormatWidgetDriver(string value, string style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return value;
        if (style == "Full") return value;
        if (style == "Code") return parts[^1].Length <= 3 ? parts[^1].ToUpperInvariant() : parts[^1][..3].ToUpperInvariant();
        if (parts.Length == 1) return parts[0];
        return $"{char.ToUpperInvariant(parts[0][0])}. {parts[^1]}";
    }
    private static string PreviewFullName(string display) => display switch
    {
        "L. Martins" => "Lucas Martins", "P. Santos" => "Paulo Santos", "V. Costa" => "Vitor Costa",
        "A. Souza" => "Ana Souza", "B. Rocha" => "Bruno Rocha", _ => display
    };
    private static string PreviewCarNumber(string name) => name switch
    {
        "Lucas Martins" => "#22", "Paulo Santos" => "#69", "Vitor Costa" => "#16", "Ana Souza" => "#45", "Bruno Rocha" => "#88", _ => "--"
    };
    private void RefreshDriverNames(WidgetProfile widget)
    {
        var drivers = widget.IsStandings ? PreviewDrivers : RelativeDrivers;
        foreach (var driver in drivers.Where(driver => !driver.IsClassHeader))
            driver.Driver = FormatWidgetDriver(string.IsNullOrWhiteSpace(driver.RawDriverName) ? driver.Driver : driver.RawDriverName, widget.DriverNameStyle);
    }
    private void RebuildPreviewMulticlass()
    {
        var widget = Widgets.FirstOrDefault(widget => widget.IsStandings);
        if (widget is null) return;
        var source = PreviewDrivers.Where(row => !row.IsClassHeader).ToList();
        if (source.Count == 0) return;
        if (source.Count <= 5) source = ExpandPreviewRows(source);
        var playerClass = source.FirstOrDefault(row => row.IsPlayer)?.ClassName ?? source.First().ClassName;
        var selected = source.GroupBy(row => row.ClassName).OrderBy(group => ClassRank(group.Key)).SelectMany(group =>
        {
            var isPlayerClass = string.Equals(group.Key, playerClass, StringComparison.OrdinalIgnoreCase);
            return !isPlayerClass && !widget.ShowMulticlass ? Enumerable.Empty<DriverRow>() : group.Take(isPlayerClass ? widget.PlayerClassRows : widget.OtherClassRows);
        }).ToList();
        var built = new List<DriverRow>();
        foreach (var group in selected.GroupBy(row => row.ClassName).OrderBy(group => ClassRank(group.Key)))
        {
            built.Add(new DriverRow
            {
                IsClassHeader = true, ClassName = group.Key, PositionBackground = ClassPositionBrush(ClassRank(group.Key)),
                ClassHeaderText = group.Key, ClassHeaderPositionWidth = widget.PositionColumnWidth,
                ClassHeaderFields = widget.HeaderFields
            });
            foreach (var driver in group)
            {
                PopulateFields(driver, widget);
                built.Add(driver);
            }
        }
        ApplyRows(PreviewDrivers, built);
        RefreshDriverNames(widget);
        RebuildRelativePreview();
        RebuildTimingLayout(widget);
    }

    private void RebuildRelativePreview()
    {
        var widget = Widgets.FirstOrDefault(candidate => candidate.IsRelative);
        if (widget is null || _relativePreviewSource.Count == 0) return;
        var playerClass = _relativePreviewSource.FirstOrDefault(row => row.IsPlayer)?.ClassName ?? _relativePreviewSource[0].ClassName;
        var selected = _relativePreviewSource.GroupBy(row => row.ClassName).OrderBy(group => ClassRank(group.Key)).SelectMany(group =>
        {
            var mine = string.Equals(group.Key, playerClass, StringComparison.OrdinalIgnoreCase);
            return !mine && !widget.ShowMulticlass ? Enumerable.Empty<DriverRow>() : group.Take(mine ? widget.PlayerClassRows : widget.OtherClassRows);
        }).ToList();
        var built = new List<DriverRow>();
        foreach (var source in selected)
        {
            var row = ClonePreviewRow(source);
            PopulateFields(row, widget);
            built.Add(row);
        }
        ApplyRows(RelativeDrivers, built);
        RefreshDriverNames(widget);
        RebuildTimingLayout(widget);
    }

    private static DriverRow ClonePreviewRow(DriverRow source) => new()
    {
        Position = source.Position, PositionBackground = source.PositionBackground, CarNumber = source.CarNumber, ClassName = source.ClassName,
        Flag = source.Flag, FlagImage = source.FlagImage, RawDriverName = source.RawDriverName, Driver = source.Driver,
        License = source.License, LicenseBrush = source.LicenseBrush, IRatingValue = source.IRatingValue, IRatingDelta = source.IRatingDelta,
        IRatingDeltaBrush = source.IRatingDeltaBrush, IRatingArrow = source.IRatingArrow, IRatingGain = source.IRatingGain,
        Gap = source.Gap, GapToLeader = source.GapToLeader, Interval = source.Interval, LastLap = source.LastLap, BestLap = source.BestLap, LapDelta = source.LapDelta, Tire = source.Tire,
        P2P = source.P2P, P2PState = source.P2PState, P2PBrush = source.P2PBrush, P2PIcon = source.P2PIcon, P2PLevel = source.P2PLevel,
        Pit = source.Pit, PitBrush = source.PitBrush,
        Manufacturer = source.Manufacturer, BrandImage = source.BrandImage, IsPlayer = source.IsPlayer, IsPlayerClass = source.IsPlayerClass, IsPreview = true
    };
    private static List<DriverRow> ExpandPreviewRows(IReadOnlyList<DriverRow> source)
    {
        var names = new[] { "Alex Dog", "Felipe Murler", "Caio Must", "Rafael Marcello", "Nicolas Catsburg" };
        var numbers = new[] { "#22", "#69", "#16", "#45", "#88" };
        var result = new List<DriverRow>();
        var seenClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in source)
        {
            // One deterministic 20-car pool per class keeps the Studio preview representative
            // at every supported row count; duplicate seed rows must not cap GT3 at five.
            if (!seenClasses.Add(template.ClassName)) continue;
            const int total = 20;
            for (var index = 0; index < total; index++)
            {
                var isPlayer = template.IsPlayer && index == 0;
                result.Add(new DriverRow
                {
                    // CarIdxClassPosition restarts for every class.
                    Position = (index + 1).ToString(CultureInfo.InvariantCulture),
                    CarNumber = numbers[index % numbers.Length], PositionBackground = template.PositionBackground, ClassName = template.ClassName,
                    Flag = template.Flag, FlagImage = template.FlagImage, RawDriverName = isPlayer ? "Vitor Costa" : names[index % names.Length], Driver = isPlayer ? "V. Costa" : names[index % names.Length],
                    License = template.License, LicenseBrush = template.LicenseBrush, IRatingValue = template.IRatingValue, IRatingDelta = template.IRatingDelta,
                    IRatingGain = template.IRatingGain, IRatingDeltaBrush = template.IRatingDeltaBrush, IRatingArrow = template.IRatingArrow,
                    Gap = index == 0 ? "LEADER" : $"+{index * .025:0.000}", GapToLeader = index == 0 ? "LEADER" : $"+{index * .025:0.000}", Interval = index == 0 ? "--" : $"+{index * .012:0.000}",
                    P2P = template.P2P, P2PState = template.P2PState, P2PBrush = template.P2PBrush, P2PIcon = template.P2PIcon, P2PLevel = template.P2PLevel,
                    Pit = index == 0 ? "PIT 10s" : $"L{23 + index % 3} {8 + index}s", PitBrush = PitBrush(index == 0 ? "PIT 10s" : "L23 12s"),
                    Manufacturer = template.Manufacturer, BrandImage = template.BrandImage, IsPlayer = isPlayer, IsPlayerClass = template.IsPlayerClass, IsPreview = true
                });
            }
        }
        return result;
    }
    private static IEnumerable<StandingsRow> SelectStandingsRows(IReadOnlyList<StandingsRow> source, WidgetProfile widget)
    {
        var playerClass = source.FirstOrDefault(row => row.IsPlayer)?.ClassShortName;
        if (string.IsNullOrWhiteSpace(playerClass))
        {
            foreach (var row in source) yield return row;
            yield break;
        }
        var visibleByClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in source.OrderBy(row => row.Position))
        {
            var inPlayerClass = string.Equals(row.ClassShortName, playerClass, StringComparison.OrdinalIgnoreCase);
            if (!inPlayerClass && !widget.ShowMulticlass) continue;
            var limit = inPlayerClass ? widget.PlayerClassRows : widget.OtherClassRows;
            var key = row.ClassShortName ?? string.Empty;
            visibleByClass.TryGetValue(key, out var count);
            if (count >= limit) continue;
            visibleByClass[key] = count + 1;
            yield return row;
        }
    }
    private static IEnumerable<RelativeRow> SelectRelativeRows(IReadOnlyList<RelativeRow> source, WidgetProfile widget)
    {
        var playerClass = source.FirstOrDefault(row => row.IsPlayer)?.ClassShortName;
        if (string.IsNullOrWhiteSpace(playerClass)) { foreach (var row in source) yield return row; yield break; }
        var visibleByClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in source.OrderBy(row => row.PositionOffset))
        {
            var mine = string.Equals(row.ClassShortName, playerClass, StringComparison.OrdinalIgnoreCase);
            if (!mine && !widget.ShowMulticlass) continue;
            var key = row.ClassShortName ?? string.Empty;
            visibleByClass.TryGetValue(key, out var count);
            if (count >= (mine ? widget.PlayerClassRows : widget.OtherClassRows)) continue;
            visibleByClass[key] = count + 1;
            yield return row;
        }
    }
    private static string FormatSigned(double? seconds) => seconds is double value ? $"{value:+0.000;-0.000;0.000}" : "--";
    private static string TireText(int? compound) => compound is int tire && tire >= 0 ? $"P{tire}" : "--";
    private static void PopulateFields(DriverRow row, WidgetProfile widget)
    {
        row.Fields.Clear();
        var visibleColumns = widget.TimingColumns.Where(column => column.IsVisible).ToList();
        // The Studio width is literal.  A selected field is never silently compressed.
        foreach (var column in visibleColumns)
        {
            var (value, accent, p2p) = column.Key switch
            {
                "Gap" => (row.GapToLeader == "--" ? row.Gap : row.GapToLeader, System.Windows.Media.Brushes.White, false),
                "Interval" => (row.Interval == "--" && row.IsPreview ? "+0.482" : row.Interval, new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 197, 215)), false),
                "LastLap" => (row.LastLap == "--" && row.IsPreview ? "1:47.293" : row.LastLap, new SolidColorBrush(System.Windows.Media.Color.FromRgb(105, 221, 236)), false),
                "BestLap" => (row.BestLap == "--" && row.IsPreview ? "1:46.880" : row.BestLap, new SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 233, 95)), false),
                "LapDelta" => (row.LapDelta == "--" && row.IsPreview ? "+0.413" : row.LapDelta, row.DeltaBrush, false),
                "Tire" => (row.Tire == "--" && row.IsPreview ? "M" : row.Tire, new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 184, 75)), false),
                "P2P" => (P2PDisplay(row), row.P2PBrush, true),
                "Pit" => (row.Pit, row.PitBrush, false),
                _ => ("--", System.Windows.Media.Brushes.Gray, false)
            };
            row.Fields.Add(new TimingFieldCell { Label = column.Label, Value = value, Accent = accent, IsP2P = p2p, P2PIcon = row.P2PIcon, P2PLevel = row.P2PLevel, Width = column.Width });
        }
    }
    private static string P2PDisplay(DriverRow row)
    {
        if (row.P2PState == "Unavailable") return "--";
        var digits = new string(row.P2P.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? "--" : $"{digits}s";
    }
    private void RebuildVisibleFields()
    {
        var standings = Widgets.FirstOrDefault(widget => widget.IsStandings);
        var relative = Widgets.FirstOrDefault(widget => widget.IsRelative);
        if (standings is not null) foreach (var row in PreviewDrivers) PopulateFields(row, standings);
        if (relative is not null) foreach (var row in RelativeDrivers) PopulateFields(row, relative);
    }
    private void RebuildTimingLayout(WidgetProfile widget)
    {
        RebuildVisibleFields();
        if (!widget.AutoFitToColumns) return;
        var fieldsWidth = widget.TimingColumns.Where(column => column.IsVisible).Sum(column => column.Width);
        var logicalWidth = widget.PositionColumnWidth + widget.CarNumberColumnWidth + widget.DriverColumnWidth + widget.LicenseColumnWidth + widget.IRatingColumnWidth + fieldsWidth;
        // LayoutTransform scales the layout itself (not just its pixels), so the physical card
        // tracks the selected font scale and cannot leave an artificial strip at the right.
        widget.Width = logicalWidth * widget.FontScale + 10d;
        var tableHeader = widget.IsStandings ? 21d : 20d;
        var classHeaderHeight = widget.IsStandings ? PreviewDrivers.Count(row => row.IsClassHeader) * 37d : 0d;
        var driverHeight = widget.IsStandings ? PreviewDrivers.Count(row => !row.IsClassHeader) * 30d : RelativeDrivers.Count * 27d;
        var logicalHeight = (widget.ShowWidgetHeader ? 29d : 0d) + tableHeader + classHeaderHeight + driverHeight;
        widget.Height = logicalHeight * widget.FontScale + 10d;
    }
    private void SetHeaderValue(string key, string value)
    {
        foreach (var widget in Widgets.Where(widget => widget.IsTimingWidget))
            foreach (var field in widget.HeaderFields.Where(field => field.Key == key)) field.Value = string.IsNullOrWhiteSpace(value) ? "--" : value;
    }
    private static PackIconMaterialKind HeaderIcon(string key) => key switch
    {
        "Class" => PackIconMaterialKind.FlagCheckered, "Session" => PackIconMaterialKind.Flag,
        "Lap" => PackIconMaterialKind.TimerOutline, "Sof" => PackIconMaterialKind.ChartLine,
        "Drivers" => PackIconMaterialKind.AccountMultiple, "Clock" => PackIconMaterialKind.ClockOutline,
        "TrackTemp" => PackIconMaterialKind.Thermometer, "BrakeBias" => PackIconMaterialKind.Car,
        _ => PackIconMaterialKind.InformationOutline
    };
    private static string HeaderLabel(string key) => key switch
    {
        "Class" => "CLASS", "Session" => "SESSION", "Lap" => "LAP", "Sof" => "SOF",
        "Drivers" => "DRIVERS", "Clock" => "HOUR", "TrackTemp" => "TRACK TEMP", "BrakeBias" => "BRAKE BIAS",
        _ => key.ToUpperInvariant()
    };
    private static string P2PState(bool? active, bool cooldown) => active is null ? "Unavailable" : active == true ? "Active" : cooldown ? "Charging" : "Ready";
    private static System.Windows.Media.Brush P2PBrush(bool? active, bool cooldown) => active is null ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(151, 160, 170)) : active == true ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 233, 95)) : cooldown ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 184, 74)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(151, 160, 170));
    private static System.Windows.Media.Brush PitBrush(string status) => status.StartsWith("PIT", StringComparison.OrdinalIgnoreCase)
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 224, 209))
        : status == "--" ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(151, 160, 170))
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 233, 95));
    private static PackIconMaterialKind P2PIcon(bool? active, bool cooldown) => active is null ? PackIconMaterialKind.BatteryOutline : active == true ? PackIconMaterialKind.BatteryCharging80 : cooldown ? PackIconMaterialKind.Battery30 : PackIconMaterialKind.Battery90;
    private static double P2PLevel(bool? active, double? seconds, bool cooldown)
    {
        if (active is null) return 0;
        if (active == true && seconds is double remaining) return Math.Clamp(remaining / TelemetryReader.OtsActiveSeconds * 100d, 0, 100);
        if (cooldown && seconds is double recharge) return Math.Clamp((TelemetryReader.OtsCooldownSeconds - recharge) / TelemetryReader.OtsCooldownSeconds * 100d, 0, 100);
        return 100;
    }
    private static System.Windows.Media.Brush LicenseBrush(string license) => license.StartsWith("A", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 88, 255)) : license.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 151, 87)) : license.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 174, 0)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 52, 230));
    private static System.Windows.Media.Brush IRatingDeltaBrush(double? delta) => (delta ?? 0) >= 0 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 233, 95)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 102));
    private static PackIconMaterialKind IRatingArrow(double? delta) => (delta ?? 0) >= 0 ? PackIconMaterialKind.MenuUp : PackIconMaterialKind.MenuDown;
    private static string TrackCondition(int wetness, bool declaredWet) => wetness switch
    {
        <= 0 when !declaredWet => "Dry",
        <= 1 => "Very lightly wet",
        <= 2 => "Lightly wet",
        <= 3 => "Wet",
        <= 4 => "Very wet",
        _ => declaredWet ? "Wet" : "Dry"
    };
    private static System.Windows.Media.Brush ClassPositionBrush(int rank) => new LinearGradientBrush(rank switch
    {
        0 => System.Windows.Media.Color.FromArgb(190, 255, 196, 42),
        1 => System.Windows.Media.Color.FromArgb(190, 84, 154, 255),
        2 => System.Windows.Media.Color.FromArgb(190, 255, 60, 34),
        _ => System.Windows.Media.Color.FromArgb(190, 77, 233, 95)
    }, System.Windows.Media.Color.FromArgb(15, 0, 0, 0), 0);
    private static int ClassRank(string? className) => className?.ToUpperInvariant() switch
    {
        var name when name?.Contains("GTP") == true => 0,
        var name when name?.Contains("LMP2") == true => 1,
        var name when name?.Contains("GT3") == true => 2,
        _ => 3
    };

    private void OnWidgetMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || sender is not FrameworkElement { DataContext: WidgetProfile widget }) return;
        _dragging = widget; _dragOrigin = e.GetPosition(this); _widgetLeft = widget.Left; _widgetTop = widget.Top;
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }
    private void OnWidgetMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this); _dragging.Left = Math.Max(0, _widgetLeft + point.X - _dragOrigin.X); _dragging.Top = Math.Max(0, _widgetTop + point.Y - _dragOrigin.Y);
    }
    private void OnWidgetMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null) return;
        _dragging = null; Mouse.Capture(null); Save(); e.Handled = true;
    }
    private void OnResizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || sender is not FrameworkElement { DataContext: WidgetProfile widget }) return;
        _dragging = widget;
        _resizing = true;
        _dragOrigin = e.GetPosition(this);
        _widgetWidth = widget.Width;
        _widgetHeight = widget.Height;
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }
    private void OnResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || _dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        _dragging.Width = _widgetWidth + point.X - _dragOrigin.X;
        _dragging.Height = _widgetHeight + point.Y - _dragOrigin.Y;
    }
    private void OnResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        _dragging = null;
        Mouse.Capture(null);
        Save();
        e.Handled = true;
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null) { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
    private void Set(ref double field, double value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null) { if (Math.Abs(field - value) < .01) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
