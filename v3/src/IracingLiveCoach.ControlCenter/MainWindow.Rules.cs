using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Theme;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Right-hand cards and the Classes / Combustível / Regras tabs: row rules, formats, class
/// colours, fuel, session visibility and class/car profiles. All live-applied.</summary>
public partial class MainWindow
{
    private static readonly string[] SessionKindNames = ["Practice", "Qualify", "Race"];
    private static readonly string[] SessionKindLabels = ["Treino", "Classificação", "Corrida"];
    private readonly Dictionary<(string Widget, string Kind), CheckBox> _sessionBoxes = new();

    private static readonly string[] DefaultRankColors = ["#FFD400", "#5CC8FF", "#FF6EB4", "#3DDC84"];
    private static readonly string[] RankLabels = ["Classe mais rápida", "2ª mais rápida", "3ª mais rápida", "4ª mais rápida"];
    private static readonly string[] PresetColors =
    [
        "#FF3038", "#FF8A3D", "#FFD400", "#3DDC84", "#2DD4BF", "#5CC8FF", "#5B8CFF", "#A78BFA",
        "#FF6EB4", "#F472B6", "#FFFFFF", "#B8C4D0", "#73808C", "#00E600", "#FBCD08", "#9AD3FE",
    ];

    // --- Linhas e classificação ---

    private void LoadRulesIntoControls()
    {
        var rules = _profileStore.StandingsRules ?? StandingsPresentationOptions.Default;
        var relative = _profileStore.RelativeRules ?? RelativeRules.Default;
        TopNBox.Text = rules.TopNPerClass.ToString(CultureInfo.InvariantCulture);
        OwnClassRowsBox.Text = rules.OwnClassRows.ToString(CultureInfo.InvariantCulture);
        OtherClassRowsBox.Text = rules.OtherClassRows.ToString(CultureInfo.InvariantCulture);
        KeepPlayerWindowBox.IsChecked = rules.KeepPlayerWindow;
        TopNCountsBox.IsChecked = rules.TopNCountsTowardTotal;
        RelAheadBox.Text = relative.Ahead.ToString(CultureInfo.InvariantCulture);
        RelBehindBox.Text = relative.Behind.ToString(CultureInfo.InvariantCulture);
    }

