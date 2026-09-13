using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>The P2P strip -- a second, independently movable/resizable overlay window (same
/// drag/resize mechanics as MainWindow, duplicated rather than shared: each window owns its own
/// Left/Top/Width/Height and border element, and the two behaviors are only ~15 lines each).
/// Locking is NOT independent, though -- MainWindow's tray-icon toggle locks both windows together
/// (see MainWindow.ToggleLock), since they're meant to be positioned once and then both stay out of
/// the way while actually driving.</summary>
public partial class RelativeOverlayWindow : Window
{
    private readonly AppSettings _settings;
    private readonly RelativeViewModel _viewModel = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public RelativeOverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = _viewModel;

        Width = _settings.RelativeWidth;
        Height = _settings.RelativeHeight;
        if (_settings.RelativeLeft is double left && _settings.RelativeTop is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    public void UpdateRows(List<RelativeCarStatus> statuses) => _viewModel.SetRows(statuses);

    public void SetLocked(bool locked)
    {
        ClickThrough.Set(Handle, locked);
        OuterBorder.BorderBrush = locked
            ? (Brush)FindResource("HudBorderBrush")
            : (Brush)FindResource("HudBorderActiveBrush");
    }

    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
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
        _settings.RelativeLeft = Left;
        _settings.RelativeTop = Top;
        _settings.RelativeWidth = Width;
        _settings.RelativeHeight = Height;
        _settings.Save();
    }
}
