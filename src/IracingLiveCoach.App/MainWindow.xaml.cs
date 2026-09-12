using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public partial class MainWindow : Window
{
    private readonly OverlayViewModel _viewModel = new();
    private TelemetryReader? _telemetryReader;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
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
        var importKey = Environment.GetEnvironmentVariable("LOCAL_COACH_SECRET") ?? "";
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

    protected override void OnClosed(EventArgs e)
    {
        _telemetryReader?.Dispose();
        base.OnClosed(e);
    }
}
