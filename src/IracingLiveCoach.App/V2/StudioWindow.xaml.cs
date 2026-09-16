using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IracingLiveCoach.V2;

public partial class StudioWindow : Window, INotifyPropertyChanged
{
    private readonly OverlayProfile _profile;
    private readonly Action _save;
    private readonly Action<bool> _setEditing;
    private readonly DispatcherTimer _saveTimer;
    public ObservableCollection<WidgetProfile> Widgets => _profile.Widgets;
    public string LockLabel => _profile.Locked ? "DESTRAVAR E EDITAR" : "TRAVAR OVERLAY";
    public ICommand ToggleLockCommand { get; }
    public ICommand MoveColumnLeftCommand { get; }
    public ICommand MoveColumnRightCommand { get; }
    public ICommand MoveHeaderLeftCommand { get; }
    public ICommand MoveHeaderRightCommand { get; }
    public ICommand IncreasePlayerRowsCommand { get; }
    public ICommand DecreasePlayerRowsCommand { get; }
    public ICommand IncreaseOtherRowsCommand { get; }
    public ICommand DecreaseOtherRowsCommand { get; }
    public StudioWindow(OverlayProfile profile, ObservableCollection<DriverRow> previewDrivers, Action save, Action<bool> setEditing)
    {
        _profile = profile; _save = save; _setEditing = setEditing;
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _save(); };
        ToggleLockCommand = new RelayCommand(ToggleLock);
        MoveColumnLeftCommand = new ParameterRelayCommand(column => MoveColumn((TimingColumn)column!, -1));
        MoveColumnRightCommand = new ParameterRelayCommand(column => MoveColumn((TimingColumn)column!, 1));
        MoveHeaderLeftCommand = new ParameterRelayCommand(field => MoveHeader((HeaderField)field!, -1));
        MoveHeaderRightCommand = new ParameterRelayCommand(field => MoveHeader((HeaderField)field!, 1));
        IncreasePlayerRowsCommand = new ParameterRelayCommand(widget => AdjustRows((WidgetProfile)widget!, true, 1));
        DecreasePlayerRowsCommand = new ParameterRelayCommand(widget => AdjustRows((WidgetProfile)widget!, true, -1));
        IncreaseOtherRowsCommand = new ParameterRelayCommand(widget => AdjustRows((WidgetProfile)widget!, false, 1));
        DecreaseOtherRowsCommand = new ParameterRelayCommand(widget => AdjustRows((WidgetProfile)widget!, false, -1));
        foreach (var widget in Widgets)
        {
            widget.PropertyChanged += (_, _) => ScheduleSave();
            widget.TimingColumns.CollectionChanged += (_, _) => ScheduleSave();
            foreach (var column in widget.TimingColumns) column.PropertyChanged += (_, _) => ScheduleSave();
            widget.HeaderFields.CollectionChanged += (_, _) => ScheduleSave();
            foreach (var field in widget.HeaderFields) field.PropertyChanged += (_, _) => ScheduleSave();
        }
        DataContext = this;
    }
    private void ToggleLock() { _profile.Locked = !_profile.Locked; _setEditing(!_profile.Locked); _save(); PropertyChanged?.Invoke(this, new(nameof(LockLabel))); }
    private void MoveColumn(TimingColumn column, int direction)
    {
        var widget = Widgets.FirstOrDefault(item => item.TimingColumns.Contains(column));
        if (widget is null) return;
        var current = widget.TimingColumns.IndexOf(column);
        var next = Math.Clamp(current + direction, 0, widget.TimingColumns.Count - 1);
        if (current == next) return;
        widget.TimingColumns.Move(current, next);
        _save();
    }
    private void MoveHeader(HeaderField field, int direction)
    {
        var widget = Widgets.FirstOrDefault(item => item.HeaderFields.Contains(field));
        if (widget is null) return;
        var current = widget.HeaderFields.IndexOf(field);
        var next = Math.Clamp(current + direction, 0, widget.HeaderFields.Count - 1);
        if (current == next) return;
        widget.HeaderFields.Move(current, next);
        _save();
    }
    private void AdjustRows(WidgetProfile widget, bool playerClass, int amount)
    {
        if (playerClass) widget.PlayerClassRows += amount;
        else widget.OtherClassRows += amount;
        _save();
    }
    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _action;
    public RelayCommand(Action action) => _action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public sealed class ParameterRelayCommand : ICommand
{
    private readonly Action<object?> _action;
    public ParameterRelayCommand(Action<object?> action) => _action = action;
    public bool CanExecute(object? parameter) => parameter is not null;
    public void Execute(object? parameter) => _action(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
