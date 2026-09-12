using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public partial class MainWindow : Window
{
    private readonly OverlayViewModel _viewModel = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private TelemetryReader? _telemetryReader;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is double left && _settings.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        _viewModel.StatusText = "Aguardando sessão do iRacing...";

        _telemetryReader = new TelemetryReader();
        _telemetryReader.SessionDetected += (carId, trackId) => _ = OnSessionDetectedAsync(carId, trackId);
        _telemetryReader.Start();
    }

    /// <summary>Runs once per app session, the first time TelemetryReader reports a detected
    /// car+track (see TelemetryReader.SessionDetected) -- fetches that combo's own history and
    /// only then starts feeding live telemetry into a LiveCoachEngine. Car/track are never picked
    /// by the user, per the design spec ("auto-detected once the SDK reports a session").</summary>
    private async System.Threading.Tasks.Task OnSessionDetectedAsync(int carId, int trackId)
    {
        Dispatcher.Invoke(() => _viewModel.StatusText = $"Carro {carId} / Pista {trackId} -- carregando histórico...");

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "baselines");
        // AppSettings.ImportKey (seeded at publish/setup time -- see AppSettings.cs) is the normal
        // path so the app needs zero manual configuration; LOCAL_COACH_SECRET still wins if set,
        // for a driver who prefers an environment variable or is running multiple keys.
        var importKey = Environment.GetEnvironmentVariable("LOCAL_COACH_SECRET") ?? _settings.ImportKey ?? "";
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var sync = new BaselineSync(httpClient, cacheDir, "https://iracing-analytics.vercel.app", importKey);
        var baseline = await sync.GetBaselineAsync(carId, trackId);

        var engine = new LiveCoachEngine(baseline.Corners, baseline.GearModel, baseline.TrackLengthMeters);
        engine.CornerCompleted += feedback => Dispatcher.Invoke(() => _viewModel.OnCornerCompleted(feedback));

        Dispatcher.Invoke(() =>
        {
            _viewModel.StatusText = baseline.Corners.Count > 0
                ? $"Carro {carId} / Pista {trackId} -- {baseline.Corners.Count} curvas com histórico"
                : $"Carro {carId} / Pista {trackId} -- sem histórico ainda";
            _telemetryReader?.AttachEngine(engine);
        });
    }

    // 12/09/2026: hand-rolled drag-to-move -- WindowStyle=None means there's no native title bar to
    // drag. DragMove() blocks until the mouse button is released, so persisting right after it
    // returns saves exactly once per drag gesture (not on every intermediate mouse-move tick).
    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        PersistLayout();
    }

    // Hand-rolled resize grip (same reason as drag-to-move: no native border to resize from).
    // Clamped to MinWidth/MinHeight (set in XAML) so the grip can't shrink the overlay to nothing.
    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e) => PersistLayout();

    private void PersistLayout()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistLayout();
        _telemetryReader?.Dispose();
        base.OnClosed(e);
    }
}
