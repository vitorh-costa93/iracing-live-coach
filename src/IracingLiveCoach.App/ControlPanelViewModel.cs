using System.Collections.ObjectModel;
using System.ComponentModel;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public class ControlPanelRowViewModel : INotifyPropertyChanged
{
    private bool _visible;
    private double _opacity;
    private double _fontScale;
    public string DisplayName { get; }
    public string Key { get; }
    public WidgetOptionsViewModel Options { get; }
    public bool IsRelative => Key == "relative";
    public bool IsStandings => Key == "standings";
    public bool IsTimingTable => IsRelative || IsStandings;
    public bool IsRadar => Key == "radar";
    public bool IsStartHelper => Key == "startHelper";

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
            VisibilityChanged?.Invoke(value);
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = System.Math.Clamp(value, 0.25, 1.0);
            if (System.Math.Abs(_opacity - clamped) < 0.01) return;
            _opacity = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
            OpacityChanged?.Invoke(clamped);
        }
    }
    public double FontScale
    {
        get => _fontScale;
        set
        {
            var clamped = System.Math.Clamp(value, 0.70, 1.40);
            if (System.Math.Abs(_fontScale - clamped) < 0.01) return;
            _fontScale = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontScale)));
            FontScaleChanged?.Invoke(clamped);
        }
    }

    public event System.Action<bool>? VisibilityChanged;
    public event System.Action<double>? OpacityChanged;
    public event System.Action<double>? FontScaleChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ControlPanelRowViewModel(string key, string displayName, bool initiallyVisible, double initialOpacity, double initialFontScale, WidgetLayout layout)
    {
        Key = key;
        DisplayName = displayName;
        _visible = initiallyVisible;
        _opacity = initialOpacity;
        _fontScale = initialFontScale;
        Options = new WidgetOptionsViewModel(layout);
    }
}

/// <summary>
/// The full per-widget control surface.  It is intentionally a real settings model rather than
/// a collection of one-off checkboxes in the window: every value is saved with that widget and
/// raises Changed immediately, so changing a setting never needs an Apply button or a restart.
/// The labels mirror the useful parts of Kapps/GoFast (appearance, information, rows and refresh).
/// </summary>
public sealed class WidgetOptionsViewModel : INotifyPropertyChanged
{
    private readonly WidgetLayout _layout;
    public WidgetOptionsViewModel(WidgetLayout layout) => _layout = layout;
    public event System.Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public double BackgroundBrightness { get => _layout.BackgroundBrightness; set => Set(value, v => _layout.BackgroundBrightness = System.Math.Clamp(v, .45, 1.25)); }
    public double BackgroundSaturation { get => _layout.BackgroundSaturation; set => Set(value, v => _layout.BackgroundSaturation = System.Math.Clamp(v, 0, 1.25)); }
    public double CornerRadius { get => _layout.CornerRadius; set => Set(value, v => _layout.CornerRadius = System.Math.Clamp(v, 0, 18)); }
    public bool TextShadow { get => _layout.TextShadow; set => Set(value, v => _layout.TextShadow = v); }
    public bool ShowHeader { get => _layout.ShowHeader; set => Set(value, v => _layout.ShowHeader = v); }
    public bool HideInReplay { get => _layout.HideInReplay; set => Set(value, v => _layout.HideInReplay = v); }
    public int RenderFps { get => _layout.RenderFps; set => Set(value, v => _layout.RenderFps = System.Math.Clamp(v, 10, 60)); }
    public int RelativeRows { get => _layout.RelativeRows; set => Set(value, v => _layout.RelativeRows = System.Math.Clamp(v, 2, 12)); }
    public int StandingsRows { get => _layout.StandingsRows; set => Set(value, v => _layout.StandingsRows = System.Math.Clamp(v, 1, 30)); }
    public bool CondensedRows { get => _layout.CondensedRows; set => Set(value, v => _layout.CondensedRows = v); }
    public bool ShowFlags { get => _layout.ShowFlags; set => Set(value, v => _layout.ShowFlags = v); }
    public bool ShowManufacturerLogos { get => _layout.ShowManufacturerLogos; set => Set(value, v => _layout.ShowManufacturerLogos = v); }
    public bool ShowIRatingGain { get => _layout.ShowIRatingGain; set => Set(value, v => _layout.ShowIRatingGain = v); }
    public bool HighlightCarsAlongside { get => _layout.HighlightCarsAlongside; set => Set(value, v => _layout.HighlightCarsAlongside = v); }
    public bool ShowSessionInfo { get => _layout.ShowSessionInfo; set => Set(value, v => _layout.ShowSessionInfo = v); }
    public bool ShowTrackInfo { get => _layout.ShowTrackInfo; set => Set(value, v => _layout.ShowTrackInfo = v); }
    public bool ShowCarInfo { get => _layout.ShowCarInfo; set => Set(value, v => _layout.ShowCarInfo = v); }
    public int RadarRangeMeters { get => _layout.RadarRangeMeters; set => Set(value, v => _layout.RadarRangeMeters = System.Math.Clamp(v, 15, 150)); }
    public bool RadarShowDistanceLabels { get => _layout.RadarShowDistanceLabels; set => Set(value, v => _layout.RadarShowDistanceLabels = v); }
    public bool StartHelperEnabled { get => _layout.StartHelperEnabled; set => Set(value, v => _layout.StartHelperEnabled = v); }

