using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Ams2.ControlCenter;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "AMS2 Control Center", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    // Abrir o Control Center basta: o overlay (mesma pasta) sobe junto, se ainda nao estiver rodando.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (Process.GetProcessesByName("Ams2.OverlayHost").Length > 0) return;
            string exe = Path.Combine(AppContext.BaseDirectory, "Ams2.OverlayHost.exe");
            if (!File.Exists(exe)) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch { }
    }
}
