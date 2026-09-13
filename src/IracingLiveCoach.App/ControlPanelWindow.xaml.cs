using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>Lists every registered widget with a visibility checkbox -- the direct equivalent of
/// Kapps' own layersControlPanel (see the spec's own reference to %AppData%\Kapps\settings.json),
/// restyled to the F1 theme. Unlike every other widget window, this one is EXCLUDED from the
/// shared click-through lock (it must stay interactive to be useful) but hides its own content
/// entirely while the suite is locked, matching Kapps' own "only appears when configuring
/// something" behavior.</summary>
public partial class ControlPanelWindow : Window
{
    private readonly WidgetLayoutStore _store;
    private readonly Action _onChanged;
    private readonly WidgetLayout _layout;
    private readonly ControlPanelViewModel _viewModel = new();
    private readonly Dictionary<string, Window> _widgetsByKey = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public ControlPanelWindow(WidgetLayoutStore store, Action onChanged, IEnumerable<(string Key, string DisplayName, Window Window)> widgets)
    {
        InitializeComponent();
        _store = store;
        _onChanged = onChanged;
        DataContext = _viewModel;

        _layout = _store.Get("controlPanel", 220, 160);
        Width = _layout.Width;
        Height = _layout.Height;
        if (_layout.Left is double left && _layout.Top is double top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }

        foreach (var (key, displayName, window) in widgets)
        {
            _widgetsByKey[key] = window;
            var widgetLayout = _store.Get(key, window.Width, window.Height);
            var row = new ControlPanelRowViewModel(key, displayName, widgetLayout.Visible);
            row.VisibilityChanged += visible =>
            {
                widgetLayout.Visible = visible;
                window.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                _store.Save();
            };
            _viewModel.Rows.Add(row);
            window.Visibility = widgetLayout.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Locking hides this window's own content entirely (Visibility), not just click-through --
    // per this class's own doc comment, it should never be visible while actually driving.
    public void SetLocked(bool locked) => Visibility = locked ? Visibility.Collapsed : Visibility.Visible;

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
