using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Forms;
using IracingLiveCoach.Core;
using System.Collections.Generic;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using DrawingIcon = System.Drawing.Icon;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace IracingLiveCoach.App;

public partial class MainWindow : Window
{
    private readonly OverlayViewModel _viewModel = new();
    private readonly AppSettings _appSettings = AppSettings.Load();
    private readonly WidgetLayoutStore _layoutStore = WidgetLayoutStore.Load();
    private readonly WidgetLayout _coachLayout;
    private TelemetryReader? _telemetryReader;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _lockMenuItem;
    private RelativeWidget? _relativeWidget;
    private StandingsWidget? _standingsWidget;
    private FuelWidget? _fuelWidget;
    private WeatherWidget? _weatherWidget;
    private TireWearWidget? _tireWidget;
    private RadarWidget? _radarWidget;
    private ControlPanelWindow? _controlPanel;

    // 12/09/2026: "quero que o lugar que ele ocupa na tela e tamanho seja personalizável" -- locked
    // by default so the overlay never eats a click meant for iRacing itself; the driver unlocks it
    // (tray icon) only to drag/resize, then locks it again. See ClickThrough.cs.
    private bool _locked = true;
    private readonly bool _previewMode = Environment.GetCommandLineArgs().Contains("--preview", StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _coachLayout = _layoutStore.Get("coach", 320, 200);
        Width = _coachLayout.Width;
        Height = _coachLayout.Height;
        if (_coachLayout.Left is double left && _coachLayout.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        _viewModel.Status = CoachStatus.Waiting;
        _viewModel.StatusText = "Aguardando sessão do iRacing...";

        _relativeWidget = new RelativeWidget(_layoutStore.Get("relative", 360, 470), () => _layoutStore.Save());
        _relativeWidget.Show();

        _standingsWidget = new StandingsWidget(_layoutStore.Get("standings", 540, 480), () => _layoutStore.Save());
        _standingsWidget.Show();

        _fuelWidget = new FuelWidget(_layoutStore.Get("fuel", 250, 170), () => _layoutStore.Save());
        _fuelWidget.Show();

        _weatherWidget = new WeatherWidget(_layoutStore.Get("weather", 280, 200), () => _layoutStore.Save());
        _weatherWidget.Show();

        _tireWidget = new TireWearWidget(_layoutStore.Get("tires", 240, 220), () => _layoutStore.Save());
        _tireWidget.Show();

        _radarWidget = new RadarWidget(_layoutStore.Get("radar", 220, 106), () => _layoutStore.Save());
        _radarWidget.Show();

        _controlPanel = new ControlPanelWindow(_layoutStore, () => _layoutStore.Save(), new (string, string, Window)[]
        {
            ("coach", "Coach", this),
            ("relative", "Relative", _relativeWidget),
            ("standings", "Standings (F1)", _standingsWidget),
            ("fuel", "Fuel", _fuelWidget),
            ("weather", "Weather", _weatherWidget),
            ("tires", "Tire Wear", _tireWidget),
            ("radar", "Radar", _radarWidget),
        });
        _controlPanel.Show();

        SetupTrayIcon();
        ApplyClickThrough();

        if (_previewMode)
        {
            ShowPreview();
            return;
        }

        _telemetryReader = new TelemetryReader();
        _telemetryReader.SessionDetected += (carId, trackId) => _ = OnSessionDetectedAsync(carId, trackId);
        _telemetryReader.FullRelativeUpdated += rows => Dispatcher.Invoke(() => _relativeWidget?.UpdateRows(rows));
        _telemetryReader.PlayerCarStatusUpdated += status => Dispatcher.Invoke(() =>
        {
            _relativeWidget?.UpdatePlayerStatus(status);
        });
        _telemetryReader.StandingsUpdated += rows => Dispatcher.Invoke(() => _standingsWidget?.UpdateRows(rows));
        _telemetryReader.SessionStatusUpdated += status => Dispatcher.Invoke(() =>
        {
            _standingsWidget?.UpdateSessionStatus(status);
            _relativeWidget?.UpdateSessionStatus(status);
        });
        _telemetryReader.FuelUpdated += status => Dispatcher.Invoke(() => _fuelWidget?.UpdateStatus(status));
        _telemetryReader.WeatherUpdated += status => Dispatcher.Invoke(() => _weatherWidget?.UpdateStatus(status));
        _telemetryReader.TireWearUpdated += status => Dispatcher.Invoke(() => _tireWidget?.UpdateStatus(status));
        _telemetryReader.RadarUpdated += status => Dispatcher.Invoke(() => _radarWidget?.UpdateStatus(status));
        _telemetryReader.OnTrackStateChanged += isOnTrack => Dispatcher.Invoke(() => _controlPanel?.ApplyOnTrackGate(isOnTrack));
        _telemetryReader.Start();
    }

    // Development-only visual fixture.  It is opt-in through --preview, does not subscribe to the
    // SDK and never persists its temporary positions, so it is safe for design review screenshots.
    private void ShowPreview()
    {
        var gt3 = new List<RelativeRow>
        {
            new(3, "P. SANTOS", -3.910, null, null, null, null, false, "🇧🇷", "A 2.94", "#1976FF", 3620, 1, "MERCEDES"),
            new(4, "G. LIMA", -1.422, null, null, null, null, false, "🇧🇷", "B 3.45", "#20C060", 3410, 1, "FERRARI"),
            new(5, "V. COSTA", 0.0, null, null, null, null, false, "🇧🇷", "A 2.58", "#1976FF", 3574, 1, "MCLAREN", true),
            new(6, "A. SOUZA", 0.887, null, null, null, null, false, "🇧🇷", "A 3.12", "#1976FF", 3280, 1, "LAMBORGHINI"),
            new(7, "B. ROCHA", 3.321, null, null, null, null, false, "🇧🇷", "B 2.76", "#20C060", 3190, 1, "AUDI"),
        };
        var formula = new List<RelativeRow>
        {
            new(3, "K. TANAKA", -2.840, null, true, 4, 12, false, "🇯🇵", "A 3.91", "#1976FF", 5620, 2, "DALLARA"),
            new(4, "R. SILVA", -0.916, null, false, 3, 82, true, "🇧🇷", "A 3.20", "#1976FF", 5410, 2, "DALLARA"),
            new(5, "V. COSTA", 0.0, null, true, 3, 18, false, "🇧🇷", "A 3.49", "#1976FF", 5262, 2, "DALLARA"),
            new(6, "J. MILLER", 0.642, null, false, 2, 56, true, "🇺🇸", "A 2.87", "#1976FF", 5180, 2, "DALLARA"),
        };
        var playerLastLap = 108.326;
        double?[] gaps = { null, 1.842, 3.216, 5.704, 7.126 };
        double?[] deltaIRs = { 62, 45, 32, 24, 18 };
        var standings = gt3.Select((row, index) =>
        {
            var lastLap = 108.0 + index * .4;
            return new StandingsRow(index + 1, row.DriverCode, 12, lastLap, null, row.IsPlayer, row.FlagEmoji,
                row.LicString, row.LicColorHex, row.IRating, row.CarClassId, row.ManufacturerBadge,
                gaps[index], deltaIRs[index], lastLap - playerLastLap);
        }).ToList();

        _standingsWidget?.UpdateRows(standings);
        _relativeWidget?.UpdateRows(gt3);
        var status = new PlayerCarStatus(54.2, "MODERADO", 107.912, playerLastLap, 32.0);
        _relativeWidget?.UpdatePlayerStatus(status);
        var sessionStatus = new SessionStatus("GT3", "RACE", 12, 28, "GREEN", "#FF20E884", 3420, 20);
        _standingsWidget?.UpdateSessionStatus(sessionStatus);
        _relativeWidget?.UpdateSessionStatus(sessionStatus);
        _radarWidget?.UpdateStatus(new RadarStatus(true, false, new List<RadarBlip> { new(28, "P. SANTOS"), new(-18, "A. SOUZA") }, true));
        _fuelWidget?.UpdateStatus(new FuelStatus(43.9, 3.4, 2.05, 21.4, 2315));
        _weatherWidget?.UpdateStatus(new WeatherStatus(24.0, 32.0, 0.0, 1, false, new List<TrackPositionDot>()));
        _tireWidget?.UpdateStatus(new TireWearStatus(
            new TireCornerWear(0.62, 0.6, 0.58), new TireCornerWear(0.6, 0.58, 0.6),
            new TireCornerWear(0.55, 0.52, 0.5), new TireCornerWear(0.5, 0.52, 0.55), null));

        // Spread every widget across a wide, non-overlapping grid -- this fixture is captured for
        // design review (screenshots at each widget's own MinWidth/MinHeight), and an overlap would
        // corrupt those captures the same way a stale layout from a previous run would.
        _standingsWidget!.Left = 20; _standingsWidget.Top = 60;
        _relativeWidget!.Left = 620; _relativeWidget.Top = 60;
        _fuelWidget!.Left = 1060; _fuelWidget.Top = 60;
        _weatherWidget!.Left = 1060; _weatherWidget.Top = 260;
        _tireWidget!.Left = 1060; _tireWidget.Top = 460;
        _radarWidget!.Left = 1350; _radarWidget.Top = 60;
        _locked = false;
        ApplyClickThrough();

        // Do not let a user's normal "hidden outside the track" preference conceal a review
        // capture.  This branch is only reachable with --preview and never calls PersistLayout.
        foreach (var widget in new Window[] { _standingsWidget, _relativeWidget, _radarWidget, _fuelWidget, _weatherWidget, _tireWidget })
        {
            widget.WindowStartupLocation = WindowStartupLocation.Manual;
            widget.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Runs once per app session, the first time TelemetryReader reports a detected
    /// car+track (see TelemetryReader.SessionDetected) -- fetches that combo's own history and
    /// only then starts feeding live telemetry into a LiveCoachEngine. Car/track are never picked
    /// by the user, per the design spec ("auto-detected once the SDK reports a session").</summary>
    private async System.Threading.Tasks.Task OnSessionDetectedAsync(int carId, int trackId)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.Status = CoachStatus.Loading;
            _viewModel.StatusText = $"Carro {carId} / Pista {trackId} -- carregando histórico...";
        });

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "baselines");
        // AppSettings.ImportKey (seeded at publish/setup time -- see AppSettings.cs) is the normal
        // path so the app needs zero manual configuration; LOCAL_COACH_SECRET still wins if set,
        // for a driver who prefers an environment variable or is running multiple keys.
        var importKey = Environment.GetEnvironmentVariable("LOCAL_COACH_SECRET") ?? _appSettings.ImportKey ?? "";
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var sync = new BaselineSync(httpClient, cacheDir, "https://iracing-analytics.vercel.app", importKey);
        var baseline = await sync.GetBaselineAsync(carId, trackId);
        _telemetryReader?.SetTrackLength(baseline.TrackLengthMeters);

