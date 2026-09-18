using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

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
    private readonly Dictionary<string, WidgetUiState> _state = BuildDefaults();
    private string _selectedWidget = "standings";
    private bool _suppressChangeEvents;
    private OverlayPreviewHost? _previewHost;

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoControls(_selectedWidget);
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
        if (_adjustingAspect || PreviewBorder.ActualWidth <= 0) return;
        double targetHeight = PreviewBorder.ActualWidth * 9.0 / 16.0;
        if (Math.Abs(targetHeight - PreviewBorder.ActualHeight) > 0.5)
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
}

/// <param name="X">Virtual-desktop DIPs, top-left anchored (spec §4).</param>
public readonly record struct WidgetUiState(
    float X, float Y, float Width, float Height,
    bool Visible = true, bool Locked = false, float Opacity = 1f, float Scale = 1f);
