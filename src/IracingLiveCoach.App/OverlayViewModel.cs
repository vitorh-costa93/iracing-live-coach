using System.Collections.ObjectModel;
using System.ComponentModel;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>Backs the overlay window's display -- holds the most recent CornerFeedback per
/// corner number so the UI can show "last time through this corner" rather than only a single
/// most-recent-of-any-corner value. Implements INotifyPropertyChanged so WPF data binding
/// updates the window automatically when LiveCoachEngine.CornerCompleted fires.</summary>
public class OverlayViewModel : INotifyPropertyChanged
{
    public ObservableCollection<CornerFeedback> RecentCorners { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnCornerCompleted(CornerFeedback feedback)
    {
        RecentCorners.Insert(0, feedback);
        while (RecentCorners.Count > 5) RecentCorners.RemoveAt(RecentCorners.Count - 1);
    }
}
