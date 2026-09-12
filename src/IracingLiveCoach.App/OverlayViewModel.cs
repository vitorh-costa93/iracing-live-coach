using System.Collections.ObjectModel;
using System.ComponentModel;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>Backs the overlay window's display -- holds the most recent CornerFeedback per
/// corner number so the UI can show "last time through this corner" rather than only a single
/// most-recent-of-any-corner value. Implements INotifyPropertyChanged so WPF data binding
/// updates the window automatically when LiveCoachEngine.CornerCompleted fires, and to reflect
/// StatusText changes (session detection / baseline load progress) before any corner has
/// completed.</summary>
public class OverlayViewModel : INotifyPropertyChanged
{
    private string _statusText = "";

    public ObservableCollection<CornerFeedback> RecentCorners { get; } = new();

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

    public void OnCornerCompleted(CornerFeedback feedback)
    {
        RecentCorners.Insert(0, feedback);
        while (RecentCorners.Count > 5) RecentCorners.RemoveAt(RecentCorners.Count - 1);
    }
}
