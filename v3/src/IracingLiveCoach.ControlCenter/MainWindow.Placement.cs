using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;

using LayoutColumn = IracingLiveCoach.OverlayHost.Layout.ColumnDefinition;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Right-hand "Dimensões e posição" card: position, monitor, width mode/limit, lock and
/// click-through, the width-limit warning with "Ajustar larguras", plus the Layout/Aparência tab controls
/// that map onto the placement record (opacity, scale, restore).</summary>
public partial class MainWindow
{
    private const float DefaultWidthLimit = 480f;
    private List<MonitorRect> _monitors = [];

    private void LoadPlacementIntoControls(string widget)
    {
        bool previous = _suppressChangeEvents;
        _suppressChangeEvents = true;
        try
        {
            var state = _state[widget];
            XBox.Text = state.X.ToString("0", CultureInfo.InvariantCulture);
            YBox.Text = state.Y.ToString("0", CultureInfo.InvariantCulture);
            WidthBox.Text = state.Width.ToString("0", CultureInfo.InvariantCulture);
            HeightBox.Text = state.Height.ToString("0", CultureInfo.InvariantCulture);
            WidthModeBox.SelectedIndex = state.AutoSize ? 0 : 1;
            LockedBox.IsChecked = state.Locked;
            ApplyLockUi(state.Locked, state.AutoSize);
            ClickThroughBox.IsChecked = state.ClickThrough;
            OpacitySlider.Value = state.Opacity;
            ScaleSlider.Value = state.Scale;
            LimitBox.Text = WidthLimitFor(widget).ToString("0", CultureInfo.InvariantCulture);
            SelectMonitorFor(state);
            if (_sidebarSwitches.TryGetValue(widget, out var sw)) sw.IsChecked = state.Visible;
        }
        finally { _suppressChangeEvents = previous; }
    }

    private void PositionChanged(object sender, TextChangedEventArgs e) => ApplyControlsToStateAndSend();
    private void ToggleChanged(object sender, RoutedEventArgs e) => ApplyControlsToStateAndSend();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyControlsToStateAndSend();