    private void Set<T>(T value, System.Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}

public class ControlPanelViewModel
{
    public ObservableCollection<ControlPanelRowViewModel> Rows { get; } = new();
    public FuelColumnsViewModel FuelColumns { get; } = new();
    public StandingsClassRowsViewModel StandingsClassRows { get; } = new();
}

// "Em Standings as classes não se misturam, igual no Kapps e eu posso escolher quantos eu quero
// mostrar da minha classe e das outras" (14/09/2026) -- 0 in either field means "show all".
public class StandingsClassRowsViewModel : INotifyPropertyChanged
{
    private int _myClassRows;
    private int _otherClassRows = 3;
    private bool _showInterval;
    public int MyClassRows { get => _myClassRows; set => Set(ref _myClassRows, value); }
    public int OtherClassRows { get => _otherClassRows; set => Set(ref _otherClassRows, value); }
    // "quero em standings ter a opção de interval, não só gap" (14/09/2026).
    public bool ShowInterval { get => _showInterval; set => Set(ref _showInterval, value); }
    public event System.Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Load(WidgetLayout layout)
    {
        _myClassRows = layout.StandingsMyClassRows;
        _otherClassRows = layout.StandingsOtherClassRows;
        _showInterval = layout.StandingsShowInterval;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    private void Set(ref int field, int value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
    private void Set(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}

public class FuelColumnsViewModel : INotifyPropertyChanged
{
    private bool _level = true, _usePerHour = true, _averagePerLap = true, _lapsRemaining = true, _timeRemaining = true;
    public bool Level { get => _level; set => Set(ref _level, value); }
    public bool UsePerHour { get => _usePerHour; set => Set(ref _usePerHour, value); }
    public bool AveragePerLap { get => _averagePerLap; set => Set(ref _averagePerLap, value); }
    public bool LapsRemaining { get => _lapsRemaining; set => Set(ref _lapsRemaining, value); }
    public bool TimeRemaining { get => _timeRemaining; set => Set(ref _timeRemaining, value); }
    public event System.Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Load(WidgetLayout layout)
    {
        _level = layout.FuelShowLevel; _usePerHour = layout.FuelShowUsePerHour; _averagePerLap = layout.FuelShowAveragePerLap;
        _lapsRemaining = layout.FuelShowLapsRemaining; _timeRemaining = layout.FuelShowTimeRemaining;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    private void Set(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}
