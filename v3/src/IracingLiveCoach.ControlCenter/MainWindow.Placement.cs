using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IracingLiveCoach.OverlayHost.Layout;

using LayoutColumn = IracingLiveCoach.OverlayHost.Layout.ColumnDefinition;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Right-hand "Dimensões e posição" card: position, monitor, width mode/limit, lock and
/// click-through, the width-limit warning with "Ajustar larguras", plus the Layout/Aparência tab controls
/// that map onto the placement record (opacity, scale, restore).</summary>
public partial class MainWindow
{
    private const float DefaultWidthLimit = 480f;
    private const float TableLeftMargin = 8f;
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
            WidthBox.IsEnabled = HeightBox.IsEnabled = !state.AutoSize;
            LockedBox.IsChecked = state.Locked;
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
        WidthBox.IsEnabled = HeightBox.IsEnabled = WidthModeBox.SelectedIndex == 1;
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
        var state = new WidgetUiState(x, y, width, height,
            previous.Visible, LockedBox.IsChecked == true,
            (float)OpacitySlider.Value, (float)ScaleSlider.Value, ClickThroughBox.IsChecked == true, WidthModeBox.SelectedIndex != 1);
        _state[_selectedWidget] = state;
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
    private float CurrentWidgetWidth()
    {
        if (_currentColumns.Count == 0) return _state[_selectedWidget].Width;
        float padding = CurrentAppearancePaddingH();
        return TableLeftMargin + _currentColumns.Where(c => c.Visible).Sum(c => FootprintOf(c, padding));
    }

    private static float FootprintOf(LayoutColumn column, float paddingH) =>
        column.WidthPx + column.PaddingLeftPx + (paddingH >= 0 && column.PaddingRightPx > 0 ? paddingH : column.PaddingRightPx);

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

    /// <summary>"Ajustar larguras": first narrows the flexible name column, then trims the other data
    /// columns proportionally (never below 80% of their default width). Icons (position/flag/brand) keep their size.</summary>
    private void FitWidths(object sender, RoutedEventArgs e)
    {
        if (_currentColumns.Count == 0) return;
        float limit = WidthLimitFor(_selectedWidget);
        float over = CurrentWidgetWidth() - limit;
        if (over <= 0) return;

        int nameIndex = _currentColumns.FindIndex(c => c.Key == "name" && c.Visible);
        if (nameIndex >= 0)
        {
            var name = _currentColumns[nameIndex];
            float newWidth = Math.Max(90f, name.WidthPx - over);
            over -= name.WidthPx - newWidth;
            _currentColumns[nameIndex] = name with { WidthPx = newWidth, MinWidthPx = Math.Min(name.MinWidthPx, newWidth) };
        }

        string[] keepSize = ["position", "flag", "brand", "name"];
        for (int pass = 0; pass < 6 && over > 0.5f; pass++)
        {
            var candidates = _currentColumns.Select((c, i) => (c, i)).Where(t => t.c.Visible && !keepSize.Contains(t.c.Key)).ToList();
            var roomTotal = candidates.Sum(t => Math.Max(0f, t.c.WidthPx - FitFloor * DefaultWidthOf(t.c.Key, t.c.WidthPx)));
            if (roomTotal < 1f) break;
            foreach (var (column, index) in candidates)
            {
                float room = Math.Max(0f, column.WidthPx - FitFloor * DefaultWidthOf(column.Key, column.WidthPx));
                float cut = Math.Min(room, over * room / roomTotal);
                _currentColumns[index] = column with { WidthPx = MathF.Floor(column.WidthPx - cut), MinWidthPx = Math.Min(column.MinWidthPx, MathF.Floor(column.WidthPx - cut)) };
            }
            over = CurrentWidgetWidth() - limit;
        }

        RefreshColumnList();
        ApplyColumnsNow();
        UpdateWidthWarning();
        float remaining = CurrentWidgetWidth() - limit;
        SetStatus(remaining > 0.5f
            ? $"Larguras ajustadas, mas ainda faltam {Math.Ceiling(remaining):0} px para o limite — oculte colunas para caber."
            : "Larguras ajustadas ao limite.");
    }

    /// <summary>"Ajustar larguras" never trims a data column below this share of its default width.</summary>
    private const float FitFloor = 0.8f;

    private float DefaultWidthOf(string key, float fallback)
    {
        var defaults = DefaultColumnsFor(_selectedWidget);
        return defaults?.FirstOrDefault(c => c.Key == key)?.WidthPx ?? fallback;
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
