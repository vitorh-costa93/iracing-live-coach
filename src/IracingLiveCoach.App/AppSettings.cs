using System;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.App;

/// <summary>Persisted app configuration: the shared secret this app uses to authenticate with
/// iracing-analytics's baselines endpoint. Plain JSON under %APPDATA%\iracing-live-coach\settings.json
/// (the SAME file WidgetLayoutStore uses for per-widget layout persistence). Reading a local file
/// instead of an environment variable means the published .exe needs zero manual machine-wide setup
/// after install: the installer/publish step seeds ImportKey here directly. LOCAL_COACH_SECRET (if
/// set) still wins when present, so a user who prefers an env var can still use one. Layout
/// persistence is now owned by WidgetLayoutStore (separate key-per-widget entries), not AppSettings.
/// Save() was removed entirely: nothing in this app ever writes ImportKey at runtime -- it is
/// written once, externally, by the publish process -- so there was no live caller left, and a
/// future caller would have hit a lost-update hazard (its old "preserve Widgets" logic read
/// Widgets from disk, not from the live WidgetLayoutStore instance).</summary>
public class AppSettings
{
    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iracing-live-coach",
        "settings.json");

    public string? ImportKey { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var doc = JsonDocument.Parse(json);
                var settings = new AppSettings();
                if (doc.RootElement.TryGetProperty("ImportKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                    settings.ImportKey = keyEl.GetString();
                return settings;
            }
        }
        catch
        {
            // Corrupted/unreadable settings file -- fall back to defaults rather than crash on startup.
        }
        return new AppSettings();
    }
}