    private void WidthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        ApplyLockUi(LockedBox.IsChecked == true, WidthModeBox.SelectedIndex != 1);
        ApplyControlsToStateAndSend();
    }

    private void ApplyControlsToStateAndSend()
    {
        if (_suppressChangeEvents) return;
        if (!float.TryParse(XBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return;
        if (!float.TryParse(YBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return;
        if (!float.TryParse(WidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) return;
        if (!float.TryParse(HeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return;

        var previous = _state[_selectedWidget];
        bool locked = LockedBox.IsChecked == true;
        var state = new WidgetUiState(x, y, width, height,
            previous.Visible, locked,
            (float)OpacitySlider.Value, (float)ScaleSlider.Value, ClickThroughBox.IsChecked == true, WidthModeBox.SelectedIndex != 1);
        // A locked widget keeps its position, size and scale (the overlay ignores those edits too).
        if (previous.Locked && locked)
            state = state with { X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, Scale = previous.Scale, AutoSize = previous.AutoSize };
        _state[_selectedWidget] = state;
        ApplyLockUi(locked, state.AutoSize);
        UpdateWidthWarning();
        _ = SendPlacementAsync(_selectedWidget);
    }

    /// <summary>Sends one widget's state to the overlay and mirrors it into the local profile (the preview reads it).</summary>
    private async Task SendPlacementAsync(string key)
    {
        var state = _state[key];
        var existing = _profileStore.Get(key);
        var placement = new WidgetPlacement(existing?.MonitorIndex ?? 0, state.X, state.Y, existing?.Anchor ?? PlacementAnchor.TopLeft,
            state.Width, state.Height, state.Scale, state.Locked, existing?.ZOrder ?? Array.IndexOf(AllWidgetKeys, key),
            state.Visible, state.Opacity, state.ClickThrough, state.AutoSize);
        _profileStore.ReplacePlacements([new KeyValuePair<string, WidgetPlacement>(key, placement)]);
        _previewHost?.ApplyProfile(_profileStore);

        bool sent = await _client.SendAsync(key, state);
        Dispatcher.Invoke(() => SetStatus(sent
            ? "Conectado — overlay atualizado ao vivo."
            : "Overlay fechado ou inacessível — as mudanças valem quando ele abrir."));
    }

    /// <summary>While a widget is locked its position, size, monitor and scale can't change, so those
    /// fields are disabled instead of accepting edits that would silently do nothing.</summary>
    private void ApplyLockUi(bool locked, bool autoSize)
    {
        XBox.IsEnabled = YBox.IsEnabled = MonitorBox.IsEnabled = WidthModeBox.IsEnabled = ScaleSlider.IsEnabled = !locked;
        WidthBox.IsEnabled = HeightBox.IsEnabled = !locked && !autoSize;
    }

    // --- Monitor ---

    private void LoadMonitors()
    {
        _monitors = MonitorEnumerator.GetMonitors();
        Quiet(() =>
        {
            MonitorBox.ItemsSource = _monitors.Select((m, i) => $"Monitor {i + 1}").ToList();
            if (_monitors.Count > 0) MonitorBox.SelectedIndex = 0;
        });
    }

    private void SelectMonitorFor(WidgetUiState state)
    {
        if (_monitors.Count == 0) return;
        int index = _monitors.FindIndex(m => state.X >= m.Left && state.X < m.Left + m.Width && state.Y >= m.Top && state.Y < m.Top + m.Height);
        MonitorBox.SelectedIndex = Math.Max(0, index);
    }

    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents || MonitorBox.SelectedIndex < 0 || MonitorBox.SelectedIndex >= _monitors.Count) return;
        var target = _monitors[MonitorBox.SelectedIndex];
        var state = _state[_selectedWidget];
        var current = _monitors.FirstOrDefault(m => state.X >= m.Left && state.X < m.Left + m.Width && state.Y >= m.Top && state.Y < m.Top + m.Height) ?? _monitors[0];
        if (current == target) return;
        float newX = target.Left + Math.Clamp(state.X - current.Left, 0, Math.Max(0, target.Width - 40));
        float newY = target.Top + Math.Clamp(state.Y - current.Top, 0, Math.Max(0, target.Height - 40));
        _state[_selectedWidget] = state with { X = newX, Y = newY };
        LoadPlacementIntoControls(_selectedWidget);
        _ = SendPlacementAsync(_selectedWidget);
        SetStatus($"Movido para o monitor {MonitorBox.SelectedIndex + 1}.");
    }

    // --- Width limit ("Limite de largura") ---

    private readonly Dictionary<string, float> _widthLimits = LoadWidthLimits();

    private static string SettingsPath => Path.Combine(ProfileFiles.DataDir, "control-center.json");

    private static Dictionary<string, float> LoadWidthLimits()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(SettingsPath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return [];
    }

    private void SaveWidthLimits()
    {
        try
        {
            Directory.CreateDirectory(ProfileFiles.DataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_widthLimits));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Default per-widget limit: 25% of a 1920 px monitor (480) for everything except Standings, whose
    /// mockup layout (multi-class, 11 columns) is ~800 px wide by design.</summary>
    private float WidthLimitFor(string widget) =>
        _widthLimits.TryGetValue(widget, out var limit) && limit >= 100 ? limit : widget == "standings" ? StandingsDefaultWidthLimit : DefaultWidthLimit;

    private const float StandingsDefaultWidthLimit = 800f;

    private void LimitChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        if (float.TryParse(LimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) && limit >= 100)
        {
            _widthLimits[_selectedWidget] = limit;
            Debounce("limits", SaveWidthLimits, 500);
        }
        UpdateWidthWarning();
    }

    /// <summary>Width the widget will actually occupy: for table widgets the sum of the visible columns
    /// (plus the class-strip margin) — what auto width follows; for the others the placement width.</summary>
    private float CurrentWidgetWidth() =>
        _currentColumns.Count == 0 ? _state[_selectedWidget].Width : ColumnFitter.TotalWidth(_currentColumns, CurrentAppearancePaddingH());

    private void UpdateWidthWarning()
    {
        if (WidthWarning is null) return;
        float limit = WidthLimitFor(_selectedWidget);
        float width = CurrentWidgetWidth();
        int over = (int)Math.Ceiling(width - limit);
        bool exceeds = over > 0;
        WidthWarningRow.Visibility = exceeds ? Visibility.Visible : Visibility.Collapsed;
        FitWidthsButton.Visibility = exceeds && _currentColumns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WidthOk.Visibility = exceeds ? Visibility.Collapsed : Visibility.Visible;
        WidthWarning.Text = $"{over} px excedem o limite de {limit:0} px.";
    }

    /// <summary>"Ajustar larguras": see <see cref="ColumnFitter.Fit"/>.</summary>
    private void FitWidths(object sender, RoutedEventArgs e)
    {
        if (_currentColumns.Count == 0) return;
        float limit = WidthLimitFor(_selectedWidget);
        if (CurrentWidgetWidth() <= limit) return;

        _currentColumns = ColumnFitter.Fit(_currentColumns, DefaultColumnsFor(_selectedWidget), limit, CurrentAppearancePaddingH(), out float remaining);
        RefreshColumnList();
        ApplyColumnsNow();
        UpdateWidthWarning();
        SetStatus(remaining > 0.5f
            ? $"Larguras ajustadas, mas ainda faltam {Math.Ceiling(remaining):0} px para o limite — oculte colunas para caber."
            : "Larguras ajustadas ao limite.");
    }

    // --- Layout / Aparência tab ---

    private async void RestoreDefaultPlacement(object sender, RoutedEventArgs e)
    {
        var defaults = BuildDefaults();
        if (!defaults.TryGetValue(_selectedWidget, out var defaultState)) return;
        _state[_selectedWidget] = defaultState with { Visible = _state[_selectedWidget].Visible };
        LoadPlacementIntoControls(_selectedWidget);
        UpdateWidthWarning();
        await SendPlacementAsync(_selectedWidget);
    }
}
