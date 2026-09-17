using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoControls(_selectedWidget);
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
