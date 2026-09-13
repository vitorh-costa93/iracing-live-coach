using System.Collections.ObjectModel;
using System.ComponentModel;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>One row in the P2P strip -- "P+1" (car directly behind), "P+2", etc., plus whether that
/// car currently has push-to-pass engaged. Colored like Wheelspin in the main overlay (a real
/// good/bad-for-you signal gets a real color; nothing else does): active P2P behind you is worth a
/// glance, so it gets the same warn color CornerRowViewModel uses for wheelspin.</summary>
public class RelativeRowViewModel
{
    public string Label { get; }
    public string P2PText { get; }
    public Brush P2PBrush { get; }

    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x54, 0x70));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));

    public RelativeRowViewModel(RelativeCarStatus status)
    {
        Label = $"P+{status.PositionOffset}";
        P2PText = status.P2PActive ? "P2P" : "--";
        P2PBrush = status.P2PActive ? WarnBrush : NeutralBrush;
    }
}

/// <summary>Backs the thin P2P strip window (RelativeOverlayWindow) -- deliberately separate from
/// OverlayViewModel/MainWindow, so the driver can position this strip right next to iRacing's own
/// native Relative box (13/09/2026: "eu queria que isso estivesse junto da black box de relative do
/// iRacing, o que já é nativo") independently of the corner-coaching card.</summary>
public class RelativeViewModel : INotifyPropertyChanged
{
    private string _statusText = "Aguardando sessão do iRacing...";

    public ObservableCollection<RelativeRowViewModel> Rows { get; } = new();

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetRows(System.Collections.Generic.List<RelativeCarStatus> statuses)
    {
        StatusText = statuses.Count > 0 ? "" : "Ninguém atrás";
        Rows.Clear();
        foreach (var status in statuses) Rows.Add(new RelativeRowViewModel(status));
    }
}
