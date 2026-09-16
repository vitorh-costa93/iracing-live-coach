using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;
using System.Windows.Media;

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
    private readonly Dictionary<string, WidgetLayout> _layoutsByKey = new();

    // 14/09/2026: "deixar oculto até eu ir pra pista" -- starts false (hidden) so the overlay
    // suite doesn't clutter the screen the moment the app launches, before the driver has actually
    // gone out. Combined with each widget's own on/off checkbox below: a widget only actually shows
    // when BOTH the driver wants it visible AND the player is genuinely on track (see
    // ApplyOnTrackGate, called from MainWindow's own TelemetryReader.OnTrackStateChanged subscription).
    private bool _hasEnteredDrivingSession;

    // 14/09/2026: "quando eu clico para destravar... eu devo conseguir ver todos os widgets para
    // poder posicioná-los" -- the on-track gate above must NOT apply while the driver is actively
    // repositioning widgets (unlocked). Starts true (locked) to match MainWindow's own default.
    // See ApplyCombinedVisibility: a widget shows when the checkbox is on AND (on track OR unlocked).
    private bool _isLocked = true;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public ControlPanelWindow(WidgetLayoutStore store, Action onChanged, IEnumerable<(string Key, string DisplayName, Window Window)> widgets)
    {
        InitializeComponent();
        _store = store;
        _onChanged = onChanged;
        DataContext = _viewModel;

        _layout = _store.Get("controlPanel", 360, 560);
        Width = _layout.Width;
        Height = _layout.Height;
        if (_layout.Left is double left && _layout.Top is double top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }

        foreach (var (key, displayName, window) in widgets)
        {
            _widgetsByKey[key] = window;
            var widgetLayout = _store.Get(key, window.Width, window.Height);
            _layoutsByKey[key] = widgetLayout;
            window.Opacity = Math.Clamp(widgetLayout.Opacity, 0.25, 1.0);
            ApplyFontScale(window, widgetLayout.FontScale);
            var row = new ControlPanelRowViewModel(key, displayName, widgetLayout.Visible, window.Opacity, widgetLayout.FontScale, widgetLayout);
            row.VisibilityChanged += visible =>
            {
                widgetLayout.Visible = visible;
                _store.Save();
                ApplyCombinedVisibility(key);
            };
            row.OpacityChanged += opacity =>
            {
                widgetLayout.Opacity = opacity;
                window.Opacity = opacity;
                _store.Save();
            };
            row.FontScaleChanged += scale =>
            {
                widgetLayout.FontScale = scale;
                ApplyFontScale(window, scale);
                _store.Save();
            };
            row.Options.Changed += () =>
            {
                ApplyAppearance(window, widgetLayout);
                ApplyWidgetOptions(key, window, widgetLayout);
                _store.Save();
            };
            _viewModel.Rows.Add(row);
            ApplyAppearance(window, widgetLayout);
            ApplyWidgetOptions(key, window, widgetLayout);
            ApplyCombinedVisibility(key);
        }

        if (_widgetsByKey.TryGetValue("fuel", out var fuelWindow) && fuelWindow is FuelWidget fuel && _layoutsByKey.TryGetValue("fuel", out var fuelLayout))
        {
            _viewModel.FuelColumns.Load(fuelLayout);
            _viewModel.FuelColumns.Changed += () => fuel.SetColumns(
                _viewModel.FuelColumns.Level,
                _viewModel.FuelColumns.UsePerHour,
                _viewModel.FuelColumns.AveragePerLap,
                _viewModel.FuelColumns.LapsRemaining,
                _viewModel.FuelColumns.TimeRemaining);
        }

        if (_widgetsByKey.TryGetValue("standings", out var standingsWindow) && standingsWindow is StandingsWidget standings && _layoutsByKey.TryGetValue("standings", out var standingsLayout))
        {
            _viewModel.StandingsClassRows.Load(standingsLayout);
            standings.SetClassRowLimits(_viewModel.StandingsClassRows.MyClassRows, _viewModel.StandingsClassRows.OtherClassRows);
            standings.SetGapDisplayMode(_viewModel.StandingsClassRows.ShowInterval);
            _viewModel.StandingsClassRows.Changed += () =>
            {
                standingsLayout.StandingsMyClassRows = _viewModel.StandingsClassRows.MyClassRows;
                standingsLayout.StandingsOtherClassRows = _viewModel.StandingsClassRows.OtherClassRows;
                standingsLayout.StandingsShowInterval = _viewModel.StandingsClassRows.ShowInterval;
                _store.Save();
                standings.SetClassRowLimits(_viewModel.StandingsClassRows.MyClassRows, _viewModel.StandingsClassRows.OtherClassRows);
                standings.SetGapDisplayMode(_viewModel.StandingsClassRows.ShowInterval);
            };
        }
    }

    private static void ApplyFontScale(Window window, double scale)
    {
        if (window.Content is not UIElement content) return;
        content.RenderTransformOrigin = new System.Windows.Point(0, 0);
        content.RenderTransform = new ScaleTransform(Math.Clamp(scale, .70, 1.40), Math.Clamp(scale, .70, 1.40));
    }

    private static void ApplyAppearance(Window window, WidgetLayout layout)
    {
        // Widgets use the same named root border.  Applying the radius here lets every widget
        // change from the broadcast's sharp edge to a GoFast-style rounded card live.
        if (window.FindName("OuterBorder") is System.Windows.Controls.Border border)
            border.CornerRadius = new CornerRadius(layout.CornerRadius);

        if (window.Content is not DependencyObject root) return;
        ApplyTextEffects(root, layout.TextShadow);
    }

    private static void ApplyTextEffects(DependencyObject node, bool shadow)
    {
        if (node is System.Windows.Controls.TextBlock text)
        {
            text.Effect = shadow
                ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = .75, Color = Colors.Black }
                : null;
        }
        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++) ApplyTextEffects(VisualTreeHelper.GetChild(node, index), shadow);
    }

    private static void ApplyWidgetOptions(string key, Window window, WidgetLayout layout)
    {
        // These settings affect the actual rendered content, not merely the panel state.
        if (key == "relative" && window is RelativeWidget relative)
            relative.SetPresentation(layout.RelativeRows, layout.CondensedRows);
        if (key == "standings" && window is StandingsWidget standings)
            standings.SetPresentation(layout.StandingsRows, layout.CondensedRows);
        if (key == "radar" && window is RadarWidget radar)
            radar.SetPresentation(layout.RadarRangeMeters, layout.RadarShowDistanceLabels);
        if (key == "startHelper") window.Visibility = layout.StartHelperEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    // A widget is shown when the driver's own checkbox is on AND (the player is on track OR the
    // suite is currently unlocked for editing) -- unlocking always reveals every checked widget so
    // it can be dragged/resized, regardless of where the player actually is; the on-track gate only
    // kicks back in once locked. Called on every checkbox toggle (for just that one widget) and
    // from ApplyOnTrackGate/SetLocked (for all of them, when either signal changes).
    private void ApplyCombinedVisibility(string key)
    {
        if (!_widgetsByKey.TryGetValue(key, out var window) || !_layoutsByKey.TryGetValue(key, out var layout)) return;
        // Once iRacing has put the player in the car, widgets remain available for the rest of
        // that driving session.  PlayerTrackSurface briefly changes in pits, incidents and off
        // track excursions; using it as a live visibility switch made the overlays disappear at
        // exactly the moments in which their information is most useful.
        window.Visibility = layout.Visible && (_hasEnteredDrivingSession || !_isLocked) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Called by MainWindow whenever TelemetryReader.OnTrackStateChanged fires -- re-applies
    /// combined visibility to every registered widget at once. This window itself (the Control
    /// Panel) is NOT gated by track state -- the driver can still open it and toggle widgets on
    /// while sitting in a menu or the pits, per its own existing lock-based visibility.</summary>
    public void ApplyOnTrackGate(bool isOnTrack)
    {
        _hasEnteredDrivingSession |= isOnTrack;
        foreach (var key in _widgetsByKey.Keys) ApplyCombinedVisibility(key);
    }

    // Locking hides this window's own content entirely (Visibility), not just click-through --
    // per this class's own doc comment, it should never be visible while actually driving.
    // Also drives the other widgets' own on-track gate override -- see ApplyCombinedVisibility.
    public void SetLocked(bool locked)
    {
        Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _isLocked = locked;
        ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        foreach (var key in _widgetsByKey.Keys) ApplyCombinedVisibility(key);
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
