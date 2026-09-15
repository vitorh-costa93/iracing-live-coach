using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IracingLiveCoach.App;
using IracingLiveCoach.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/screenshots");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("APPDATA", Path.Combine(Path.GetTempPath(), "live-coach-ui-" + Guid.NewGuid()));
        Application.ResourceAssembly = typeof(StandingsWidget).Assembly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/IracingLiveCoach.App;component/F1Theme.xaml") });
        app.Resources.Add("BoolToVisibilityConverter", new BooleanToVisibilityConverter());
        var standings = new StandingsWidget(new WidgetLayout { Width = 800, Height = 460 }, () => { });
        var names = new[] { "L. MARTINS", "R. ALMEIDA", "P. SANTOS", "G. LIMA", "V. COSTA", "A. SOUZA", "B. ROCHA" };
        var brands = new[] { "FERRARI", "BMW", "MERCEDES", "PORSCHE", "MCLAREN", "LAMBORGHINI", "AUDI" };
        standings.UpdateRows(Enumerable.Range(0, 7).Select(i => new StandingsRow(i + 1, names[i], 12, 108.326 + (i - 4) * .15, null, i == 4, "🇧🇷", "A 3.49", "#1764D9", 3574 + i * 30, 1, brands[i], i * 1.78, 40 - i * 9, (i - 4) * .15, "GT3", "#FF9C24", i + 1, 1.78)).ToList());
        standings.UpdateSessionStatus(new SessionStatus("GT3", "RACE", 12, 28, "GREEN", "#22E889", 3420, 20));
        Capture(standings, "standings", output);
        var relative = new RelativeWidget(new WidgetLayout { Width = 650, Height = 440 }, () => { });
        var rows = Enumerable.Range(0, 5).Select(i => new RelativeRow(i + 3, names[i], (i - 2) * .887, null, null, null, null, false, "🇧🇷", "A 3.49", "#1764D9", 3574, 1, brands[i], i == 2, i + 3, "GT3", "#FF9C24")).ToList();
        relative.UpdateRows(rows);
        relative.UpdatePlayerStatus(new PlayerCarStatus(54.2, "Moderate", 107.912, 108.326, 32));
        if (((RelativeWidgetViewModel)relative.DataContext).HasOvertake) throw new Exception("GT3 must hide overtake.");
        Capture(relative, "relative-gt3", output);
        relative.UpdateRows(rows.Select((row, i) => row with { P2PActive = i == 2, P2PSecondsRemaining = i == 2 ? 12 : i == 3 ? 18 : null, P2PInCooldown = i == 3, ManufacturerBadge = "", ClassShortName = "SF23" }).ToList());
        if (!((RelativeWidgetViewModel)relative.DataContext).HasOvertake) throw new Exception("SF23 must show overtake.");
        Capture(relative, "relative-sf23", output);
        var fuel = new FuelWidget(new WidgetLayout { Width = 290, Height = 440 }, () => { });
        fuel.UpdateStatus(new FuelStatus(18.6, 78, 2.4, 7.75, 838));
        Capture(fuel, "fuel", output);
        fuel.SetColumns(false, false, true, true, false);
        Capture(fuel, "fuel-configured", output);
        var hiddenMetric = Descendants(fuel).OfType<TextBlock>().First(e => e.Text == "NO TANQUE");
        if (hiddenMetric.IsVisible) throw new Exception("Hidden fuel field still rendered.");
        var weather = new WeatherWidget(new WidgetLayout { Width = 300, Height = 230 }, () => { });
        weather.UpdateStatus(new WeatherStatus(24, 32, 0, 0, false, new()));
        Capture(weather, "weather", output);
        var radar = new RadarWidget(new WidgetLayout { Width = 220, Height = 220 }, () => { });
        radar.UpdateStatus(new RadarStatus(true, false, new() { new RadarBlip(8, "ALM"), new RadarBlip(-6, "SOU") }, true));
        Capture(radar, "radar", output);
        var control = new ControlPanelWindow(new WidgetLayoutStore(), () => { }, new[] { ("standings", "Standings", (Window)standings), ("relative", "Relative", (Window)relative), ("fuel", "Combustível", (Window)fuel), ("weather", "Condições da pista", (Window)weather), ("radar", "Radar", (Window)radar) });
        control.SetLocked(false);
        control.Height = 980;
        Capture(control, "control-panel", output);
        foreach (var window in new Window[] { standings, relative, fuel, control })
        {
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            Capture(window, window.GetType().Name + "-minimum", output);
        }
        Console.WriteLine("Native WPF screens rendered; configured fuel visibility and overtake states verified.");
        foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static void Capture(Window window, string name, string output)
    {
        window.Show(); window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
}
