// src/IracingLiveCoach.App/FuelWidget.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>F1-style fuel calculator widget. Same drag/resize/lock mechanics as every other
/// overlay widget window (see RelativeOverlayWindow's own comment for why they're duplicated
/// rather than shared).</summary>
public partial class FuelWidget : Window
{
    private readonly WidgetLayout _layout;
    private readonly Action _onChanged;
    private readonly FuelWidgetViewModel _viewModel = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public FuelWidget(WidgetLayout layout, Action onChanged)
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

    public void UpdateStatus(FuelStatus status) => _viewModel.Apply(status);

    public void SetLocked(bool locked)
    {
        ClickThrough.Set(Handle, locked);
        OuterBorder.BorderBrush = locked
            ? (Brush)FindResource("F1BorderIdleBrush")
            : (Brush)FindResource("F1BorderActiveBrush");
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
        _layout.Left = Left;
        _layout.Top = Top;
        _layout.Width = Width;
        _layout.Height = Height;
        _onChanged();
    }
}
