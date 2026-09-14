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
        // the spec's own "player fixed at center-bottom, ahead is up" radar convention. The two
        // directions have different amounts of canvas space available (playerY sits near the
        // bottom on purpose) so each direction is scaled against its own actual span, not a
        // shared one -- reusing playerY for the "behind" scale would push blips past the bottom
        // edge of the canvas.
        double top;
        if (clamped >= 0)
        {
            // Ahead: maps [0, maxRangeMeters] onto [playerY, 0] -- the headroom above the player marker.
            top = playerY - (clamped / maxRangeMeters) * playerY;
        }
        else
        {
            // Behind: maps [0, maxRangeMeters] onto [playerY, canvasHeight] -- the space below the
            // player marker, which is NOT the same size as the headroom above it.
            var behindSpan = canvasHeight - playerY;
            top = playerY + (-clamped / maxRangeMeters) * behindSpan;
        }
        Top = top;
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
