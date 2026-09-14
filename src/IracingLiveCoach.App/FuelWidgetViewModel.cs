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

    public string FuelLevelText { get => _fuelLevelText; private set => Set(ref _fuelLevelText, value); }
    public string FuelUsePerHourText { get => _fuelUsePerHourText; private set => Set(ref _fuelUsePerHourText, value); }
    public string AveragePerLapText { get => _averagePerLapText; private set => Set(ref _averagePerLapText, value); }
    public string LapsRemainingText { get => _lapsRemainingText; private set => Set(ref _lapsRemainingText, value); }
    public string TimeRemainingText { get => _timeRemainingText; private set => Set(ref _timeRemainingText, value); }

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
            ? System.TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss")
            : "--";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
