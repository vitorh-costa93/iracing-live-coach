using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows;
using System.Text.Json.Serialization;
using Brush = System.Windows.Media.Brush;
using MahApps.Metro.IconPacks;

namespace IracingLiveCoach.V2;

public enum WidgetKind { Standings, Relative, Fuel, Weather, Radar, StartHelper }

public sealed class WidgetProfile : INotifyPropertyChanged
{
    private bool _enabled = true, _showHeader = true, _compact = true, _showFlags = true, _showLogos = true, _showP2PColumn;
    private bool _showFuelLastLap = true, _showFuelFiveLap = true, _showFuelMax = true, _showFuelLaps = true;
    private double _left, _top, _width, _height, _fontScale = 1, _opacity = .96;
    private double _positionColumnWidth = 32, _carNumberColumnWidth = 42, _driverColumnWidth = 170, _licenseColumnWidth = 52, _iRatingColumnWidth = 82;
    private bool _sessionVisible = true, _isSingleClassSession;
    private int _rows = 5, _refreshFps = 60, _radarRange = 55, _playerClassRows = 5, _otherClassRows = 2;
    private string _driverNameStyle = "Abbreviated";
    private bool _showMulticlass = true;
    private bool _columnLayoutInitialized;
    private bool _autoFitToColumns = true;
    public WidgetKind Kind { get; init; }
    public string Title { get; init; } = "";
    public bool IsStandings => Kind == WidgetKind.Standings;
    public bool IsRelative => Kind == WidgetKind.Relative;
    public bool IsFuel => Kind == WidgetKind.Fuel;
    public bool IsWeather => Kind == WidgetKind.Weather;
    public bool IsRadar => Kind == WidgetKind.Radar;
    public bool IsStartHelper => Kind == WidgetKind.StartHelper;
    public bool IsTimingWidget => IsStandings || IsRelative;
    public double Left { get => _left; set => Set(ref _left, value); }
    public double Top { get => _top; set => Set(ref _top, value); }
    // A timing card is content-driven by default: no selected field is silently squeezed.
    public double Width { get => _width; set => Set(ref _width, Math.Clamp(value, 180, 1600)); }
    public double Height { get => _height; set => Set(ref _height, Math.Clamp(value, 48, 1600)); }
    [JsonIgnore]
    public bool IsSingleClassSession { get => _isSingleClassSession; set => Set(ref _isSingleClassSession, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool SessionVisible { get => _sessionVisible; set { if (_sessionVisible == value) return; _sessionVisible = value; PropertyChanged?.Invoke(this, new(nameof(SessionVisible))); PropertyChanged?.Invoke(this, new(nameof(WidgetVisible))); } }
    private bool _dynamicGateOpen = true;
    // Radar/StartHelper only render themselves while there is something relevant to show (a
    // nearby car, or the grid before a standing start) -- every other widget ignores this gate.
    public bool DynamicGateOpen { get => _dynamicGateOpen; set { if (_dynamicGateOpen == value) return; _dynamicGateOpen = value; PropertyChanged?.Invoke(this, new(nameof(DynamicGateOpen))); PropertyChanged?.Invoke(this, new(nameof(WidgetVisible))); } }
    public bool WidgetVisible => Enabled && SessionVisible && (DynamicGateOpen || (Kind != WidgetKind.Radar && Kind != WidgetKind.StartHelper));
    public bool ShowHeader { get => _showHeader; set { if (_showHeader == value) return; _showHeader = value; PropertyChanged?.Invoke(this, new(nameof(ShowHeader))); PropertyChanged?.Invoke(this, new(nameof(HeaderHeight))); } }
    // Standings carries the selected session fields in each class header; Relative keeps its
    // single header because it has no per-class bands.
    public bool ShowWidgetHeader => ShowHeader && !IsStandings;
    public GridLength HeaderHeight => ShowWidgetHeader ? new GridLength(29) : new GridLength(0);
    public bool Compact { get => _compact; set => Set(ref _compact, value); }
    public bool ShowFlags { get => _showFlags; set => Set(ref _showFlags, value); }
    public bool ShowLogos { get => _showLogos; set => Set(ref _showLogos, value); }
    /// <summary>Only meaningful for timing tables. It is off by default to preserve compact layouts.</summary>
    public bool ShowP2PColumn
    {
        get => _showP2PColumn;
        set
        {
            if (_showP2PColumn == value) return;
            _showP2PColumn = value;
            PropertyChanged?.Invoke(this, new(nameof(ShowP2PColumn)));
            PropertyChanged?.Invoke(this, new(nameof(P2PColumnWidth)));
        }
    }
    public GridLength P2PColumnWidth => ShowP2PColumn ? new GridLength(54) : new GridLength(0);
    public double FontScale { get => _fontScale; set => Set(ref _fontScale, Math.Clamp(value, .70, 1.40)); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, .35, 1)); }
    public int Rows { get => _rows; set => Set(ref _rows, Math.Clamp(value, 1, 20)); }
    public int RefreshFps { get => _refreshFps; set => Set(ref _refreshFps, Math.Clamp(value, 10, 60)); }
    public int RadarRange { get => _radarRange; set => Set(ref _radarRange, Math.Clamp(value, 15, 150)); }
    public string DriverNameStyle { get => _driverNameStyle; set => Set(ref _driverNameStyle, value); }
    public bool ShowMulticlass { get => _showMulticlass; set => Set(ref _showMulticlass, value); }
    public int PlayerClassRows { get => _playerClassRows; set => Set(ref _playerClassRows, Math.Clamp(value, 1, 20)); }
    public int OtherClassRows { get => _otherClassRows; set => Set(ref _otherClassRows, Math.Clamp(value, 0, 20)); }
    private int _gapDecimals = 3, _intervalDecimals = 3, _lapDeltaDecimals = 3;
    // How many decimal places GAP/INTERVAL/Δ VOLTA show (0-3) -- e.g. "+1.234" at 3, "+1" at 0.
    public int GapDecimals { get => _gapDecimals; set => Set(ref _gapDecimals, Math.Clamp(value, 0, 3)); }
    public int IntervalDecimals { get => _intervalDecimals; set => Set(ref _intervalDecimals, Math.Clamp(value, 0, 3)); }
    public int LapDeltaDecimals { get => _lapDeltaDecimals; set => Set(ref _lapDeltaDecimals, Math.Clamp(value, 0, 3)); }
    private int _topNFixed;
    // Standings-only: a fixed leaderboard (P1..N) always shown ahead of the player-centered window
    // sized by PlayerClassRows -- e.g. TopNFixed=2 + PlayerClassRows=5 with the player at P8 shows
    // P1, P2, then P6-P10 (2 ahead of the player, the player, 2 behind), per the driver's own
    // worked example (16/09/2026). 0 disables the fixed leaderboard entirely.
    public int TopNFixed { get => _topNFixed; set => Set(ref _topNFixed, Math.Clamp(value, 0, 10)); }
    public double PositionColumnWidth { get => _positionColumnWidth; set => Set(ref _positionColumnWidth, Math.Clamp(value, 26, 90)); }
    public double CarNumberColumnWidth { get => _carNumberColumnWidth; set => Set(ref _carNumberColumnWidth, Math.Clamp(value, 34, 120)); }
    public double DriverColumnWidth { get => _driverColumnWidth; set => Set(ref _driverColumnWidth, Math.Clamp(value, 90, 360)); }
    public double LicenseColumnWidth { get => _licenseColumnWidth; set => Set(ref _licenseColumnWidth, Math.Clamp(value, 42, 120)); }
    public double IRatingColumnWidth { get => _iRatingColumnWidth; set => Set(ref _iRatingColumnWidth, Math.Clamp(value, 60, 170)); }
    public bool ShowFuelLastLap { get => _showFuelLastLap; set => Set(ref _showFuelLastLap, value); }
    public bool ShowFuelFiveLap { get => _showFuelFiveLap; set => Set(ref _showFuelFiveLap, value); }
    public bool ShowFuelMax { get => _showFuelMax; set => Set(ref _showFuelMax, value); }
    public bool ShowFuelLaps { get => _showFuelLaps; set => Set(ref _showFuelLaps, value); }
    public bool ColumnLayoutInitialized { get => _columnLayoutInitialized; set => Set(ref _columnLayoutInitialized, value); }
    public bool AutoFitToColumns { get => _autoFitToColumns; set => Set(ref _autoFitToColumns, value); }
    public ObservableCollection<TimingColumn> TimingColumns { get; set; } = new();
    public ObservableCollection<HeaderField> HeaderFields { get; set; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}

public sealed class TimingColumn : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private double _width = 58;
    public string Key { get; init; } = "";
    public string Label { get; set; } = "";
    public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; PropertyChanged?.Invoke(this, new(nameof(IsVisible))); } }
    public double Width { get => _width; set { var clamped = Math.Clamp(value, 34, 180); if (Math.Abs(_width - clamped) < .1) return; _width = clamped; PropertyChanged?.Invoke(this, new(nameof(Width))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static ObservableCollection<TimingColumn> CreateDefaults() => new()
    {
        new() { Key="Gap", Label="GAP" }, new() { Key="Interval", Label="INTERVAL" },
        new() { Key="LastLap", Label="LAST LAP", IsVisible=false }, new() { Key="BestLap", Label="BEST LAP", IsVisible=false },
        new() { Key="LapDelta", Label="LAP DELTA", IsVisible=false }, new() { Key="Tire", Label="TIRE", IsVisible=false },
        new() { Key="P2P", Label="P2P" }, new() { Key="Pit", Label="PIT", Width=62 }
    };
}

public sealed class HeaderField : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private string _value = "--";
    public string Key { get; init; } = "";
    public string Label { get; set; } = "";
    public PackIconMaterialKind Icon { get; set; } = PackIconMaterialKind.InformationOutline;
    public string Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
    public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; PropertyChanged?.Invoke(this, new(nameof(IsVisible))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static ObservableCollection<HeaderField> CreateDefaults() => new()
    {
        new() { Key="Class", Label="CLASS", Value="GTP", Icon=PackIconMaterialKind.FlagCheckered }, new() { Key="Session", Label="SESSION", Value="RACE", Icon=PackIconMaterialKind.Flag }, new() { Key="Lap", Label="LAP", Value="LAP 4 / 16", Icon=PackIconMaterialKind.TimerOutline },
        new() { Key="Sof", Label="SOF", Value="SOF 4.2K", Icon=PackIconMaterialKind.ChartLine }, new() { Key="Drivers", Label="DRIVERS", Value="26 DRIVERS", Icon=PackIconMaterialKind.AccountMultiple }, new() { Key="Clock", Label="HOUR", Value="14:45", Icon=PackIconMaterialKind.ClockOutline },
        new() { Key="TrackTemp", Label="TRACK TEMP", Value="TRACK 31°C", Icon=PackIconMaterialKind.Thermometer }, new() { Key="BrakeBias", Label="BRAKE BIAS", Value="BB 56.5%", Icon=PackIconMaterialKind.Car }
    };
}

public sealed class OverlayProfile
{
    public bool Locked { get; set; } = true;
    public ObservableCollection<WidgetProfile> Widgets { get; set; } = new();
    public static OverlayProfile CreateDefault() => new()
    {
        Widgets = new()
        {
            new() { Kind=WidgetKind.Standings, Title="STANDINGS", Left=18, Top=50, Width=800, Height=310, Rows=8, ShowP2PColumn=true, TopNFixed=3, TimingColumns=TimingColumn.CreateDefaults(), HeaderFields=HeaderField.CreateDefaults() },
            new() { Kind=WidgetKind.Relative, Title="RELATIVE", Left=1180, Top=670, Width=700, Height=255, Rows=5, ShowP2PColumn=true, TimingColumns=TimingColumn.CreateDefaults(), HeaderFields=HeaderField.CreateDefaults() },
            new() { Kind=WidgetKind.Fuel, Title="FUEL", Left=1210, Top=815, Width=250, Height=175 },
            new() { Kind=WidgetKind.Weather, Title="WEATHER", Left=1210, Top=655, Width=250, Height=145 },
            new() { Kind=WidgetKind.Radar, Title="RADAR", Left=860, Top=100, Width=190, Height=190, RadarRange=55 },
            new() { Kind=WidgetKind.StartHelper, Title="START HELPER", Left=790, Top=790, Width=320, Height=85 }
        }
    };
}

public sealed class DriverRow
{
    public string Position { get; init; } = "";
    public Brush PositionBackground { get; init; } = System.Windows.Media.Brushes.Transparent;
    public string Flag { get; init; } = "";
    public string? FlagImage { get; init; }
    public string Driver { get; set; } = "";
    public string RawDriverName { get; set; } = "";
    public string CarNumber { get; set; } = "--";
    public string License { get; init; } = "";
    public Brush LicenseBrush { get; init; } = System.Windows.Media.Brushes.DodgerBlue;
    public string IRating { get; init; } = "";
    public string IRatingValue { get; init; } = "";
    public string IRatingDelta { get; init; } = "";
    public bool IRatingGain { get; init; }
    public Brush IRatingDeltaBrush { get; init; } = System.Windows.Media.Brushes.LightGray;
    public PackIconMaterialKind IRatingArrow { get; init; } = PackIconMaterialKind.MenuUp;
    public string Delta { get; init; } = "";
    public string Gap { get; init; } = "";
    public string P2P { get; init; } = "";
    public string P2PState { get; init; } = "Unavailable";
    public Brush P2PBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public PackIconMaterialKind P2PIcon { get; init; } = PackIconMaterialKind.BatteryOutline;
    public double P2PLevel { get; init; }
    public string Pit { get; init; } = "--";
    public Brush PitBrush { get; init; } = System.Windows.Media.Brushes.LightGray;
    public bool IsPreview { get; set; }
    public string Manufacturer { get; init; } = "";
    public string? BrandImage { get; init; }
    public bool IsPlayer { get; init; }
    public Brush DeltaBrush { get; init; } = System.Windows.Media.Brushes.White;
    public string GapToLeader { get; init; } = "--";
    public string Interval { get; init; } = "--";
    public string LastLap { get; init; } = "--";
    public string BestLap { get; init; } = "--";
    public string LapDelta { get; init; } = "--";
    public string Tire { get; init; } = "--";
    public string ClassName { get; init; } = "GT3";
    public bool IsPlayerClass { get; init; }
    public bool IsClassHeader { get; init; }
    public bool IsDriverRow => !IsClassHeader;
    public string ClassHeaderText { get; init; } = "";
    public double ClassHeaderPositionWidth { get; init; } = 32;
    public IEnumerable<HeaderField> ClassHeaderFields { get; init; } = Array.Empty<HeaderField>();
    public ObservableCollection<TimingFieldCell> Fields { get; } = new();
}

public sealed class TimingFieldCell
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "--";
    public Brush Accent { get; init; } = System.Windows.Media.Brushes.LightGray;
    public bool IsP2P { get; init; }
    public PackIconMaterialKind P2PIcon { get; init; } = PackIconMaterialKind.BatteryOutline;
    public double P2PLevel { get; init; }
    public double Width { get; init; } = 58;
    public string NonP2PValue => IsP2P ? string.Empty : Value;
    public string P2PSeconds => IsP2P ? Value : string.Empty;
}

/// <summary>Display-only radar point. Longitudinal placement is live iRacing telemetry; lateral
/// placement is only asserted when iRacing's left/right proximity channel confirms a side.</summary>
public sealed class RadarDot
{
    public double Left { get; init; }
    public double Top { get; init; }
    public string Label { get; init; } = "";
    /// <summary>Real longitudinal distance (meters, signed) -- the SDK's only exact per-car
    /// proximity number; shown directly on the dot instead of only a generic nearest-car text.</summary>
    public string DistanceLabel { get; init; } = "";
    public Brush Fill { get; init; } = System.Windows.Media.Brushes.White;
}
