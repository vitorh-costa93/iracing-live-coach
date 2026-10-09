using System.Diagnostics;
using System.IO;
using System.Windows;
namespace IracingLiveCoach.ControlCenter;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartAnalyticsAgent();
    }

    // The Racing Analytics tray agent travels with the Control Center, since this is what the driver
    // always opens to play. Best-effort: never blocks or breaks the overlay if the agent is absent.
    private static void StartAnalyticsAgent()
    {
        try
        {
            if (Process.GetProcessesByName("RacingAnalyticsAgent").Length > 0) return;
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RacingAnalyticsAgent", "app", "RacingAnalyticsAgent.exe");
            if (!File.Exists(exe)) return;
            Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch { }
    }
}
