// src/IracingLiveCoach.App/FuelWidgetViewModel.cs
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IracingLiveCoach.App;

public class FuelWidgetViewModel : INotifyPropertyChanged
{
    private string _fuelLevelText = "--";
    private string _fuelUsePerHourText = "--";
    private string _averagePerLapText = "--";
    private string _lapsRemainingText = "--";
    private string _timeRemainingText = "--";
    private bool _showLevel = true;
    private bool _showUsePerHour = true;
    private bool _showAveragePerLap = true;
    private bool _showLapsRemaining = true;
    private bool _showTimeRemaining = true;

    public string FuelLevelText { get => _fuelLevelText; private set => Set(ref _fuelLevelText, value); }
    public string FuelUsePerHourText { get => _fuelUsePerHourText; private set => Set(ref _fuelUsePerHourText, value); }
    public string AveragePerLapText { get => _averagePerLapText; private set => Set(ref _averagePerLapText, value); }
    public string LapsRemainingText { get => _lapsRemainingText; private set => Set(ref _lapsRemainingText, value); }
    public string TimeRemainingText { get => _timeRemainingText; private set => Set(ref _timeRemainingText, value); }
    public bool ShowLevel { get => _showLevel; set => Set(ref _showLevel, value); }
    public bool ShowUsePerHour { get => _showUsePerHour; set => Set(ref _showUsePerHour, value); }
    public bool ShowAveragePerLap { get => _showAveragePerLap; set => Set(ref _showAveragePerLap, value); }
    public bool ShowLapsRemaining { get => _showLapsRemaining; set => Set(ref _showLapsRemaining, value); }
    public bool ShowTimeRemaining { get => _showTimeRemaining; set => Set(ref _showTimeRemaining, value); }

    public void Apply(FuelStatus status)
    {
        FuelLevelText = status.FuelLevelLiters.ToString("0.0", CultureInfo.InvariantCulture) + " L";
        FuelUsePerHourText = status.FuelUsePerHourLiters.ToString("0.0", CultureInfo.InvariantCulture) + " L/h";
        AveragePerLapText = status.AverageFuelPerLapLiters is double perLap
            ? perLap.ToString("0.00", CultureInfo.InvariantCulture) + " L/lap"
            : "--";
        LapsRemainingText = status.LapsRemaining is double laps
            ? laps.ToString("0.0", CultureInfo.InvariantCulture)
            : "--";
        TimeRemainingText = status.TimeRemainingSeconds is double seconds
            ? FormatTimeRemaining(seconds)
            : "--";
    }

    // "mm" alone is minutes-within-the-hour (00-59) with no hour component, so any estimate past
    // 60 minutes silently wraps (e.g. 90 min -> "30:00", indistinguishable from 30 min remaining).
    // Full-tank estimates for GT3/GTP/LMP-class cars routinely exceed an hour early in a session,
    // so this needs an explicit hour component once the estimate crosses that threshold. Clamped
    // against TimeSpan.FromSeconds' ~10^8-day ceiling in case a near-zero avgFuelPerLap sample ever
    // produces an astronomically large estimate.
    private static string FormatTimeRemaining(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return "--";
        var clamped = System.Math.Min(seconds, System.TimeSpan.MaxValue.TotalSeconds);
        var span = System.TimeSpan.FromSeconds(clamped);
        return span.TotalSeconds >= 3600
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(@"m\:ss");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
