using System;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.App;

/// <summary>Persisted app configuration: the overlay window's own position/size (request: "quero
/// que o lugar que ele ocupa na tela e tamanho seja personalizável, igual os overlays do Kapps")
/// and the shared secret this app uses to authenticate with iracing-analytics's baselines endpoint.
/// Plain JSON under %APPDATA%\iracing-live-coach\settings.json -- reading a local file instead of
/// an environment variable means the published .exe needs zero manual machine-wide setup after
/// install: the installer/publish step seeds ImportKey here directly. LOCAL_COACH_SECRET (if set)
/// still wins when present, so a user who prefers an env var can still use one.</summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "settings.json");

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 200;
    public string? ImportKey { get; set; }

    // 13/09/2026: "eu queria que isso estivesse junto da black box de relative do iRacing" -- the
    // P2P strip (RelativeOverlayWindow) is a second, independently positioned window so it can sit
    // right against the driver's own native Relative box, wherever that is on their layout. Kept
    // as its own Left/Top/Width/Height, separate from the main coaching card above.
    public double? RelativeLeft { get; set; }
    public double? RelativeTop { get; set; }
    public double RelativeWidth { get; set; } = 90;
    public double RelativeHeight { get; set; } = 130;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // Corrupted/unreadable settings file -- fall back to defaults rather than crash on startup.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort -- a failed save shouldn't crash the overlay, just means layout won't persist.
        }
    }
}
