using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Widgets;

using LayoutColumn = IracingLiveCoach.OverlayHost.Layout.ColumnDefinition;

namespace IracingLiveCoach.ControlCenter;

/// <summary>"Colunas" tab (table with drag-to-reorder, per-column visibility / width / format /
/// alignment), its Tipografia panel, and the "Cabeçalhos" tab. Everything applies live.</summary>
public partial class MainWindow
{
    private List<LayoutColumn> _currentColumns = [];
    private List<HeaderFieldConfig> _currentHeader = [];
    private const float BaseFontPx = 16f;
    private const float DefaultRowHeightPx = 32f;
    private static readonly int[] FontSizes = [12, 13, 14, 15, 16, 17, 18, 20, 22];

    private static readonly Dictionary<string, string> ColumnNames = new()
    {
        ["position"] = "Posição", ["offset"] = "Offset", ["carNumber"] = "Número", ["brand"] = "Emblema", ["flag"] = "Bandeira",
        ["name"] = "Piloto", ["license"] = "Carteira (SR)", ["iratingDelta"] = "iRating + Δ", ["irating"] = "iRating",
        ["interval"] = "Interval", ["lastLap"] = "Última volta", ["lapDelta"] = "Δ volta", ["pit"] = "Pit",
        ["gap"] = "Gap", ["overtake"] = "Overtake (P2P)",
    };

    private static List<LayoutColumn>? DefaultColumnsFor(string widget) => widget switch
    {
        "standings" => StandingsWidget.BuildDefaultColumns(),
        "relative" => RelativeWidget.BuildDefaultColumns(),
        _ => null
    };

    private void LoadColumnsForSelectedWidget()
    {
        var defaults = DefaultColumnsFor(_selectedWidget);
        if (defaults is null)
        {
            _currentColumns = [];
            ColumnsStatus.Text = "Este widget não tem tabela de colunas — só Standings e Relative.";
            GapLeaderBox.IsEnabled = false;
            RefreshColumnList();
            return;
        }
        ColumnsStatus.Text = "";
        var saved = _profileStore.ColumnOverrides.TryGetValue(_selectedWidget, out var s) ? s.OrderBy(c => c.Order).ToList() : null;
        if (saved is null) _currentColumns = defaults;
        else
        {
            // A profile saved before a column existed keeps working: missing columns join at the end, hidden as saved-default.
            foreach (var column in defaults.Where(d => saved.All(c => c.Key != d.Key)))
                saved.Add(column with { Order = saved.Count });
            _currentColumns = saved.Where(c => defaults.Any(d => d.Key == c.Key)).ToList();
        }
        GapLeaderBox.IsEnabled = _selectedWidget == "standings";
        Quiet(() => GapLeaderBox.IsChecked = _selectedWidget == "standings" && _currentColumns.Any(c => c.Key == "gap" && c.Visible));
        RefreshColumnList();
    }

    private (string[] Options, int Selected, Action<int>? OnChange) FormatFor(LayoutColumn column)
    {
        switch (column.Key)
        {
            case "name":
                return (["Nome Sobrenome", "N. Sobrenome"], NameFormatBox.SelectedIndex, i => NameFormatBox.SelectedIndex = i);
            case "license":
                return (["A 3.49", "3.49", "A"], SafetyRatingFormatBox.SelectedIndex, i => SafetyRatingFormatBox.SelectedIndex = i);
            case "iratingDelta":
            case "irating":
                return (["3.694", "3.7k"], IRatingFormatBox.SelectedIndex, i => IRatingFormatBox.SelectedIndex = i);
            case "interval": case "gap": case "lapDelta":
                return (["+0.000", "+0.00", "+0.0", "+0"], 3 - Math.Clamp(column.DecimalPlaces ?? 3, 0, 3), i => SetDecimals(column.Key, 3 - i));
            case "lastLap":
                return (["m:ss.000", "m:ss.00", "m:ss.0", "m:ss"], 3 - Math.Clamp(column.DecimalPlaces ?? 3, 0, 3), i => SetDecimals(column.Key, 3 - i));
            case "position": return (["Inteiro (1, 2, 3…)"], 0, null);
            case "carNumber": return (["#00"], 0, null);
            case "brand": case "flag": return (["Ícone"], 0, null);
            case "pit": return (["L8 / 24.0s"], 0, null);
            case "overtake": return (["Segundos + barra"], 0, null);
            default: return ([""], 0, null);
        }
    }

