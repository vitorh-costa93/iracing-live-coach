using System.Collections.ObjectModel;
using System.ComponentModel;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public class ControlPanelRowViewModel : INotifyPropertyChanged
{
    private bool _visible;
    private double _opacity;
    public string DisplayName { get; }
    public string Key { get; }

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

    public event System.Action<bool>? VisibilityChanged;
    public event System.Action<double>? OpacityChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ControlPanelRowViewModel(string key, string displayName, bool initiallyVisible, double initialOpacity)
    {
        Key = key;
        DisplayName = displayName;
        _visible = initiallyVisible;
        _opacity = initialOpacity;
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
