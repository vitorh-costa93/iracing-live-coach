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
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        // 12/09/2026: car/track selection UI is a follow-up -- hardcoded here so this task's own
        // scope (build-verified plumbing) stays testable-by-compilation without inventing a whole
        // settings screen. Replace with a real picker once this is validated against a live session.
        const int placeholderCarId = 0;
        const int placeholderTrackId = 0;

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "baselines");
        var importKey = Environment.GetEnvironmentVariable("LOCAL_COACH_SECRET") ?? "";
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var sync = new BaselineSync(httpClient, cacheDir, "https://iracing-analytics.vercel.app", importKey);
        var baseline = await sync.GetBaselineAsync(placeholderCarId, placeholderTrackId);

        var engine = new LiveCoachEngine(baseline.Corners, baseline.GearModel, baseline.TrackLengthMeters);
        engine.CornerCompleted += feedback => Dispatcher.Invoke(() => _viewModel.OnCornerCompleted(feedback));

        _telemetryReader = new TelemetryReader(engine);
        _telemetryReader.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _telemetryReader?.Dispose();
        base.OnClosed(e);
    }
}
