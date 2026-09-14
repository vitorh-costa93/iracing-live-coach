// src/IracingLiveCoach.App/RadarWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IracingLiveCoach.App;

public class RadarBlipViewModel
{
    public double Top { get; }
    public string ToolTip { get; }

    // Matches RadarWidget.xaml's own canvas height and player-marker position -- kept in sync
    // manually since this view model has no direct reference to the XAML element (see Phase 2's
    // TrackUsageDotViewModel for the same, already-established pattern in this app).
    public RadarBlipViewModel(RadarBlip blip, double canvasHeight, double playerY, double maxRangeMeters)
    {
        var clamped = System.Math.Clamp(blip.DistanceMeters, -maxRangeMeters, maxRangeMeters);
        // Ahead (positive meters) draws ABOVE the player marker; behind draws below -- matching
        // the spec's own "player fixed at center-bottom, ahead is up" radar convention.
        Top = playerY - (clamped / maxRangeMeters) * playerY;
        ToolTip = blip.DriverCode;
    }
}

public class RadarWidgetViewModel : INotifyPropertyChanged
{
    private const double CanvasHeight = 200.0;
    private const double PlayerY = 170.0; // near the bottom, leaving headroom for "ahead" blips
    private const double MaxRangeMeters = 100.0;

    private bool _blindLeft;
    private bool _blindRight;

    public bool BlindLeft { get => _blindLeft; private set => Set(ref _blindLeft, value); }
    public bool BlindRight { get => _blindRight; private set => Set(ref _blindRight, value); }
    public ObservableCollection<RadarBlipViewModel> Blips { get; } = new();

    public void Apply(RadarStatus status)
    {
        BlindLeft = status.BlindSpotLeft;
        BlindRight = status.BlindSpotRight;

        Blips.Clear();
        foreach (var blip in status.Blips)
            Blips.Add(new RadarBlipViewModel(blip, CanvasHeight, PlayerY, MaxRangeMeters));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
