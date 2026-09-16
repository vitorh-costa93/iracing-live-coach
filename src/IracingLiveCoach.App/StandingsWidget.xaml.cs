// src/IracingLiveCoach.App/StandingsWidget.xaml.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>F1-style full classification widget -- shows every driver's position, code and last
/// lap time in one panel. Same drag/resize/lock mechanics as the other overlay windows (see
/// RelativeOverlayWindow's own comment for why they're duplicated rather than shared).</summary>
public partial class StandingsWidget : Window
{
    private readonly WidgetLayout _layout;
    private readonly Action _onChanged;
    private readonly StandingsWidgetViewModel _viewModel = new();
    private int _rowLimit = 8;
    private bool _condensed = true;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public StandingsWidget(WidgetLayout layout, Action onChanged)
    {
        InitializeComponent();
        _layout = layout;
        _onChanged = onChanged;
        DataContext = _viewModel;

        Width = _layout.Width;
        Height = _layout.Height;
        if (_layout.Left is double left && _layout.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    public void UpdateRows(List<StandingsRow> rows)
    {
        _viewModel.SetRows(rows);
        // Compact automatically to the useful number of rows. This prevents a solo test drive
        // reserving an empty half-screen while still allowing a larger multiclass field.
        Height = Math.Clamp(110 + Math.Min(_viewModel.Rows.Count, _rowLimit) * (_condensed ? 27 : 34), MinHeight, 520);
    }

    public void UpdateSessionStatus(SessionStatus status) => _viewModel.ApplySessionStatus(status);

    public void SetClassRowLimits(int myClassLimit, int otherClassLimit) => _viewModel.SetClassRowLimits(myClassLimit, otherClassLimit);

    public void SetGapDisplayMode(bool showInterval) => _viewModel.SetGapDisplayMode(showInterval);

    public void SetPresentation(int rows, bool condensed)
    {
        _rowLimit = Math.Clamp(rows, 1, 30);
        _condensed = condensed;
        _viewModel.SetDisplayRowLimit(_rowLimit);
    }

    public void SetLocked(bool locked)
    {
        ClickThrough.Set(Handle, locked);
        ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        OuterBorder.BorderBrush = locked
            ? (Brush)FindResource("F1BorderIdleBrush")
            : (Brush)FindResource("F1BorderActiveBrush");
    }

    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || BroadcastChrome.IsInteractive(e.OriginalSource as DependencyObject)) return;
        DragMove();
        PersistLayout();
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e) => PersistLayout();

    private void PersistLayout()
    {
        _layout.Left = Left;
        _layout.Top = Top;
        _layout.Width = Width;
        _layout.Height = Height;
        _onChanged();
    }
}
