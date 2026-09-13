using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IracingLiveCoach.App;

public class ControlPanelRowViewModel : INotifyPropertyChanged
{
    private bool _visible;
    public string DisplayName { get; }
    public string Key { get; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
            VisibilityChanged?.Invoke(value);
        }
    }

    public event System.Action<bool>? VisibilityChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ControlPanelRowViewModel(string key, string displayName, bool initiallyVisible)
    {
        Key = key;
        DisplayName = displayName;
        _visible = initiallyVisible;
    }
}

public class ControlPanelViewModel
{
    public ObservableCollection<ControlPanelRowViewModel> Rows { get; } = new();
}
