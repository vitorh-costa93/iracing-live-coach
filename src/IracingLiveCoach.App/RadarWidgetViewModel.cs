// src/IracingLiveCoach.App/RadarWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IracingLiveCoach.App;

public class RadarBlipViewModel
{
    public double Top { get; }
    public double Left { get; }
    public string ToolTip { get; }
    public string DistanceText { get; }

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
        // iRacing supplies exact longitudinal separation for every car. It only supplies lateral
        // occupation as an aggregate blind-spot signal, so far-field cars stay on the centre line
        // instead of inventing a lane that the SDK did not report.
        Left = 72;
        ToolTip = blip.DriverCode;
        DistanceText = $"{(blip.DistanceMeters >= 0 ? "+" : "")}{blip.DistanceMeters:0}m";
    }
}

public class RadarWidgetViewModel : INotifyPropertyChanged
{
    private const double CanvasHeight = 180.0;
    private const double PlayerY = 90.0;
    private double _maxRangeMeters = 55.0;
    private bool _showDistanceLabels = true;
    private RadarStatus? _lastStatus;

    private bool _blindLeft;
    private bool _blindRight;
    private bool _showNoTrackLength;
    private bool _hasProximity;

    public bool BlindLeft { get => _blindLeft; private set => Set(ref _blindLeft, value); }
    public bool BlindRight { get => _blindRight; private set => Set(ref _blindRight, value); }
    public bool ShowNoTrackLength { get => _showNoTrackLength; private set => Set(ref _showNoTrackLength, value); }
    public bool HasProximity { get => _hasProximity; private set => Set(ref _hasProximity, value); }
    public bool ShowDistanceLabels { get => _showDistanceLabels; private set => Set(ref _showDistanceLabels, value); }
    public ObservableCollection<RadarBlipViewModel> Blips { get; } = new();

    public void Apply(RadarStatus status)
    {
        _lastStatus = status;
        BlindLeft = status.BlindSpotLeft;
        BlindRight = status.BlindSpotRight;
        ShowNoTrackLength = !status.HasTrackLength;
        HasProximity = status.BlindSpotLeft || status.BlindSpotRight || status.Blips.Count > 0;

        Blips.Clear();
        foreach (var blip in status.Blips)
            Blips.Add(new RadarBlipViewModel(blip, CanvasHeight, PlayerY, _maxRangeMeters));
    }

    public void SetPresentation(int rangeMeters, bool showDistanceLabels)
    {
        _maxRangeMeters = System.Math.Clamp(rangeMeters, 15, 150);
        ShowDistanceLabels = showDistanceLabels;
        if (_lastStatus is not null) Apply(_lastStatus);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
