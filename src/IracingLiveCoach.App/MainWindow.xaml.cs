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
    private RelativeOverlayWindow? _relativeWindow;
    private RelativeWidget? _relativeWidget;
    private StandingsWidget? _standingsWidget;
    private ControlPanelWindow? _controlPanel;

    // 12/09/2026: "quero que o lugar que ele ocupa na tela e tamanho seja personalizável" -- locked
    // by default so the overlay never eats a click meant for iRacing itself; the driver unlocks it
    // (tray icon) only to drag/resize, then locks it again. See ClickThrough.cs.
    private bool _locked = true;

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

        // 13/09/2026: "eu queria que isso estivesse junto da black box de relative do iRacing" --
        // a second window, not a section of this one, so the driver can drag it to sit right next
        // to their own native Relative box independently of where this coaching card ends up.
        _relativeWindow = new RelativeOverlayWindow(_layoutStore.Get("p2p", 90, 130), () => _layoutStore.Save());
        _relativeWindow.Show();

        _relativeWidget = new RelativeWidget(_layoutStore.Get("relative", 260, 240), () => _layoutStore.Save());
        _relativeWidget.Show();

        _standingsWidget = new StandingsWidget(_layoutStore.Get("standings", 320, 360), () => _layoutStore.Save());
        _standingsWidget.Show();

        _controlPanel = new ControlPanelWindow(_layoutStore, () => _layoutStore.Save(), new (string, string, Window)[]
        {
            ("coach", "Coach", this),
            ("p2p", "P2P", _relativeWindow),
            ("relative", "Relative (F1)", _relativeWidget),
            ("standings", "Standings (F1)", _standingsWidget),
        });
        _controlPanel.Show();

        SetupTrayIcon();
        ApplyClickThrough();

        _telemetryReader = new TelemetryReader();
        _telemetryReader.SessionDetected += (carId, trackId) => _ = OnSessionDetectedAsync(carId, trackId);
        _telemetryReader.RelativeUpdated += statuses => Dispatcher.Invoke(() => _relativeWindow?.UpdateRows(statuses));
        _telemetryReader.FullRelativeUpdated += rows => Dispatcher.Invoke(() => _relativeWidget?.UpdateRows(rows));
        _telemetryReader.StandingsUpdated += rows => Dispatcher.Invoke(() => _standingsWidget?.UpdateRows(rows));
        _telemetryReader.Start();
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
        _relativeWindow?.SetLocked(_locked);
        _relativeWidget?.SetLocked(_locked);
        _standingsWidget?.SetLocked(_locked);
        _controlPanel?.SetLocked(_locked);
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistLayout();
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        _telemetryReader?.Dispose();
        _relativeWindow?.Close();
        _relativeWidget?.Close();
        _standingsWidget?.Close();
        _controlPanel?.Close();
        base.OnClosed(e);
    }
}