    private void SetDecimals(string key, int decimals) => UpdateColumn(key, c => c with { DecimalPlaces = Math.Clamp(decimals, 0, 3) });

    private void RefreshColumnList()
    {
        var panel = new StackPanel();
        var ordered = _currentColumns.OrderBy(c => c.Order).ToList();
        ColumnHeaderRow.Visibility = ordered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RestoreColumnsButton.Visibility = ordered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < ordered.Count; i++)
        {
            var column = ordered[i];
            string key = column.Key;
            var row = new Border
            {
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#0F3346")!, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 3, 0, 3), AllowDrop = true, Background = Brushes.Transparent, Tag = key
            };
            var grid = new Grid();
            foreach (double width in new[] { 30d, 58d, 50d, 0d, 72d, 140d, 108d })
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });

            var handle = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15, Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = Center(), Cursor = Cursors.SizeAll, ToolTip = "Arraste para reordenar",
                Background = Brushes.Transparent, Padding = new Thickness(7, 8, 7, 8)
            };
            Point? dragStart = null;
            handle.MouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(null);
            handle.MouseLeftButtonUp += (_, _) => dragStart = null;
            handle.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragStart is not { } start) return;
                var now = e.GetPosition(null);
                if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
                dragStart = null;
                DragDrop.DoDragDrop(handle, key, DragDropEffects.Move);
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(handle, "ColHandle_" + key);
            grid.Children.Add(handle);

            var visible = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = column.Visible, HorizontalAlignment = Center(), VerticalAlignment = VerticalAlignment.Center };
            visible.Checked += (_, _) => { if (!_suppressChangeEvents) UpdateColumn(key, c => c with { Visible = true }); SyncGapBox(); };
            visible.Unchecked += (_, _) => { if (!_suppressChangeEvents) UpdateColumn(key, c => c with { Visible = false }); SyncGapBox(); };
            System.Windows.Automation.AutomationProperties.SetAutomationId(visible, "ColVisible_" + key);
            Grid.SetColumn(visible, 1);
            grid.Children.Add(visible);

            var order = new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), HorizontalAlignment = Center(), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            Grid.SetColumn(order, 2);
            grid.Children.Add(order);

            var name = new TextBlock { Text = ColumnNames.GetValueOrDefault(key, key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 16, Opacity = column.Visible ? 1 : 0.55 };
            Grid.SetColumn(name, 3);
            grid.Children.Add(name);

            var widthBox = new TextBox
            {
                Text = column.WidthPx.ToString("0", CultureInfo.InvariantCulture), Style = (Style)FindResource("NumField"),
                HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(4, 4, 4, 4)
            };
            widthBox.TextChanged += (_, _) =>
            {
                if (_buildingColumns) return;
                if (float.TryParse(widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w is >= 8 and <= 600)
                    UpdateColumn(key, c => c with { WidthPx = w, MinWidthPx = Math.Min(c.MinWidthPx, w) });
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(widthBox, "ColWidth_" + key);
            Grid.SetColumn(widthBox, 4);
            grid.Children.Add(widthBox);

            var (options, selected, onChange) = FormatFor(column);
            var formatBox = new ComboBox { FontSize = 14, MinHeight = 32, Margin = new Thickness(8, 0, 0, 0), IsEnabled = onChange is not null };
            foreach (var option in options) formatBox.Items.Add(option);
            formatBox.SelectedIndex = Math.Clamp(selected, 0, options.Length - 1);
            if (onChange is not null)
                formatBox.SelectionChanged += (_, _) =>
                {
                    if (_buildingColumns || formatBox.SelectedIndex < 0) return;
                    onChange(formatBox.SelectedIndex);
                };
            System.Windows.Automation.AutomationProperties.SetAutomationId(formatBox, "ColFormat_" + key);
            Grid.SetColumn(formatBox, 5);
            grid.Children.Add(formatBox);

            var alignBox = new ComboBox { FontSize = 14, MinHeight = 32, Margin = new Thickness(8, 0, 0, 0) };
            alignBox.Items.Add("Esquerda");
            alignBox.Items.Add("Centro");
            alignBox.Items.Add("Direita");
            alignBox.SelectedIndex = column.Alignment switch { ColumnAlignment.Left => 0, ColumnAlignment.Center => 1, _ => 2 };
            alignBox.SelectionChanged += (_, _) =>
            {
                if (_buildingColumns || alignBox.SelectedIndex < 0) return;
                var alignment = alignBox.SelectedIndex switch { 0 => ColumnAlignment.Left, 1 => ColumnAlignment.Center, _ => ColumnAlignment.Right };
                UpdateColumn(key, c => c with { Alignment = alignment });
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(alignBox, "ColAlign_" + key);
            Grid.SetColumn(alignBox, 6);
            grid.Children.Add(alignBox);

            row.Child = grid;
            row.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; row.Background = (Brush)new BrushConverter().ConvertFromString("#0B2A3D")!; };
            row.DragLeave += (_, _) => row.Background = Brushes.Transparent;
            row.Drop += (_, e) =>
            {
                row.Background = Brushes.Transparent;
                if (e.Data.GetData(typeof(string)) is string dragged && dragged != key) ReorderColumn(dragged, key);
            };
            panel.Children.Add(row);
        }
        _buildingColumns = true;
        ColumnList.Items.Clear();
        ColumnList.Items.Add(panel);
        _buildingColumns = false;
    }

    private bool _buildingColumns;

    private static HorizontalAlignment Center() => HorizontalAlignment.Center;

    private void SyncGapBox()
    {
        if (_selectedWidget != "standings" || _suppressChangeEvents) return;
        Quiet(() => GapLeaderBox.IsChecked = _currentColumns.Any(c => c.Key == "gap" && c.Visible));
    }

    private void UpdateColumn(string key, Func<LayoutColumn, LayoutColumn> update)
    {
        int index = _currentColumns.FindIndex(c => c.Key == key);
        if (index < 0) return;
        _currentColumns[index] = update(_currentColumns[index]);
        UpdateWidthWarning();
        Debounce("columns", ApplyColumnsNow, 180);
    }

    private void ReorderColumn(string draggedKey, string targetKey)
    {
        var ordered = _currentColumns.OrderBy(c => c.Order).ToList();
        int from = ordered.FindIndex(c => c.Key == draggedKey);
        int to = ordered.FindIndex(c => c.Key == targetKey);
        if (from < 0 || to < 0) return;
        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, moved);
        for (int i = 0; i < ordered.Count; i++)
        {
            int index = _currentColumns.FindIndex(c => c.Key == ordered[i].Key);
            _currentColumns[index] = _currentColumns[index] with { Order = i };
        }
        RefreshColumnList();
        ApplyColumnsNow();
    }

    /// <summary>Sends the current columns to the overlay, mirrors them into the profile (preview) and schedules the disk write.</summary>
    private async void ApplyColumnsNow()
    {
        if (_currentColumns.Count == 0) return;
        string widget = _selectedWidget;
        var snapshot = _currentColumns.Select(c => c with { }).ToList();
        _profileStore.ColumnOverrides[widget] = snapshot;
        _previewHost?.ApplyProfile(_profileStore);
        var entries = snapshot.Select(c => new ColumnConfigEntry(
            c.Key, c.Visible, c.Order, c.WidthPx, c.MinWidthPx, c.WidthMode.ToString(),
            c.Alignment.ToString(), c.DecimalPlaces, c.PaddingLeftPx, c.PaddingRightPx)).ToList();
        bool sent = await _columnConfigClient.SendAsync(widget, entries);
        ReportSent(sent, "Colunas");
        Debounce("save", SaveProfileStore, 600);
    }

    private void RestoreDefaultColumns(object sender, RoutedEventArgs e)
    {
        var defaults = DefaultColumnsFor(_selectedWidget);
        if (defaults is null) return;
        _currentColumns = defaults;
        _profileStore.ColumnOverrides.Remove(_selectedWidget);
        RefreshColumnList();
        SyncGapBox();
        UpdateWidthWarning();
        ApplyColumnsNow();
        _profileStore.ColumnOverrides.Remove(_selectedWidget);
        ColumnsStatus.Text = "Colunas restauradas para o padrão.";
    }

    private void GapLeaderChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || _selectedWidget != "standings") return;
        bool show = GapLeaderBox.IsChecked == true;
        UpdateColumn("gap", c => c with { Visible = show });
        RefreshColumnList();
    }

    // --- Tipografia (Colunas tab, right panel) + Aparência ---

    private void FillFontSizes()
    {
        FontSizeBox.Items.Clear();
        foreach (int px in FontSizes) FontSizeBox.Items.Add(new ComboBoxItem { Content = $"{px} px" });
    }

    private WidgetAppearance StoredAppearance() =>
        _profileStore.AppearanceOverrides.TryGetValue(_selectedWidget, out var saved) ? saved : WidgetAppearance.Default;

    private float CurrentAppearancePaddingH() => StoredAppearance().PaddingHDip;

    private bool IsTableWidget => _selectedWidget is "standings" or "relative";

    private void LoadAppearanceIntoControls()
    {
        bool previous = _suppressChangeEvents;
        _suppressChangeEvents = true;
        try
        {
            var appearance = StoredAppearance();
            int px = (int)Math.Round(appearance.FontScale * BaseFontPx);
            int best = 0;
            for (int i = 0; i < FontSizes.Length; i++)
                if (Math.Abs(FontSizes[i] - px) < Math.Abs(FontSizes[best] - px)) best = i;
            FontSizeBox.SelectedIndex = best;
            FontWeightBox.SelectedIndex = appearance.FontWeight >= 600 ? 2 : appearance.FontWeight > 0 ? 1 : 0;
            RowHeightBox.IsEnabled = IsTableWidget;
            RowHeightBox.Text = IsTableWidget ? (appearance.RowHeightDip > 0 ? appearance.RowHeightDip : DefaultRowHeightPx).ToString("0", CultureInfo.InvariantCulture) : "";
            PaddingHBox.IsEnabled = IsTableWidget;
            PaddingHBox.Text = IsTableWidget && appearance.PaddingHDip >= 0 ? appearance.PaddingHDip.ToString("0", CultureInfo.InvariantCulture) : "";
            AppearanceStatus.Text = IsTableWidget ? "" : "Altura da linha e padding só valem para Standings e Relative.";
        }
        finally { _suppressChangeEvents = previous; }
    }

    private void AppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("appearance", ApplyAppearance, 120);
    }

    private void AppearanceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("appearance", ApplyAppearance, 400);
    }

    private async void ApplyAppearance()
    {
        string widget = _selectedWidget;
        var current = StoredAppearance();
        int sizeIndex = Math.Clamp(FontSizeBox.SelectedIndex, 0, FontSizes.Length - 1);
        float scale = FontSizes[sizeIndex] / BaseFontPx;
        int weight = FontWeightBox.SelectedIndex switch { 2 => 600, 1 => 400, _ => 0 };
        float rowHeight = current.RowHeightDip;
        float padding = current.PaddingHDip;
        if (IsTableWidget)
        {
            if (float.TryParse(RowHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rh))
                rowHeight = Math.Abs(rh - DefaultRowHeightPx) < 0.5f || rh <= 0 ? 0f : Math.Clamp(rh, 16f, 80f);
            padding = float.TryParse(PaddingHBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ph) ? Math.Clamp(ph, 0f, 40f) : -1f;
        }
        var appearance = new WidgetAppearance(scale, rowHeight, current.RowSpacingDip, weight, padding);
        _profileStore.AppearanceOverrides[widget] = appearance;
        _previewHost?.ApplyProfile(_profileStore);
        UpdateWidthWarning();
        bool sent = await _appearanceClient.SendAsync(widget, appearance.FontScale, appearance.RowHeightDip, appearance.RowSpacingDip, appearance.FontWeight, appearance.PaddingHDip);
        ReportSent(sent, "Tipografia");
        Debounce("save", SaveProfileStore, 600);
    }

    private async void RestoreDefaultAppearance(object sender, RoutedEventArgs e)
    {
        _profileStore.AppearanceOverrides.Remove(_selectedWidget);
        var state = _state[_selectedWidget] with { Opacity = 1f, Scale = 1f };
        _state[_selectedWidget] = state;
        LoadAppearanceIntoControls();
        LoadPlacementIntoControls(_selectedWidget);
        _previewHost?.ApplyProfile(_profileStore);
        UpdateWidthWarning();
        var d = WidgetAppearance.Default;
        bool sent = await _appearanceClient.SendAsync(_selectedWidget, d.FontScale, d.RowHeightDip, d.RowSpacingDip, d.FontWeight, d.PaddingHDip);
        await SendPlacementAsync(_selectedWidget);
        ReportSent(sent, "Aparência restaurada");
        Debounce("save", SaveProfileStore, 600);
    }

    // --- Cabeçalhos ---

    private static readonly Dictionary<string, string> HeaderLabels = new()
    {
        ["type"] = "Tipo de sessão (RACE)", ["class"] = "Classe", ["lap"] = "Volta atual/total",
        ["sof"] = "SOF", ["drivers"] = "Nº de pilotos", ["clock"] = "Relógio", ["bb"] = "Brake bias", ["track"] = "Temperatura da pista",
        ["rubber"] = "Emborrachamento", ["best"] = "Melhor volta", ["last"] = "Última volta", ["local"] = "Hora local",
    };

    private static List<HeaderFieldConfig>? DefaultHeaderFor(string widget) => widget switch
    {
        "standings" => HeaderFields.DefaultStandings(),
        "relative" => HeaderFields.DefaultRelative(),
        _ => null
    };

    private void LoadHeaderForSelectedWidget()
    {
        var defaults = DefaultHeaderFor(_selectedWidget);
        if (defaults is null)
        {
            _currentHeader = [];
            HeaderHint.Text = "Este widget não tem faixa de cabeçalho configurável — só Standings e Relative.";
        }
        else
        {
            _currentHeader = _profileStore.HeaderOverrides.TryGetValue(_selectedWidget, out var saved) ? HeaderFields.Complete(saved) : defaults;
            HeaderHint.Text = "Escolha quais campos aparecem na faixa de cabeçalho e em que ordem (↑/↓). Campos sem dado ainda são omitidos, nunca preenchidos com valor falso.";
        }
        HeaderStatus.Text = "";
        RefreshHeaderList();
    }

    private void RefreshHeaderList()
    {
        var panel = new StackPanel();
        for (int i = 0; i < _currentHeader.Count; i++)
        {
            var field = _currentHeader[i];
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var moves = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var up = new Button { Content = "", Style = (Style)FindResource("RowMoveButton"), IsEnabled = index > 0 };
            up.Click += (_, _) => MoveHeaderField(index, -1);
            var down = new Button { Content = "", Style = (Style)FindResource("RowMoveButton"), IsEnabled = index < _currentHeader.Count - 1 };
            down.Click += (_, _) => MoveHeaderField(index, 1);
            moves.Children.Add(up);
            moves.Children.Add(down);
            row.Children.Add(moves);

            var label = new TextBlock { Text = HeaderLabels.GetValueOrDefault(field.Key, field.Key), VerticalAlignment = VerticalAlignment.Center, FontSize = 16, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = field.Visible, VerticalAlignment = VerticalAlignment.Center };
            toggle.Checked += (_, _) => { _currentHeader[index] = _currentHeader[index] with { Visible = true }; ApplyHeaderNow(); };
            toggle.Unchecked += (_, _) => { _currentHeader[index] = _currentHeader[index] with { Visible = false }; ApplyHeaderNow(); };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "Hdr_" + field.Key);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            panel.Children.Add(row);
        }
        HeaderList.ItemsSource = new[] { panel };
    }

    private void MoveHeaderField(int index, int direction)
    {
        int target = index + direction;
        if (target < 0 || target >= _currentHeader.Count) return;
        (_currentHeader[index], _currentHeader[target]) = (_currentHeader[target], _currentHeader[index]);
        RefreshHeaderList();
        ApplyHeaderNow();
    }

    private async void ApplyHeaderNow()
    {
        if (DefaultHeaderFor(_selectedWidget) is null || _suppressChangeEvents) return;
        string widget = _selectedWidget;
        _profileStore.HeaderOverrides[widget] = _currentHeader.ToList();
        _previewHost?.ApplyProfile(_profileStore);
        bool sent = await _headerClient.SendAsync(widget, _currentHeader.Select(f => new HeaderFieldWire(f.Key, f.Visible)).ToList());
        ReportSent(sent, "Cabeçalho");
        Debounce("save", SaveProfileStore, 600);
    }

    private void RestoreDefaultHeader(object sender, RoutedEventArgs e)
    {
        var defaults = DefaultHeaderFor(_selectedWidget);
        if (defaults is null) return;
        _currentHeader = defaults;
        RefreshHeaderList();
        ApplyHeaderNow();
        _profileStore.HeaderOverrides.Remove(_selectedWidget);
        HeaderStatus.Text = "Cabeçalho restaurado para o padrão.";
    }
}