    private void RulesChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("rules", ApplyRules, 120);
    }

    private void RulesTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("rules", ApplyRules, 450);
    }

    private async void ApplyRules()
    {
        if (!int.TryParse(TopNBox.Text, out var topN) || !int.TryParse(OwnClassRowsBox.Text, out var own) ||
            !int.TryParse(OtherClassRowsBox.Text, out var other) || !int.TryParse(RelAheadBox.Text, out var ahead) ||
            !int.TryParse(RelBehindBox.Text, out var behind))
        {
            RulesStatus.Text = "Use números inteiros nas linhas.";
            return;
        }
        RulesStatus.Text = "";
        topN = Math.Clamp(topN, 0, 10); own = Math.Clamp(own, 0, 30); other = Math.Clamp(other, 0, 10);
        ahead = Math.Clamp(ahead, 0, RelativeRules.Max); behind = Math.Clamp(behind, 0, RelativeRules.Max);
        bool keepWindow = KeepPlayerWindowBox.IsChecked == true;
        bool topNCounts = TopNCountsBox.IsChecked == true;
        _profileStore.StandingsRules = new StandingsPresentationOptions(topN, own, other, keepWindow, topNCounts);
        _profileStore.RelativeRules = new RelativeRules(ahead, behind);
        _previewHost?.ApplyProfile(_profileStore);
        bool sent = await _rulesClient.SendAsync(topN, own, other, keepWindow, ahead, behind, topNCounts);
        ReportSent(sent, "Regras de linhas");
        Debounce("save", SaveProfileStore, 600);
    }

    // --- Formatos ---

    private void LoadNumberFormatIntoControls()
    {
        var config = _profileStore.NumberFormat ?? NumberFormatConfig.Default;
        IRatingFormatBox.SelectedIndex = config.IRating switch { IRatingFormat.Plain => 1, IRatingFormat.Thousands => 2, _ => 0 };
        SafetyRatingFormatBox.SelectedIndex = config.SafetyRating switch { SafetyRatingFormat.NumberOnly => 1, SafetyRatingFormat.LetterOnly => 2, _ => 0 };
        NameFormatBox.SelectedIndex = config.NameFormat switch { NameDisplayFormat.Abbreviated => 1, NameDisplayFormat.FirstLast => 2, _ => 0 };
        IRatingDeltaBox.IsChecked = config.ShowIRatingDelta;
        NameExample.Text = NameExampleTextFor(NameFormatBox.SelectedIndex);
        SyncDecimalsBox();
    }

    private void SyncDecimalsBox()
    {
        var numeric = _currentColumns.Where(c => c.DecimalPlaces is not null).Select(c => c.DecimalPlaces!.Value).Distinct().ToList();
        bool previous = _suppressChangeEvents;
        _suppressChangeEvents = true;
        DecimalsBox.SelectedIndex = numeric.Count == 1 ? Math.Clamp(numeric[0], 0, 3) + 1 : 0; // 0 = Auto
        _suppressChangeEvents = previous;
    }

    private void FormatsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        NameExample.Text = NameExampleTextFor(NameFormatBox.SelectedIndex);
        Debounce("formats", ApplyNumberFormat, 100);
    }

    private static string NameExampleTextFor(int selectedIndex) => selectedIndex switch { 1 => "V. Costa", 2 => "Vitor Costa", _ => "Vitor Hugo Da Costa" };

    private async void ApplyNumberFormat()
    {
        var config = new NumberFormatConfig(
            IRatingFormatBox.SelectedIndex switch { 1 => IRatingFormat.Plain, 2 => IRatingFormat.Thousands, _ => IRatingFormat.Full },
            SafetyRatingFormatBox.SelectedIndex switch { 1 => SafetyRatingFormat.NumberOnly, 2 => SafetyRatingFormat.LetterOnly, _ => SafetyRatingFormat.LetterAndNumber },
            NameFormatBox.SelectedIndex switch { 1 => NameDisplayFormat.Abbreviated, 2 => NameDisplayFormat.FirstLast, _ => NameDisplayFormat.Full },
            IRatingDeltaBox.IsChecked == true);
        _profileStore.NumberFormat = config;
        _previewHost?.ApplyProfile(_profileStore);
        // The table's "Formato" combos mirror these three global formats.
        Dispatcher.BeginInvoke(new Action(RefreshColumnList));
        bool sent = await _numberFormatClient.SendAsync(config.IRating.ToString(), config.SafetyRating.ToString(), config.NameFormat.ToString(), config.ShowIRatingDelta);
        ReportSent(sent, "Formatos");
        Debounce("save", SaveProfileStore, 600);
    }

    /// <summary>"Decimais por campo": Auto keeps each numeric column's own default (3); a number forces it on every numeric column.</summary>
    private void DecimalsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents || _currentColumns.Count == 0) return;
        int decimals = DecimalsBox.SelectedIndex <= 0 ? 3 : DecimalsBox.SelectedIndex - 1;
        for (int i = 0; i < _currentColumns.Count; i++)
            if (_currentColumns[i].DecimalPlaces is not null)
                _currentColumns[i] = _currentColumns[i] with { DecimalPlaces = decimals };
        RefreshColumnList();
        ApplyColumnsNow();
    }

    // --- Combustível ---

    private void LoadFuelConfigIntoControls()
    {
        var config = _profileStore.FuelConfig ?? FuelConfig.Default;
        FuelSourceBox.SelectedIndex = config.Source switch
        {
            FuelConsumptionSource.LastLap => 0,
            FuelConsumptionSource.Average => 1,
            FuelConsumptionSource.Max => 2,
            _ => 3
        };
        FuelManualBox.Text = config.ManualLitersPerLap.ToString(CultureInfo.InvariantCulture);
        FuelReserveBox.Text = config.ReserveLaps.ToString(CultureInfo.InvariantCulture);
        FuelExcludePitBox.IsChecked = config.ExcludePitLaps;
    }

    private void FuelChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("fuel", ApplyFuelConfig, 350);
    }

    private async void ApplyFuelConfig()
    {
        string source = FuelSourceBox.SelectedIndex switch { 0 => "LastLap", 1 => "Average", 2 => "Max", _ => "Manual" };
        if (!double.TryParse(FuelManualBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var manual) ||
            !double.TryParse(FuelReserveBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var reserve))
        {
            FuelRulesStatus.Text = "Valores inválidos.";
            return;
        }
        FuelRulesStatus.Text = "";
        bool excludePit = FuelExcludePitBox.IsChecked == true;
        _profileStore.FuelConfig = new FuelConfig(Enum.Parse<FuelConsumptionSource>(source), manual, reserve, excludePit);
        _previewHost?.ApplyProfile(_profileStore);
        bool sent = await _fuelConfigClient.SendAsync(source, manual, reserve, excludePit);
        ReportSent(sent, "Combustível");
        Debounce("save", SaveProfileStore, 600);
    }

    // --- Cores por classe: by speed rank (right card) + by class name (Classes tab) ---

    private List<string> CurrentRankColors()
    {
        var saved = _profileStore.ClassRankColors;
        return Enumerable.Range(0, DefaultRankColors.Length).Select(i => saved is not null && i < saved.Count ? saved[i] : DefaultRankColors[i]).ToList();
    }

    private void BuildRankColorRows()
    {
        RankColorList.Children.Clear();
        var colors = CurrentRankColors();
        for (int i = 0; i < colors.Count; i++)
        {
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Background = ToBrush(colors[i]), BorderBrush = (Brush)FindResource("FieldBorderBrush"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(swatch);
            var label = new TextBlock { Text = RankLabels[i], VerticalAlignment = VerticalAlignment.Center, FontSize = 15 };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var hex = new TextBox { Text = colors[i], Style = (Style)FindResource("NumField"), Margin = new Thickness(0, 0, 8, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(hex, "RankHex_" + i);
            Grid.SetColumn(hex, 2);
            row.Children.Add(hex);
            var pick = new Button { Content = "", Style = (Style)FindResource("IconButton"), Padding = new Thickness(8, 5, 8, 5), ToolTip = "Escolher cor" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(pick, "RankPick_" + i);
            Grid.SetColumn(pick, 3);
            row.Children.Add(pick);

            hex.TextChanged += (_, _) =>
            {
                if (_suppressChangeEvents || !IsValidHex(hex.Text)) return;
                swatch.Background = ToBrush(hex.Text);
                SetRankColor(index, hex.Text.ToUpperInvariant());
            };
            pick.Click += (_, _) => ShowColorPopup(pick, chosen => { hex.Text = chosen; });
            RankColorList.Children.Add(row);
        }
    }

    private static bool IsValidHex(string text) => System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^#[0-9A-Fa-f]{6}$");

    private static Brush ToBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void SetRankColor(int index, string hex)
    {
        var colors = CurrentRankColors();
        colors[index] = hex;
        _profileStore.ClassRankColors = colors;
        Debounce("classcolors", ApplyClassColorsNow, 200);
    }

    private async void ApplyClassColorsNow()
    {
        var rank = CurrentRankColors();
        PaletteTokens.SetRankColors(rank);
        _previewHost?.ApplyProfile(_profileStore);
        bool sent = await _classColorsClient.SendAsync(rank, new Dictionary<string, string>(_profileStore.ClassColorOverrides));
        ReportSent(sent, "Cores por classe");
        Debounce("save", SaveProfileStore, 600);
    }

    private void ShowColorPopup(UIElement anchor, Action<string> chosen)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var wrap = new WrapPanel { Width = 196 };
        foreach (var color in PresetColors)
        {
            var chip = new Border
            {
                Width = 20, Height = 20, Margin = new Thickness(3), CornerRadius = new CornerRadius(4), Background = ToBrush(color),
                BorderBrush = (Brush)FindResource("FieldBorderBrush"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = color
            };
            string captured = color;
            chip.MouseLeftButtonUp += (_, _) => { popup.IsOpen = false; chosen(captured); };
            wrap.Children.Add(chip);
        }
        popup.Child = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#061B29")!, BorderBrush = (Brush)FindResource("CyanDeepBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(6), Child = wrap
        };
        popup.IsOpen = true;
    }

    private void RefreshClassColorList()
    {
        var panel = new StackPanel();
        foreach (var (className, hex) in _profileStore.ClassColorOverrides)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(4),
                Background = IsValidHex(hex) ? ToBrush(hex) : Brushes.Gray, BorderBrush = (Brush)FindResource("FieldBorderBrush"), BorderThickness = new Thickness(1)
            });
            row.Children.Add(new TextBlock { Text = $"{className}   {hex}", Width = 190, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remover", Tag = className, Padding = new Thickness(10, 3, 10, 3) };
            remove.Click += RemoveClassColorOverride;
            row.Children.Add(remove);
            panel.Children.Add(row);
        }
        if (_profileStore.ClassColorOverrides.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Nenhuma cor por nome — as classes usam a cor por ranking.", Style = (Style)FindResource("Muted") });
        ClassColorList.Items.Clear();
        ClassColorList.Items.Add(panel);
    }

    private void AddClassColorOverride(object sender, RoutedEventArgs e)
    {
        string className = NewClassNameBox.Text.Trim();
        string hex = NewClassColorBox.Text.Trim().ToUpperInvariant();
        if (className.Length == 0) { ColorStatus.Text = "Informe o nome curto da classe (ex.: GT3)."; return; }
        if (!IsValidHex(hex)) { ColorStatus.Text = "Cor inválida — use o formato #RRGGBB."; return; }
        _profileStore.ClassColorOverrides[className] = hex;
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        PaletteTokens.SetNameOverride(className, new Vortice.Win32.Numerics.Color4(color.R / 255f, color.G / 255f, color.B / 255f, 1f));
        RefreshClassColorList();
        ColorStatus.Text = $"Cor de “{className}” aplicada.";
        ApplyClassColorsNow();
    }

    private void RemoveClassColorOverride(object sender, RoutedEventArgs e)
    {
        string className = (string)((Button)sender).Tag;
        _profileStore.ClassColorOverrides.Remove(className);
        PaletteTokens.ClearNameOverride(className);
        RefreshClassColorList();
        ColorStatus.Text = $"Cor de “{className}” removida.";
        ApplyClassColorsNow();
    }

    // --- Visibilidade por tipo de sessão (Regras tab) ---

    private void BuildSessionVisibilityGrid()
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(190) });
        for (int c = 0; c < SessionKindNames.Length; c++) grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(130) });
        grid.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < SessionKindNames.Length; c++)
        {
            var head = new TextBlock { Text = SessionKindLabels[c], Style = (Style)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetColumn(head, c + 1);
            grid.Children.Add(head);
        }
        for (int r = 0; r < AllWidgetKeys.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock { Text = WidgetInfo[AllWidgetKeys[r]].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(label, r + 1);
            grid.Children.Add(label);
            for (int c = 0; c < SessionKindNames.Length; c++)
            {
                var box = new CheckBox { Style = (Style)FindResource("Switch"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
                box.Checked += (_, _) => SessionVisibilityChanged();
                box.Unchecked += (_, _) => SessionVisibilityChanged();
                System.Windows.Automation.AutomationProperties.SetAutomationId(box, "Sess_" + AllWidgetKeys[r] + "_" + SessionKindNames[c]);
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
                _sessionBoxes[(AllWidgetKeys[r], SessionKindNames[c])] = box;
            }
        }
        SessionVisibilityList.ItemsSource = new[] { grid };
    }

    private void RefreshSessionVisibilityGrid()
    {
        var config = _profileStore.SessionVisibility ?? SessionVisibilityConfig.Default;
        foreach (var ((widget, kind), box) in _sessionBoxes)
        {
            bool hidden = config.HiddenIn.TryGetValue(widget, out var list) && list.Contains(kind);
            box.IsChecked = !hidden;
        }
    }

    private void SessionVisibilityChanged()
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("sessionvis", ApplySessionVisibility, 200);
    }

    private async void ApplySessionVisibility()
    {
        var hiddenIn = new Dictionary<string, List<string>>();
        foreach (var widget in AllWidgetKeys)
        {
            var hidden = SessionKindNames.Where(k => _sessionBoxes[(widget, k)].IsChecked != true).ToList();
            if (hidden.Count > 0) hiddenIn[widget] = hidden;
        }
        bool sent = await _profilesClient.SendAsync("setSessionVisibility", "", hiddenIn);
        _profileStore.SessionVisibility = new SessionVisibilityConfig(hiddenIn);
        ReportSent(sent, "Visibilidade por sessão");
        Debounce("save", SaveProfileStore, 600);
    }

    // --- Perfis por classe / carro ---

    private void RefreshClassProfileList()
    {
        var panel = new StackPanel();
        foreach (var key in _profileStore.ClassProfiles.Keys.OrderBy(k => k))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = key, Width = 230, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remover", Padding = new Thickness(10, 3, 10, 3) };
            string captured = key;
            remove.Click += async (_, _) =>
            {
                bool sent = await _profilesClient.SendAsync("deleteClassProfile", captured, null);
                _profileStore.ClassProfiles.Remove(captured);
                SaveProfileStore();
                RefreshClassProfileList();
                RefreshProfileBoxes();
                ClassProfileStatus.Text = sent ? $"Perfil “{captured}” removido." : $"Perfil “{captured}” removido do arquivo — overlay fechado.";
            };
            row.Children.Add(remove);
            panel.Children.Add(row);
        }
        if (_profileStore.ClassProfiles.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Nenhum perfil de classe/carro salvo.", Style = (Style)FindResource("Muted") });
        ClassProfileList.ItemsSource = new[] { panel };
    }

    private async void SaveClassProfile(object sender, RoutedEventArgs e)
    {
        string key = NewProfileKeyBox.Text.Trim();
        if (key.Length == 0) { ClassProfileStatus.Text = "Informe o nome da classe ou do carro."; return; }
        bool sent = await _profilesClient.SendAsync("saveClassProfile", key, null);
        if (!sent)
        {
            ClassProfileStatus.Text = "O overlay precisa estar aberto para salvar o layout atual como perfil (é ele quem conhece o layout ao vivo).";
            return;
        }
        await Task.Delay(300);
        var disk = new WidgetPlacementStore();
        PlacementPersistence.Load(disk);
        foreach (var (k, placements) in disk.ClassProfiles) _profileStore.ClassProfiles[k] = placements;
        RefreshClassProfileList();
        RefreshProfileBoxes();
        ClassProfileStatus.Text = $"Layout atual salvo como perfil “{key}”.";
    }
}
