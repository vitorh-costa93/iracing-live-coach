// src/IracingLiveCoach.App/RelativeWidget.xaml.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>F1-style full running order widget -- distinct from RelativeOverlayWindow's thin P2P
/// strip, this shows every driver's gap and P2P badge in one panel. Same drag/resize/lock mechanics
/// as the other overlay windows (see RelativeOverlayWindow's own comment for why they're duplicated
/// rather than shared).</summary>
public partial class RelativeWidget : Window
{
    private readonly WidgetLayout _layout;
    private readonly Action _onChanged;
    private readonly RelativeWidgetViewModel _viewModel = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public RelativeWidget(WidgetLayout layout, Action onChanged)
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

    public void UpdatePlayerStatus(PlayerCarStatus status) => _viewModel.ApplyPlayerStatus(status);

    public void UpdateSessionStatus(SessionStatus status) => _viewModel.ApplySessionStatus(status);

    // Only the primary (player-relative) instance passes false here -- the secondary,
    // class-scoped instance's PositionOffset is an absolute class position, not a player offset.
    public void UpdateRows(List<RelativeRow> rows, bool showClassPositionAsAbsolute = false) => _viewModel.SetRows(rows, showClassPositionAsAbsolute);

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
