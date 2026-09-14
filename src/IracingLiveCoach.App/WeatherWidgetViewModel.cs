// src/IracingLiveCoach.App/WeatherWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class TrackUsageDotViewModel
{
    public double Left { get; }
    public string ToolTip { get; }
    public Brush Fill { get; }

    private static readonly Brush PlayerBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));
    private static readonly Brush OtherBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public TrackUsageDotViewModel(TrackPositionDot dot, double barWidth, double dotSize)
    {
        Left = System.Math.Clamp(dot.LapDistPct, 0.0, 1.0) * (barWidth - dotSize);
        ToolTip = dot.DriverCode;
        Fill = dot.IsPlayer ? PlayerBrush : OtherBrush;
    }
}

public class WeatherWidgetViewModel : INotifyPropertyChanged
{
    // Matches the bar's own Width in WeatherWidget.xaml -- kept in sync manually since this view
    // model has no direct reference to the XAML element (the same "computed here, not in XAML"
    // constraint that motivates this whole class -- see this task's own Step 1 comment above).
    private const double BarWidth = 240.0;
    private const double DotSize = 8.0;

    private static readonly string[] WetnessLabels =
    {
        "Desconhecido", "Seca", "Maioria seca", "Levemente úmida (muito)",
        "Levemente úmida", "Moderadamente úmida", "Muito úmida", "Extremamente úmida",
    };

    private string _airTempText = "--";
    private string _trackTempText = "--";
    private string _precipitationText = "--";
    private string _wetnessText = "--";
    private bool _declaredWetVisible;

    public string AirTempText { get => _airTempText; private set => Set(ref _airTempText, value); }
    public string TrackTempText { get => _trackTempText; private set => Set(ref _trackTempText, value); }
    public string PrecipitationText { get => _precipitationText; private set => Set(ref _precipitationText, value); }
    public string WetnessText { get => _wetnessText; private set => Set(ref _wetnessText, value); }
    public bool DeclaredWetVisible { get => _declaredWetVisible; private set => Set(ref _declaredWetVisible, value); }

    public ObservableCollection<TrackUsageDotViewModel> TrackDots { get; } = new();

    public void Apply(WeatherStatus status)
    {
        AirTempText = status.AirTempC.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
        TrackTempText = status.TrackTempC.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
        PrecipitationText = status.PrecipitationPct.ToString("0", CultureInfo.InvariantCulture) + "%";
        WetnessText = status.TrackWetness >= 0 && status.TrackWetness < WetnessLabels.Length
            ? WetnessLabels[status.TrackWetness]
            : "--";
        DeclaredWetVisible = status.WeatherDeclaredWet;

        TrackDots.Clear();
        foreach (var dot in status.CarPositions)
            TrackDots.Add(new TrackUsageDotViewModel(dot, BarWidth, DotSize));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
