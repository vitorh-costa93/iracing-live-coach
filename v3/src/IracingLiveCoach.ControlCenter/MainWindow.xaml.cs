using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Theme;
using LayoutColumn = IracingLiveCoach.OverlayHost.Layout.ColumnDefinition;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// Control Center (Portuguese UI, matching the mockup): top bar (profiles / scope / undo / import /
/// export / save), widget sidebar with per-widget switches, tabbed centre with the live preview, and
/// the right-hand cards (dimensions, rows, formats, class colours, fuel, quick access).
///
/// Every control applies LIVE: it updates the in-memory profile store, sends its typed IPC message to
/// the running overlay, refreshes the preview and (debounced) writes the shared profile file. The
/// overlay owns live placements / class profiles / session visibility, so before every disk write
/// those are refreshed from the file (<see cref="SaveProfileStore"/>).
///
/// Partial classes: this file = shell (preview, sidebar, tabs, top bar, profiles);
/// Placement / Columns / Rules hold the per-area handlers.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlacementIpcClient _client = new();
    private readonly EditModeIpcClient _editModeClient = new();
    private readonly ColumnConfigIpcClient _columnConfigClient = new();
    private readonly RulesIpcClient _rulesClient = new();
    private readonly FuelConfigIpcClient _fuelConfigClient = new();
    private readonly AppearanceIpcClient _appearanceClient = new();
    private readonly UndoRedoIpcClient _undoRedoClient = new();
    private readonly NumberFormatIpcClient _numberFormatClient = new();
    private readonly HeaderConfigIpcClient _headerClient = new();
    private readonly ProfilesIpcClient _profilesClient = new();
    private readonly ClassColorsIpcClient _classColorsClient = new();

    private static readonly string[] AllWidgetKeys = ["standings", "relative", "weather", "fuel", "radar", "start-helper"];

    private static readonly Dictionary<string, (string Label, string Icon)> WidgetInfo = new()
    {
        ["standings"] = ("Standings", ""),
        ["relative"] = ("Relative", ""),
        ["weather"] = ("Weather Report", ""),
        ["fuel"] = ("Fuel Calculator", ""),
        ["radar"] = ("Radar lateral", ""),
        ["start-helper"] = ("Start Helper", ""),
    };

    private readonly Dictionary<string, WidgetUiState> _state = BuildDefaults();
    private readonly Dictionary<string, Border> _sidebarRows = new();
    private readonly Dictionary<string, CheckBox> _sidebarSwitches = new();
    private string _selectedWidget = "standings";
    /// <summary>True while controls are being filled programmatically (and until construction finishes),
    /// so their change events never echo back as user edits.</summary>
    private bool _suppressChangeEvents = true;

    private void Quiet(Action action)
    {
        bool previous = _suppressChangeEvents;
        _suppressChangeEvents = true;
        try { action(); }
        finally { _suppressChangeEvents = previous; }
    }
    private OverlayPreviewHost? _previewHost;
    private bool _editModeUnlocked;

    /// <summary>The on-disk profile as this window sees it (columns, typography, formats, rules, class
    /// colours...). Placements/class profiles/session visibility inside it are refreshed from the file
    /// before every write because the running overlay owns them.</summary>
    private readonly WidgetPlacementStore _profileStore = new();

    public MainWindow()
    {
        InitializeComponent();
        // Never taller/wider than the usable screen area (taskbar excluded).
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 12);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 12);
        PlacementPersistence.Load(_profileStore);
        SeedStateFromStore();
        BuildWidgetSidebar();
        FillFontSizes();
        LoadMonitors();
        BuildSessionVisibilityGrid();
        ReloadAllControlsFromStore();
        RefreshProfileBoxes();
        ShowTab(2);
        _suppressChangeEvents = false;

        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => RepositionPreview();
        StateChanged += (_, _) => RepositionPreview();
        Activated += (_, _) => RefreshLiveStateFromDisk();
        Closed += (_, _) => { CompositionTarget.Rendering -= OnPreviewRenderTick; _previewHost?.Dispose(); };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // The preview surface is a real top-level Win32 popup (a WS_CHILD HwndHost failed
        // DirectComposition's CreateTargetForHwnd), owned by this window so it doesn't outlive it.
        nint ownerHwnd = new WindowInteropHelper(this).Handle;
        var (x, y, w, h) = PreviewScreenRect();
        _previewHost = new OverlayPreviewHost(ownerHwnd, x, y, w, h);
        _previewHost.ApplyProfile(_profileStore);
        CompositionTarget.Rendering += OnPreviewRenderTick;
        RepositionPreview();
    }

    private void OnPreviewRenderTick(object? sender, EventArgs e) => _previewHost?.RenderFrame();

    // --- Preview surface (16:9, tracks its WPF anchor) ---

    private bool _adjustingAspect;
    private void PreviewBorder_AspectSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_adjustingAspect || e.NewSize.Width <= 0) return;
        double targetHeight = Math.Round((e.NewSize.Width - 2) * 9.0 / 16.0) + 2;
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
        bool visible = WindowState != WindowState.Minimized && IsVisible;
        _previewHost.SetVisible(visible);
        if (!visible) return;
        var (x, y, w, h) = PreviewScreenRect();
        _previewHost.MoveTo(x, y, w, h);
    }

    private (int X, int Y, int Width, int Height) PreviewScreenRect()
    {
        // Inside the 1px border.
        var topLeft = PreviewBorder.PointToScreen(new Point(1, 1));
        return ((int)topLeft.X, (int)topLeft.Y, (int)Math.Max(1, PreviewBorder.ActualWidth - 2), (int)Math.Max(1, PreviewBorder.ActualHeight - 2));
    }

    /// <summary>Mirrors OverlayHost's own default placements (separate processes, spec §3). Used only
    /// before a profile file exists and for "restore default position".</summary>
    private static Dictionary<string, WidgetUiState> BuildDefaults() => new()
    {
        ["standings"] = new WidgetUiState(28, 30, 800, 264),
        ["relative"] = new WidgetUiState(1440, 740, 470, 262),
        ["weather"] = new WidgetUiState(1590, 30, 300, 118),
        ["fuel"] = new WidgetUiState(1270, 30, 310, 118),
        ["radar"] = new WidgetUiState(860, 720, 180, 130),
        ["start-helper"] = new WidgetUiState(820, 880, 280, 90),
    };

    private void SeedStateFromStore()
    {
        foreach (var key in AllWidgetKeys)
            if (_profileStore.Get(key) is { } p) _state[key] = ToUiState(p);
    }

    private static WidgetUiState ToUiState(WidgetPlacement p) =>
        new(p.X, p.Y, p.WidthDip, p.HeightDip, p.Visible, p.Locked, p.Opacity, p.Scale, p.ClickThrough, p.AutoSize);

    /// <summary>The overlay owns live placements (in-game drags, auto-sized windows); pull them back
    /// from the profile file whenever this window is re-activated so the fields never show stale values.</summary>
    private void RefreshLiveStateFromDisk()
    {
        try
        {
            var disk = new WidgetPlacementStore();
            PlacementPersistence.Load(disk);
            _profileStore.ReplacePlacements(disk.All);
            SeedStateFromStore();
            foreach (var key in AllWidgetKeys)
                if (_sidebarSwitches.TryGetValue(key, out var sw)) Quiet(() => sw.IsChecked = _state[key].Visible);
            LoadPlacementIntoControls(_selectedWidget);
            _previewHost?.ApplyProfile(_profileStore);
        }
        catch { /* a half-written file or a locked read: keep the current values */ }
    }

    // --- Debounce (live apply without flooding the pipe / the disk) ---

    private readonly Dictionary<string, DispatcherTimer> _debounce = new();

    private void Debounce(string key, Action action, int milliseconds = 220)
    {
        if (_debounce.TryGetValue(key, out var existing)) existing.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); _debounce.Remove(key); action(); };
        _debounce[key] = timer;
        timer.Start();
    }

    private void SetStatus(string text) => ConnectionStatus.Text = text;

    private void ReportSent(bool sent, string what)
        => SetStatus(sent ? $"{what} — aplicado ao overlay." : $"{what} — salvo (overlay fechado; vale no próximo início).");

    /// <summary>The running overlay owns live placements, class profiles and session visibility (it
    /// persists them itself on every change); this window's copies were only loaded earlier. Before
    /// writing the shared profile file, refresh those from disk so a save from here never rolls back
    /// the overlay's newer drags/profiles.</summary>
    private void SaveProfileStore()
    {
        var disk = new WidgetPlacementStore();
        PlacementPersistence.Load(disk);
        _profileStore.ReplacePlacements(disk.All);
        _profileStore.SessionVisibility = disk.SessionVisibility ?? _profileStore.SessionVisibility;
        _profileStore.ClassProfiles.Clear();
        foreach (var (key, placements) in disk.ClassProfiles) _profileStore.ClassProfiles[key] = placements;
        PlacementPersistence.Save(_profileStore);
        _previewHost?.ApplyProfile(_profileStore);
    }

    // --- Widget sidebar ---

    private void BuildWidgetSidebar()
    {
        WidgetRows.Children.Clear();
        _sidebarRows.Clear();
        _sidebarSwitches.Clear();
        foreach (var key in AllWidgetKeys)
        {
            var (label, icon) = WidgetInfo[key];
            var row = new Border { Style = (Style)FindResource("NavRow"), Tag = key };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            if (icon.StartsWith("geo:"))
            {
                bool filled = icon == "geo:IconStartLights";
                grid.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource(icon[4..]), Stretch = Stretch.Uniform, Width = 22, Height = 22,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Stroke = filled ? null : (Brush)FindResource("CyanBrush"), StrokeThickness = 1.8,
                    StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round,
                    Fill = filled ? (Brush)FindResource("CyanBrush") : null
                });
            }
            else
            {
                grid.Children.Add(new TextBlock
                {
                    Text = icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 21,
                    Foreground = (Brush)FindResource("CyanBrush"), VerticalAlignment = VerticalAlignment.Center
                });
            }
            var name = new TextBlock { Text = label, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = _state[key].Visible, VerticalAlignment = VerticalAlignment.Center };
            string captured = key;
            toggle.Checked += (_, _) => SetWidgetVisible(captured, true);
            toggle.Unchecked += (_, _) => SetWidgetVisible(captured, false);
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "Switch_" + key);
            System.Windows.Automation.AutomationProperties.SetName(toggle, label);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);
            row.Child = grid;
            row.MouseLeftButtonUp += (_, _) => SelectWidget(captured);
            WidgetRows.Children.Add(row);
            _sidebarRows[key] = row;
            _sidebarSwitches[key] = toggle;
        }
        HighlightSelectedRow();
    }

    private void HighlightSelectedRow()
    {
        foreach (var (key, row) in _sidebarRows)
        {
            bool selected = key == _selectedWidget;
            row.BorderBrush = selected ? (Brush)FindResource("CyanBrush") : (Brush)FindResource("CardBorderBrush");
            row.Background = selected ? (Brush)new BrushConverter().ConvertFromString("#0A3550")! : (Brush)new BrushConverter().ConvertFromString("#06202F")!;
            row.Effect = selected ? (System.Windows.Media.Effects.Effect)FindResource("CyanGlow") : null;
        }
    }

    private void SelectWidget(string key)
    {
        if (key == _selectedWidget) return;
        _selectedWidget = key;
        HighlightSelectedRow();
        ReloadAllControlsFromStore();
    }

    private void SetWidgetVisible(string key, bool visible)
    {
        if (_suppressChangeEvents) return;
        _state[key] = _state[key] with { Visible = visible };
        if (key == _selectedWidget) LoadPlacementIntoControls(key);
        _ = SendPlacementAsync(key);
    }

    // --- Tabs ---

    private void TabChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int index)) ShowTab(index);
    }

    private void ShowTab(int index)
    {
        UIElement[] panels = [Tab0, Tab1, Tab2, Tab3, Tab4, Tab5, Tab6];
        for (int i = 0; i < panels.Length; i++)
            panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        var buttons = new[] { TabBtn0, TabBtn1, TabBtn2, TabBtn3, TabBtn4, TabBtn5, TabBtn6 };
        if (buttons[index].IsChecked != true) buttons[index].IsChecked = true;
    }

    // --- Top bar: save / import / export / settings ---

    private async void SaveAll(object sender, RoutedEventArgs e)
    {
        SaveProfileStore();
        string message = "Layout salvo";
        if (ProfileBox.SelectedItem is ComboBoxItem { Tag: string profileName })
        {
            ProfileFiles.Save(profileName);
            message += $" no perfil “{profileName}”";
        }
        if (ScopeBox.SelectedItem is ComboBoxItem { Tag: string scopeKey } && scopeKey.Length > 0)
        {
            bool sent = await _profilesClient.SendAsync("saveClassProfile", scopeKey, null);
            message += sent ? $" e como layout da classe/carro “{scopeKey}”" : " (o layout da classe precisa do overlay aberto)";
            await Task.Delay(250);
            var disk = new WidgetPlacementStore();
            PlacementPersistence.Load(disk);
            foreach (var (k, placements) in disk.ClassProfiles) _profileStore.ClassProfiles[k] = placements;
            RefreshClassProfileList();
        }
        SetStatus(message + ".");
    }

    private void ExportProfile(object sender, RoutedEventArgs e)
    {
        SaveProfileStore();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Perfil Live Coach V3 (*.json)|*.json", FileName = "v3-layout-export.json" };
        if (dialog.ShowDialog(this) != true) return;
        SetStatus(PlacementPersistence.TryExport(dialog.FileName) ? $"Exportado para {dialog.FileName}" : "Falha ao exportar.");
    }

    private async void ImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Perfil Live Coach V3 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        ProfileFiles.BackupCurrent();
        if (!PlacementPersistence.TryImport(_profileStore, dialog.FileName))
        {
            SetStatus("Falha ao importar — arquivo inválido ou versão de esquema incompatível.");
            return;
        }
        PlacementPersistence.Save(_profileStore);
        bool sent = await _profilesClient.SendAsync("reloadFromDisk", "", null);
        SeedStateFromStore();
        ReloadAllControlsFromStore();
        SetStatus(sent ? "Perfil importado e aplicado ao overlay." : "Perfil importado (overlay fechado; vale no próximo início).");
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var shortcuts = new MenuItem { Header = "Atalhos de teclado" };
        shortcuts.Click += (_, _) => ShowShortcuts();
        var folder = new MenuItem { Header = "Abrir pasta de configurações" };
        folder.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", ProfileFiles.Root);
        menu.Items.Add(shortcuts);
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "Na janela do overlay (OverlayHost):\n\n" +
            "E — alternar modo de edição (mostrar/arrastar os widgets)\n" +
            "Espaço — alternar click-through global\n" +
            "T — alternar a simulação (dados fictícios)\n" +
            "Esc — fechar o overlay",
            "Atalhos de teclado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void QuickAccess(object sender, RoutedEventArgs e)
    {
        switch ((string)((Button)sender).Tag)
        {
            case "visibility": ShowTab(6); break;
            case "shortcuts": ShowShortcuts(); break;
            case "p2p": SelectWidget("relative"); ShowTab(2); SetStatus("Coluna Overtake: ative-a na tabela de colunas (aparece só em sessões com push-to-pass)."); break;
            case "radar": SelectWidget("radar"); ShowTab(0); break;
            case "start-helper": SelectWidget("start-helper"); ShowTab(0); break;
        }
        await Task.CompletedTask;
    }

    // --- Named profiles (top bar + "Gerenciar perfis") ---

    /// <summary>Fills the top-bar profile / scope boxes. Selection handlers are attached only after
    /// the initial fill so populating them never triggers a load.</summary>
    private void RefreshProfileBoxes()
    {
        ProfileBox.SelectionChanged -= ProfileBox_SelectionChanged;
        ScopeBox.SelectionChanged -= ScopeBox_SelectionChanged;

        ProfileBox.Items.Clear();
        ProfileBox.Items.Add(new ComboBoxItem { Content = "Layout atual (sem perfil)", Tag = null });
        foreach (var name in ProfileFiles.List()) ProfileBox.Items.Add(new ComboBoxItem { Content = "Perfil: " + name, Tag = name });
        string? active = ProfileFiles.ActiveName;
        ProfileBox.SelectedIndex = active is null ? 0 : Math.Max(0, ProfileBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == active));

        string? scope = (ScopeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ScopeBox.Items.Clear();
        ScopeBox.Items.Add(new ComboBoxItem { Content = "Global (todas as classes)", Tag = "" });
        foreach (var key in _profileStore.ClassProfiles.Keys.OrderBy(k => k))
            ScopeBox.Items.Add(new ComboBoxItem { Content = "Classe/carro: " + key, Tag = key });
        int scopeIndex = scope is null ? 0 : ScopeBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == scope);
        ScopeBox.SelectedIndex = Math.Max(0, scopeIndex);

        ProfileBox.SelectionChanged += ProfileBox_SelectionChanged;
        ScopeBox.SelectionChanged += ScopeBox_SelectionChanged;
    }

    private async void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string name)
        {
            ProfileFiles.ActiveName = null;
            return;
        }
        await LoadNamedProfile(name);
    }

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScopeBox.SelectedItem is ComboBoxItem { Tag: string key })
            SetStatus(key.Length == 0
                ? "Escopo global: “Salvar” grava o layout para todas as classes."
                : $"Escopo “{key}”: “Salvar” também grava este layout como perfil dessa classe/carro (aplicado sozinho ao entrar numa sessão com ela).");
    }

    private async Task LoadNamedProfile(string name)
    {
        SaveProfileStore();
        ProfileFiles.BackupCurrent();
        if (!ProfileFiles.Activate(name))
        {
            SetStatus($"Perfil “{name}” não encontrado.");
            RefreshProfileBoxes();
            return;
        }
        _profileStore.ColumnOverrides.Clear();
        _profileStore.AppearanceOverrides.Clear();
        _profileStore.HeaderOverrides.Clear();
        _profileStore.ClassColorOverrides.Clear();
        _profileStore.StandingsRules = null; _profileStore.RelativeRules = null; _profileStore.FuelConfig = null;
        _profileStore.NumberFormat = null; _profileStore.ClassRankColors = null;
        PaletteTokens.ClearAllNameOverrides();
        PlacementPersistence.Load(_profileStore);
        bool sent = await _profilesClient.SendAsync("reloadFromDisk", "", null);
        SeedStateFromStore();
        ReloadAllControlsFromStore();
        SetStatus(sent ? $"Perfil “{name}” carregado e aplicado ao overlay (layout anterior guardado como “_anterior”)." : $"Perfil “{name}” carregado (overlay fechado).");
    }

    private async void ManageProfiles(object sender, MouseButtonEventArgs e)
    {
        var dialog = new ProfilesDialog(_profileStore) { Owner = this };
        dialog.ShowDialog();
        if (dialog.LoadRequested is { } toLoad) await LoadNamedProfile(toLoad);
        RefreshProfileBoxes();
    }

    private async void RestoreAllDefaults(object sender, MouseButtonEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Restaurar TODAS as configurações (posições, colunas, tipografia, formatos, regras, cores e combustível) para o padrão?\n\nO layout atual é guardado em profiles\\_anterior.json.",
            "Restaurar padrões", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        SaveProfileStore();
        ProfileFiles.BackupCurrent();

        var fresh = new WidgetPlacementStore();
        foreach (var (key, state) in BuildDefaults())
        {
            int z = Array.IndexOf(AllWidgetKeys, key);
            fresh.Set(key, new WidgetPlacement(0, state.X, state.Y, PlacementAnchor.TopLeft, state.Width, state.Height, 1f, false, z));
        }
        PlacementPersistence.Save(fresh);
        _profileStore.ColumnOverrides.Clear();
        _profileStore.AppearanceOverrides.Clear();
        _profileStore.HeaderOverrides.Clear();
        _profileStore.ClassColorOverrides.Clear();
        _profileStore.StandingsRules = null; _profileStore.RelativeRules = null; _profileStore.FuelConfig = null;
        _profileStore.NumberFormat = null; _profileStore.ClassRankColors = null; _profileStore.SessionVisibility = null;
        _profileStore.ClassProfiles.Clear();
        PaletteTokens.ClearAllNameOverrides();
        PaletteTokens.SetRankColors(DefaultRankColors);
        ProfileFiles.ActiveName = null;
        PlacementPersistence.Load(_profileStore);
        bool sent = await _profilesClient.SendAsync("reloadFromDisk", "", null);
        foreach (var (key, state) in BuildDefaults()) _state[key] = state;
        ReloadAllControlsFromStore();
        RefreshProfileBoxes();
        SetStatus(sent ? "Padrões restaurados e aplicados ao overlay." : "Padrões restaurados (overlay fechado).");
    }

    // --- History (top bar + Layout tab) ---

    private async void UndoPlacement(object sender, RoutedEventArgs e)
    {
        bool sent = await _undoRedoClient.SendAsync("undo");
        SetStatus(sent ? "Desfeito." : "Overlay fechado ou inacessível — nada foi aplicado.");
        await Task.Delay(250);
        RefreshLiveStateFromDisk();
    }

    private async void RedoPlacement(object sender, RoutedEventArgs e)
    {
        bool sent = await _undoRedoClient.SendAsync("redo");
        SetStatus(sent ? "Refeito." : "Overlay fechado ou inacessível — nada foi aplicado.");
        await Task.Delay(250);
        RefreshLiveStateFromDisk();
    }

    private async void ToggleEditMode(object sender, RoutedEventArgs e)
    {
        _editModeUnlocked = !_editModeUnlocked;
        bool sent = await _editModeClient.SendAsync(_editModeUnlocked);
        EditModeButton.Content = _editModeUnlocked ? "Travar (mostrar só na pista)" : "Destravar para editar";
        EditModeStatus.Text = sent
            ? (_editModeUnlocked ? "Destravado — os overlays aparecem agora, em qualquer tela." : "Travado — os overlays só aparecem na pista.")
            : "Overlay fechado ou inacessível — nada foi aplicado.";
        if (!_editModeUnlocked) { await Task.Delay(250); RefreshLiveStateFromDisk(); }
    }

    /// <summary>Reloads every per-widget / global control from <see cref="_profileStore"/> for the selected widget.</summary>
    private void ReloadAllControlsFromStore()
    {
        bool previousSuppress = _suppressChangeEvents;
        _suppressChangeEvents = true;
        try
        {
            string label = WidgetInfo[_selectedWidget].Label;
            LayoutTitle.Text = "Layout – " + label;
            HeaderTitle.Text = "Cabeçalhos – " + label;
            AppearanceTitle.Text = "Aparência – " + label;
            ColumnsTitle.Text = "Colunas – " + label;
            LoadPlacementIntoControls(_selectedWidget);
            LoadColumnsForSelectedWidget();
            LoadAppearanceIntoControls();
            LoadHeaderForSelectedWidget();
            LoadRulesIntoControls();
            LoadFuelConfigIntoControls();
            LoadNumberFormatIntoControls();
            RefreshClassColorList();
            BuildRankColorRows();
            RefreshClassProfileList();
            RefreshSessionVisibilityGrid();
            foreach (var key in AllWidgetKeys)
                if (_sidebarSwitches.TryGetValue(key, out var sw)) sw.IsChecked = _state[key].Visible;
        }
        finally { _suppressChangeEvents = previousSuppress; }
        UpdateWidthWarning();
        _previewHost?.ApplyProfile(_profileStore);
    }
}

/// <param name="X">Virtual-desktop DIPs, top-left anchored (spec §4).</param>
public readonly record struct WidgetUiState(
    float X, float Y, float Width, float Height,
    bool Visible = true, bool Locked = false, float Opacity = 1f, float Scale = 1f, bool ClickThrough = true, bool AutoSize = true);
