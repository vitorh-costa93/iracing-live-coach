using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Theme;
using IracingLiveCoach.OverlayHost.Widgets;
using LayoutColumn = IracingLiveCoach.OverlayHost.Layout.ColumnDefinition;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// Layout tab (Phase 5, first real pass -- rewritten 2026-09-18, the prior stub had a
/// RoutedEventArgs-typed handler wired to Slider.ValueChanged, whose real event type is
/// RoutedPropertyChangedEventHandler&lt;double&gt;, and used a different pipe name/message shape
/// than the OverlayHost side actually listens on). Drives <see cref="PlacementIpcClient"/> --
/// every control change here is sent live to the running overlay, spec §3's "aplicação de
/// configurações sem reiniciar a corrida".
///
/// Still open, honestly: this panel does not yet READ BACK the overlay's current placement (no
/// query/response leg on the IPC channel yet, only fire-and-forget updates), so the numbers shown
/// when a widget is selected are this panel's own last-sent values (or its built-in defaults on
/// first launch), not a live poll of the OverlayHost process. None of spec §12's other tabs
/// (Cabeçalhos, Colunas, Aparência, Cores, Regras, Perfis) exist yet -- this is Layout only.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlacementIpcClient _client = new();
    private readonly EditModeIpcClient _editModeClient = new();
    private readonly Dictionary<string, WidgetUiState> _state = BuildDefaults();
    private string _selectedWidget = "standings";
    private bool _suppressChangeEvents;
    private OverlayPreviewHost? _previewHost;
    private bool _editModeUnlocked;

    /// <summary>Backs the Cores/Perfis tabs -- a separate <see cref="WidgetPlacementStore"/> from
    /// the Layout tab's own in-memory <see cref="_state"/> dictionary, because these two tabs work
    /// directly against the on-disk profile (spec §16's palette overrides, spec §12's
    /// import/export/restore) rather than against the live IPC channel Layout uses.</summary>
    private readonly WidgetPlacementStore _profileStore = new();

    /// <summary>Working set for the Colunas tab -- whichever widget is selected in the sidebar.
    /// Reuses OverlayHost's own <see cref="ColumnDefinition"/> type directly instead of a parallel
    /// UI-only shape, since this project already references that assembly for the preview host.</summary>
    private List<LayoutColumn> _currentColumns = [];
    private readonly ColumnConfigIpcClient _columnConfigClient = new();
    private readonly RulesIpcClient _rulesClient = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoControls(_selectedWidget);
        PlacementPersistence.Load(_profileStore);
        RefreshClassColorList();
        LoadColumnsForSelectedWidget();
        LoadRulesIntoControls();
        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => RepositionPreview();
        Closed += (_, _) => { CompositionTarget.Rendering -= OnPreviewRenderTick; _previewHost?.Dispose(); };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // The preview surface is a real top-level Win32 popup (see OverlayPreviewHost's class doc
        // comment for why -- a WS_CHILD HwndHost attempt failed DirectComposition's
        // CreateTargetForHwnd at runtime), owned by this window so it doesn't outlive the panel.
        nint ownerHwnd = new WindowInteropHelper(this).Handle;
        var (x, y, w, h) = PreviewScreenRect();
        _previewHost = new OverlayPreviewHost(ownerHwnd, x, y, w, h);
        CompositionTarget.Rendering += OnPreviewRenderTick;
    }

    private void OnPreviewRenderTick(object? sender, EventArgs e) => _previewHost?.RenderFrame();

    /// <summary>Keeps the preview box at a true 16:9 ratio -- spec §12/§17's "Preview inclui modo
    /// 1920×1080 em escala real" only holds if the box's own aspect ratio matches 1920x1080; a
    /// mismatched box (e.g. a fixed height regardless of width) makes the single X/Y scale factor
    /// OverlayPreviewHost.RenderFrame computes wrong for one axis, causing widgets positioned lower
    /// in the 1920x1080 layout to run past the box's bottom edge.</summary>
    private bool _adjustingAspect;
    private void PreviewBorder_AspectSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_adjustingAspect || e.NewSize.Width <= 0) return;
        double targetHeight = e.NewSize.Width * 9.0 / 16.0;
        if (Math.Abs(targetHeight - e.NewSize.Height) > 0.5)
        {
            _adjustingAspect = true;
            PreviewBorder.Height = targetHeight;
            _adjustingAspect = false;
        }
        RepositionPreview();
    }

    private void RepositionPreview()
    {
        if (_previewHost is null) return;
        var (x, y, w, h) = PreviewScreenRect();
        _previewHost.MoveTo(x, y, w, h);
    }

    private (int X, int Y, int Width, int Height) PreviewScreenRect()
    {
        var topLeft = PreviewBorder.PointToScreen(new Point(0, 0));
        return ((int)topLeft.X, (int)topLeft.Y, (int)Math.Max(1, PreviewBorder.ActualWidth), (int)Math.Max(1, PreviewBorder.ActualHeight));
    }

    /// <summary>Starting values mirror OverlayHost's own <c>Program.cs</c> initial
    /// <c>WidgetPlacementStore.Set</c> calls -- necessarily duplicated (separate processes, per
    /// spec §3's decoupled-process architecture), not read from the running host yet.</summary>
    private static Dictionary<string, WidgetUiState> BuildDefaults() => new()
    {
        ["standings"] = new WidgetUiState(200, 200, 820, 260),
        ["relative"] = new WidgetUiState(200, 470, 760, 200),
        ["weather"] = new WidgetUiState(980, 470, 280, 132),
        ["fuel"] = new WidgetUiState(980, 610, 300, 104),
        ["radar"] = new WidgetUiState(980, 722, 180, 130),
        ["start-helper"] = new WidgetUiState(980, 860, 280, 82),
    };

    private void SelectWidget(object sender, RoutedEventArgs e)
    {
        _selectedWidget = (string)((Button)sender).Tag;
        WidgetTitle.Text = _selectedWidget.ToUpperInvariant().Replace('-', ' ');
        LoadIntoControls(_selectedWidget);
        LoadColumnsForSelectedWidget();
    }

    private void LoadIntoControls(string widget)
    {
        _suppressChangeEvents = true;
        try
        {
            var state = _state[widget];
            XBox.Text = state.X.ToString(CultureInfo.InvariantCulture);
            YBox.Text = state.Y.ToString(CultureInfo.InvariantCulture);
            WidthBox.Text = state.Width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = state.Height.ToString(CultureInfo.InvariantCulture);
            VisibleBox.IsChecked = state.Visible;
            LockedBox.IsChecked = state.Locked;
            OpacitySlider.Value = state.Opacity;
            ScaleSlider.Value = state.Scale;
        }
        finally { _suppressChangeEvents = false; }
    }

    private void PositionChanged(object sender, TextChangedEventArgs e) => ApplyControlsToStateAndSend();
    private void ToggleChanged(object sender, RoutedEventArgs e) => ApplyControlsToStateAndSend();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyControlsToStateAndSend();

    private void ApplyControlsToStateAndSend()
    {
        if (_suppressChangeEvents) return;
        if (!float.TryParse(XBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return;
        if (!float.TryParse(YBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return;
        if (!float.TryParse(WidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) return;
        if (!float.TryParse(HeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return;

        var state = new WidgetUiState(x, y, width, height,
            VisibleBox.IsChecked == true, LockedBox.IsChecked == true,
            (float)OpacitySlider.Value, (float)ScaleSlider.Value);
        _state[_selectedWidget] = state;

        _ = SendAsync(state);
    }

    private async Task SendAsync(WidgetUiState state)
    {
        bool sent = await _client.SendAsync(_selectedWidget, state);
        Dispatcher.Invoke(() => ConnectionStatus.Text = sent
            ? "Connected — overlay updated live."
            : "Overlay not running or unreachable — changes will apply once it starts.");
    }

    // --- Cores tab (spec §16: "Permita personalização e restauração por token, paleta de classe") ---

    private void RefreshClassColorList()
    {
        var panel = new StackPanel();
        foreach (var (className, hex) in _profileStore.ClassColorOverrides)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new Border
            {
                Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)new BrushConverter().ConvertFromString(hex)!,
                BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1)
            });
            row.Children.Add(new TextBlock { Text = $"{className}  {hex}", Width = 140, VerticalAlignment = VerticalAlignment.Center });
            var removeButton = new Button { Content = "Remover", Tag = className };
            removeButton.Click += RemoveClassColorOverride;
            row.Children.Add(removeButton);
            panel.Children.Add(row);
        }
        ClassColorList.Items.Clear();
        ClassColorList.Items.Add(panel);
    }

    private void AddClassColorOverride(object sender, RoutedEventArgs e)
    {
        string className = NewClassNameBox.Text.Trim();
        string hex = NewClassColorBox.Text.Trim();
        if (className.Length == 0)
        {
            ColorStatus.Text = "Informe o nome curto da classe (ex: GT3).";
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$"))
        {
            ColorStatus.Text = "Cor inválida -- use o formato #RRGGBB.";
            return;
        }
        _profileStore.ClassColorOverrides[className] = hex;
        PlacementPersistence.Save(_profileStore);
        // Apply immediately to THIS process's PaletteTokens -- Save() only persists to disk, it
        // doesn't touch live in-memory state, so without this the embedded preview (which shares
        // this same static PaletteTokens class) wouldn't show the change until relaunched.
        var mediaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        PaletteTokens.SetNameOverride(className, new Vortice.Win32.Numerics.Color4(
            mediaColor.R / 255f, mediaColor.G / 255f, mediaColor.B / 255f, 1f));
        RefreshClassColorList();
        ColorStatus.Text = "Salvo e aplicado neste preview imediatamente. O OverlayHost.exe em execução real só aplica no próximo carregamento do perfil (reinicie-o para ver na corrida).";
    }

    private void RemoveClassColorOverride(object sender, RoutedEventArgs e)
    {
        string className = (string)((Button)sender).Tag;
        _profileStore.ClassColorOverrides.Remove(className);
        PlacementPersistence.Save(_profileStore);
        PaletteTokens.ClearNameOverride(className);
        RefreshClassColorList();
        ColorStatus.Text = $"Override de '{className}' removido.";
    }

    // --- Perfis tab (spec §12: "duplicar, renomear, importar/exportar, desfazer, restaurar") ---

    private void ExportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Perfil Live Coach V3 (*.json)|*.json", FileName = "v3-layout-export.json" };
        if (dialog.ShowDialog() != true) return;
        bool ok = PlacementPersistence.TryExport(dialog.FileName);
        ProfileStatus.Text = ok ? $"Exportado para {dialog.FileName}" : "Falha ao exportar -- nenhum perfil salvo ainda?";
    }

    private void ImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Perfil Live Coach V3 (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        bool ok = PlacementPersistence.TryImport(_profileStore, dialog.FileName);
        if (ok)
        {
            RefreshClassColorList();
            ProfileStatus.Text = "Importado. Reinicie o OverlayHost.exe para aplicar ao overlay ao vivo.";
        }
        else
        {
            ProfileStatus.Text = "Falha ao importar -- arquivo inválido ou versão de esquema incompatível.";
        }
    }

    private void RestoreDefaults(object sender, RoutedEventArgs e)
    {
        _profileStore.ClassColorOverrides.Clear();
        PlacementPersistence.Save(_profileStore);
        PaletteTokens.ClearAllNameOverrides();
        RefreshClassColorList();
        ProfileStatus.Text = "Overrides de cor restaurados para o padrão normativo (spec §16).";
    }

    // --- Colunas tab (spec §12: reorder/width/decimals/alignment/visibility, ao vivo) ---

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
            ColumnsHint.Text = $"Colunas configuráveis ainda não disponíveis para '{_selectedWidget}' -- apenas Standings e Relative têm o motor de colunas ligado.";
            RefreshColumnList();
            return;
        }
        ColumnsHint.Text = "Reordenar (↑/↓), largura, casas decimais, alinhamento e visibilidade -- aplicado ao vivo.";
        _currentColumns = _profileStore.ColumnOverrides.TryGetValue(_selectedWidget, out var saved)
            ? saved.OrderBy(c => c.Order).ToList()
            : defaults;
        RefreshColumnList();
    }

    private void RefreshColumnList()
    {
        var panel = new StackPanel();
        var ordered = _currentColumns.OrderBy(c => c.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var column = ordered[i];
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            for (int c = 0; c < 7; c++) row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());

            var upDown = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            var upButton = new Button { Content = "↑", Padding = new Thickness(4, 0, 4, 0), IsEnabled = index > 0 };
            upButton.Click += (_, _) => MoveColumn(column.Key, -1);
            var downButton = new Button { Content = "↓", Padding = new Thickness(4, 0, 4, 0), IsEnabled = index < ordered.Count - 1 };
            downButton.Click += (_, _) => MoveColumn(column.Key, 1);
            upDown.Children.Add(upButton);
            upDown.Children.Add(downButton);
            Grid.SetColumn(upDown, 0);
            row.Children.Add(upDown);

            var visibleBox = new CheckBox { IsChecked = column.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            visibleBox.Checked += (_, _) => UpdateColumn(column.Key, c => c with { Visible = true });
            visibleBox.Unchecked += (_, _) => UpdateColumn(column.Key, c => c with { Visible = false });
            Grid.SetColumn(visibleBox, 1);
            row.Children.Add(visibleBox);

            var keyLabel = new TextBlock { Text = column.Key, VerticalAlignment = VerticalAlignment.Center, Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(keyLabel, 2);
            row.Children.Add(keyLabel);

            var widthBox = new TextBox
            {
                Text = column.WidthPx.ToString("0", CultureInfo.InvariantCulture), Width = 50,
                Background = (Brush)new BrushConverter().ConvertFromString("#17232E")!, Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#405A6B")!, Margin = new Thickness(6, 0, 0, 0)
            };
            widthBox.LostFocus += (_, _) =>
            {
                if (float.TryParse(widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                    UpdateColumn(column.Key, c => c with { WidthPx = w, MinWidthPx = Math.Min(c.MinWidthPx, w) });
            };
            Grid.SetColumn(widthBox, 3);
            row.Children.Add(widthBox);

            var decimalsBox = new TextBox
            {
                Text = column.DecimalPlaces?.ToString(CultureInfo.InvariantCulture) ?? "", Width = 30,
                Background = (Brush)new BrushConverter().ConvertFromString("#17232E")!, Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#405A6B")!, Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = column.DecimalPlaces is not null
            };
            decimalsBox.LostFocus += (_, _) =>
            {
                if (int.TryParse(decimalsBox.Text, out var d))
                    UpdateColumn(column.Key, c => c with { DecimalPlaces = Math.Clamp(d, 0, 6) });
            };
            Grid.SetColumn(decimalsBox, 4);
            row.Children.Add(decimalsBox);

            var alignBox = new ComboBox { Width = 70, Margin = new Thickness(6, 0, 0, 0) };
            alignBox.Items.Add(ColumnAlignment.Left);
            alignBox.Items.Add(ColumnAlignment.Center);
            alignBox.Items.Add(ColumnAlignment.Right);
            alignBox.SelectedItem = column.Alignment;
            alignBox.SelectionChanged += (_, _) =>
            {
                if (alignBox.SelectedItem is ColumnAlignment a) UpdateColumn(column.Key, c => c with { Alignment = a });
            };
            Grid.SetColumn(alignBox, 5);
            row.Children.Add(alignBox);

            panel.Children.Add(row);
        }
        ColumnList.Items.Clear();
        ColumnList.Items.Add(panel);
    }

    private void UpdateColumn(string key, Func<LayoutColumn, LayoutColumn> update)
    {
        int idx = _currentColumns.FindIndex(c => c.Key == key);
        if (idx < 0) return;
        _currentColumns[idx] = update(_currentColumns[idx]);
    }

    private void MoveColumn(string key, int direction)
    {
        var ordered = _currentColumns.OrderBy(c => c.Order).ToList();
        int idx = ordered.FindIndex(c => c.Key == key);
        int target = idx + direction;
        if (idx < 0 || target < 0 || target >= ordered.Count) return;
        (ordered[idx], ordered[target]) = (ordered[target], ordered[idx]);
        for (int i = 0; i < ordered.Count; i++)
        {
            int keyIdx = _currentColumns.FindIndex(c => c.Key == ordered[i].Key);
            _currentColumns[keyIdx] = _currentColumns[keyIdx] with { Order = i };
        }
        RefreshColumnList();
    }

    private async void ApplyColumnConfig(object sender, RoutedEventArgs e)
    {
        if (_currentColumns.Count == 0)
        {
            ColumnsStatus.Text = "Nada para aplicar.";
            return;
        }
        var entries = _currentColumns.Select(c => new ColumnConfigEntry(
            c.Key, c.Visible, c.Order, c.WidthPx, c.MinWidthPx, c.WidthMode.ToString(),
            c.Alignment.ToString(), c.DecimalPlaces, c.PaddingLeftPx, c.PaddingRightPx)).ToList();

        bool sent = await _columnConfigClient.SendAsync(_selectedWidget, entries);
        _profileStore.ColumnOverrides[_selectedWidget] = _currentColumns;
        PlacementPersistence.Save(_profileStore);
        ColumnsStatus.Text = sent
            ? "Aplicado ao overlay ao vivo e salvo."
            : "Salvo -- overlay não está rodando ou inacessível agora, mas será aplicado no próximo carregamento do perfil.";
    }

    private void RestoreDefaultColumns(object sender, RoutedEventArgs e)
    {
        var defaults = DefaultColumnsFor(_selectedWidget);
        if (defaults is null) return;
        _currentColumns = defaults;
        _profileStore.ColumnOverrides.Remove(_selectedWidget);
        PlacementPersistence.Save(_profileStore);
        RefreshColumnList();
        ColumnsStatus.Text = "Restaurado para o padrão -- clique Aplicar para enviar ao overlay ao vivo.";
    }

    // --- Regras tab (spec §6/§12: Top N, linhas por classe, janela do jogador) ---

    private void LoadRulesIntoControls()
    {
        var rules = _profileStore.StandingsRules ?? IracingLiveCoach.Core.Telemetry.StandingsPresentationOptions.Default;
        TopNBox.Text = rules.TopNPerClass.ToString(CultureInfo.InvariantCulture);
        OwnClassRowsBox.Text = rules.OwnClassRows.ToString(CultureInfo.InvariantCulture);
        OtherClassRowsBox.Text = rules.OtherClassRows.ToString(CultureInfo.InvariantCulture);
        KeepPlayerWindowBox.IsChecked = rules.KeepPlayerWindow;
    }

    private async void ApplyRules(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TopNBox.Text, out var topN) || !int.TryParse(OwnClassRowsBox.Text, out var ownRows) || !int.TryParse(OtherClassRowsBox.Text, out var otherRows))
        {
            RulesStatus.Text = "Valores inválidos -- use números inteiros.";
            return;
        }
        bool keepWindow = KeepPlayerWindowBox.IsChecked == true;
        bool sent = await _rulesClient.SendAsync(topN, ownRows, otherRows, keepWindow);
        _profileStore.StandingsRules = new IracingLiveCoach.Core.Telemetry.StandingsPresentationOptions(topN, ownRows, otherRows, keepWindow);
        PlacementPersistence.Save(_profileStore);
        RulesStatus.Text = sent
            ? "Aplicado ao overlay ao vivo e salvo."
            : "Salvo -- overlay não está rodando ou inacessível agora, mas será aplicado no próximo carregamento do perfil.";
    }

    // --- Modo de edição global (spec §4: overlays só visíveis na pista, exceto durante edição) ---

    private async void ToggleEditMode(object sender, RoutedEventArgs e)
    {
        _editModeUnlocked = !_editModeUnlocked;
        bool sent = await _editModeClient.SendAsync(_editModeUnlocked);
        EditModeButton.Content = _editModeUnlocked ? "Travar (mostrar só na pista)" : "Destravar para editar";
        EditModeStatus.Text = sent
            ? (_editModeUnlocked ? "Destravado -- overlays visíveis agora, em qualquer tela." : "Travado -- overlays só aparecem quando você estiver na pista.")
            : "Overlay não está rodando ou inacessível -- nada foi aplicado.";
    }
}

/// <param name="X">Virtual-desktop DIPs, top-left anchored (spec §4).</param>
public readonly record struct WidgetUiState(
    float X, float Y, float Width, float Height,
    bool Visible = true, bool Locked = false, float Opacity = 1f, float Scale = 1f);
