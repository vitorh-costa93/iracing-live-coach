using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Theme;

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

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoControls(_selectedWidget);
        PlacementPersistence.Load(_profileStore);
        RefreshClassColorList();
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
