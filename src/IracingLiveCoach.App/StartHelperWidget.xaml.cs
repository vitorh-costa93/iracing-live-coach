using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public partial class StartHelperWidget : Window, INotifyPropertyChanged
{
    private readonly WidgetLayout _layout; private readonly Action _onChanged;
    private double _clutch, _throttle;
    public IntPtr Handle => new WindowInteropHelper(this).Handle;
    public double Clutch { get => _clutch; private set { _clutch = value; OnPropertyChanged(); } }
    public double Throttle { get => _throttle; private set { _throttle = value; OnPropertyChanged(); } }
    public StartHelperWidget(WidgetLayout layout, Action onChanged)
    {
        InitializeComponent(); DataContext = this; _layout = layout; _onChanged = onChanged;
        Width = layout.Width; Height = layout.Height;
        if (layout.Left is double left && layout.Top is double top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
    }
    public void Update(RaceStartStatus status) { Clutch = status.ClutchPct; Throttle = status.ThrottlePct; Visibility = status.ShouldShow && _layout.Visible ? Visibility.Visible : Visibility.Collapsed; }
    public void SetLocked(bool locked) { ClickThrough.Set(Handle, locked); ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible; OuterBorder.BorderBrush = locked ? (System.Windows.Media.Brush)FindResource("F1BorderIdleBrush") : (System.Windows.Media.Brush)FindResource("F1BorderActiveBrush"); }
    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); Persist(); } }
    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e) { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }
    private void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e) => Persist();
    private void Persist() { _layout.Left = Left; _layout.Top = Top; _layout.Width = Width; _layout.Height = Height; _onChanged(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