        var engine = new LiveCoachEngine(baseline.Corners, baseline.GearModel, baseline.TrackLengthMeters);
        engine.CornerCompleted += feedback => Dispatcher.Invoke(() => _viewModel.OnCornerCompleted(feedback));

        Dispatcher.Invoke(() =>
        {
            _viewModel.Status = CoachStatus.Ready;
            _viewModel.StatusText = baseline.Corners.Count > 0
                ? $"Carro {carId} / Pista {trackId} -- {baseline.Corners.Count} curvas com histórico"
                : $"Carro {carId} / Pista {trackId} -- sem histórico ainda";
            _telemetryReader?.AttachEngine(engine);
        });
    }

    // 12/09/2026: hand-rolled drag-to-move -- WindowStyle=None means there's no native title bar to
    // drag. DragMove() blocks until the mouse button is released, so persisting right after it
    // returns saves exactly once per drag gesture (not on every intermediate mouse-move tick). Only
    // ever reachable while unlocked -- click-through swallows the mouse event entirely while locked.
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
        _coachLayout.Left = Left;
        _coachLayout.Top = Top;
        _coachLayout.Width = Width;
        _coachLayout.Height = Height;
        _layoutStore.Save();
    }

    // 12/09/2026: system tray icon (WPF has no tray API of its own -- System.Windows.Forms.NotifyIcon
    // via UseWindowsForms, same technique SimHub/CrewChief-style overlay tools use) with a lock/unlock
    // toggle, matching the request to make position/size personalizable "igual os overlays do Kapps".
    private void SetupTrayIcon()
    {
        _lockMenuItem = new ToolStripMenuItem(LockMenuText());
        _lockMenuItem.Click += (_, _) => ToggleLock();

        var exitItem = new ToolStripMenuItem("Sair");
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_lockMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "iRacing Live Coach",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ToggleLock();
    }

    // "Icon" alone resolves to the inherited Window.Icon PROPERTY here (member lookup shadows a
    // type import for a simple name inside its own class body) -- DrawingIcon (aliased above) is
    // needed for every reference to the actual System.Drawing.Icon TYPE in this class.
    private static DrawingIcon LoadAppIcon()
    {
        try
        {
            var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (path is not null)
            {
                var icon = DrawingIcon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon;
            }
        }
        catch
        {
            // Fall back below -- e.g. running via `dotnet run`, where the main module is a generic
            // apphost without the compiled-in ApplicationIcon resource to extract.
        }
        return SystemIcons.Application;
    }

    private string LockMenuText() => _locked ? "Destravar posição" : "Travar posição";

    private void ToggleLock()
    {
        _locked = !_locked;
        ApplyClickThrough();
        if (_lockMenuItem is not null) _lockMenuItem.Text = LockMenuText();
    }

    private void ApplyClickThrough()
    {
        ClickThrough.Set(new WindowInteropHelper(this).Handle, _locked);
        // Visual affordance: only show the accent border while unlocked, so it's obvious the
        // overlay can be dragged/resized right now -- and just as obviously not once it's locked
        // again for actually driving.
        OuterBorder.BorderBrush = _locked
            ? (Brush)FindResource("HudBorderBrush")
            : (Brush)FindResource("HudBorderActiveBrush");
        // Locking is shared, not independent per window (see RelativeOverlayWindow's own comment)
        // -- one tray toggle moves both the coaching card and the P2P strip in and out of edit mode
        // together, since they're meant to be positioned once and then both stay out of the way.
        _relativeWidget?.SetLocked(_locked);
        _standingsWidget?.SetLocked(_locked);
        _fuelWidget?.SetLocked(_locked);
        _weatherWidget?.SetLocked(_locked);
        _tireWidget?.SetLocked(_locked);
        _radarWidget?.SetLocked(_locked);
        _controlPanel?.SetLocked(_locked);
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistLayout();
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        _telemetryReader?.Dispose();
        _relativeWidget?.Close();
        _standingsWidget?.Close();
        _fuelWidget?.Close();
        _weatherWidget?.Close();
        _tireWidget?.Close();
        _radarWidget?.Close();
        _controlPanel?.Close();
        base.OnClosed(e);
    }
}
