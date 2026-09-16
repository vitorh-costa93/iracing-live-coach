using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IracingLiveCoach.V2;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/screenshots");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("APPDATA", Path.Combine(Path.GetTempPath(), "live-coach-ui-" + Guid.NewGuid()));

        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Application.LoadComponent(app, new Uri("/IracingLiveCoach.App;component/V2/App.xaml", UriKind.Relative));
        var overlay = new MainWindow { WindowState = WindowState.Normal, Width = 1920, Height = 1080, Left = -4000, Top = -4000 };
        foreach (var profile in overlay.Widgets) profile.SessionVisible = true;
        overlay.Show();
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var cards = Descendants(overlay).OfType<Border>()
            .Where(border => border.DataContext is WidgetProfile)
            .GroupBy(border => ((WidgetProfile)border.DataContext).Kind)
            .Select(group => group.OrderByDescending(border => border.ActualWidth * border.ActualHeight).First())
            .OrderBy(border => ((WidgetProfile)border.DataContext).Kind);
        foreach (var card in cards)
        {
            var profile = (WidgetProfile)card.DataContext;
            Capture(card, profile.Kind.ToString().ToLowerInvariant(), output);
        }

        var studio = app.Windows.OfType<StudioWindow>().FirstOrDefault();
        if (studio is not null)
        {
            studio.UpdateLayout();
            Capture((FrameworkElement)studio.Content, "overlay-studio", output);
        }

        Console.WriteLine($"V2 widget previews written to {output}.");
        foreach (var window in app.Windows.Cast<Window>().ToArray()) window.Close();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Capture(FrameworkElement element, string name, string output)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }
}
