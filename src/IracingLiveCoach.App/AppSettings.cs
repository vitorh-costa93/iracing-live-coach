using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.App;

/// <summary>Persisted app configuration: the shared secret this app uses to authenticate with
/// iracing-analytics's baselines endpoint. Plain JSON under %APPDATA%\iracing-live-coach\settings.json
/// (the SAME file WidgetLayoutStore uses for per-widget layout persistence). Reading a local file
/// instead of an environment variable means the published .exe needs zero manual machine-wide setup
/// after install: the installer/publish step seeds ImportKey here directly. LOCAL_COACH_SECRET (if
/// set) still wins when present, so a user who prefers an env var can still use one. Layout
/// persistence is now owned by WidgetLayoutStore (separate key-per-widget entries), not AppSettings.</summary>
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

    public void Save()
    {
        try
        {
            // Preserve Widgets (owned by WidgetLayoutStore, read here only to avoid clobbering it --
            // this class never interprets or validates that field, just round-trips it).
            object? widgets = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    var existingDoc = JsonDocument.Parse(File.ReadAllText(FilePath));
                    if (existingDoc.RootElement.TryGetProperty("Widgets", out var widgetsEl))
                        widgets = JsonSerializer.Deserialize<Dictionary<string, object>>(widgetsEl.GetRawText());
                }
                catch { /* ignore -- best effort preservation only */ }
            }

            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var merged = new Dictionary<string, object?> { ["ImportKey"] = ImportKey, ["Widgets"] = widgets };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort -- a failed save shouldn't crash the overlay, just means ImportKey won't persist.
        }
    }
}
