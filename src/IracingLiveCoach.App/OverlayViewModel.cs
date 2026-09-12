using System.Collections.ObjectModel;
using System.ComponentModel;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>What stage the overlay is in before/while a corner is being compared -- drives the
/// small status dot's color (see MainWindow.xaml) so the driver has an at-a-glance signal without
/// reading the status text itself.</summary>
public enum CoachStatus { Waiting, Loading, Ready }

/// <summary>Backs the overlay window's display -- holds the most recent CornerFeedback per
/// corner number (wrapped as CornerRowViewModel, which does the label/unit formatting) so the UI
/// can show "last time through this corner" rather than only a single most-recent-of-any-corner
/// value. Implements INotifyPropertyChanged so WPF data binding updates the window automatically
/// when LiveCoachEngine.CornerCompleted fires, and to reflect StatusText/Status changes (session
/// detection / baseline load progress) before any corner has completed.</summary>
public class OverlayViewModel : INotifyPropertyChanged
{
    private string _statusText = "";
    private CoachStatus _status = CoachStatus.Waiting;

    private static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));
    private static readonly Brush LoadingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66));
    private static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB2));

    public ObservableCollection<CornerRowViewModel> RecentCorners { get; } = new();

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

    public CoachStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        }
    }

    public Brush StatusBrush => Status switch
    {
        CoachStatus.Loading => LoadingBrush,
        CoachStatus.Ready => ReadyBrush,
        _ => WaitingBrush,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnCornerCompleted(CornerFeedback feedback)
    {
        RecentCorners.Insert(0, new CornerRowViewModel(feedback));
        while (RecentCorners.Count > 5) RecentCorners.RemoveAt(RecentCorners.Count - 1);
    }
}
