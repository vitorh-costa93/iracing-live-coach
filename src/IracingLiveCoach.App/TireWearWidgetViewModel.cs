// src/IracingLiveCoach.App/TireWearWidgetViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class TireCornerViewModel
{
    public Brush FillL { get; }
    public Brush FillM { get; }
    public Brush FillR { get; }

    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB2));
    private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2C));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));

    public TireCornerViewModel(TireCornerWear wear)
    {
        FillL = ColorFor(wear.TreadL);
        FillM = ColorFor(wear.TreadM);
        FillR = ColorFor(wear.TreadR);
    }

    private static Brush ColorFor(double remainingFraction) => remainingFraction switch
    {
        > 0.6 => GreenBrush,
        > 0.3 => AmberBrush,
        _ => RedBrush,
    };
}

public class TireWearWidgetViewModel : INotifyPropertyChanged
{
    private TireCornerViewModel _lf = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _rf = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _lr = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _rr = new(new TireCornerWear(1, 1, 1));
    private string _statusText = "Sem parada ainda";

    public TireCornerViewModel LF { get => _lf; private set => Set(ref _lf, value); }
    public TireCornerViewModel RF { get => _rf; private set => Set(ref _rf, value); }
    public TireCornerViewModel LR { get => _lr; private set => Set(ref _lr, value); }
    public TireCornerViewModel RR { get => _rr; private set => Set(ref _rr, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public void Apply(TireWearStatus status)
    {
        LF = new TireCornerViewModel(status.LF);
        RF = new TireCornerViewModel(status.RF);
        LR = new TireCornerViewModel(status.LR);
        RR = new TireCornerViewModel(status.RR);
        // This label IS the deliverable, not a footnote -- see this plan's own Global Constraints
        // and TireWearStatus's own doc comment for why "sem parada ainda" (rather than a timestamp
        // implying a live reading) is the honest default state.
        StatusText = status.LastChangedAtUtc is System.DateTime changedAtUtc
            ? "Atualizado às " + changedAtUtc.ToLocalTime().ToString("HH:mm:ss")
            : "Sem parada ainda";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
