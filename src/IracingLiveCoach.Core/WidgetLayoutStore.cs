using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.Core;

/// <summary>One widget's own position/size/visibility. Left/Top are null until the driver has
/// actually moved the widget once (matching AppSettings's own existing Left/Top nullability
/// convention) -- a window with null Left/Top uses WPF's own default startup placement.</summary>
public class WidgetLayout
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Visible { get; set; } = true;
    // Per-widget instead of a suite-wide opacity: a radar can stay subtle while a timing board
    // remains readable.  Existing JSON omits this field and naturally deserializes to 1.0.
    public double Opacity { get; set; } = 1.0;
    // Visual scale is deliberately per widget: a compact Relative can use 80% typography while
    // a Fuel calculation remains larger and readable.
    public double FontScale { get; set; } = 1.0;
    // Shared appearance controls.  They deliberately live with each individual widget rather
    // than in a global theme so a compact timing tower can remain understated while a fuel card
    // stays highly legible.  Defaults match the broadcast preset.
    public string Theme { get; set; } = "Broadcast";
    public double BackgroundBrightness { get; set; } = 1.0;
    public double BackgroundSaturation { get; set; } = 1.0;
    public double CornerRadius { get; set; } = 0.0;
    public bool TextShadow { get; set; }
    public bool ShowHeader { get; set; } = true;
    public bool HideInReplay { get; set; }
    public int RenderFps { get; set; } = 30;
    // Relative / standings presentation controls modelled after the corresponding Kapps
    // settings groups.  A zero row limit means automatic (only as many entries as useful).
    public int RelativeRows { get; set; } = 5;
    public int StandingsRows { get; set; } = 8;
    public bool CondensedRows { get; set; } = true;
    public bool ShowFlags { get; set; } = true;
    public bool ShowManufacturerLogos { get; set; } = true;
    public bool ShowIRatingGain { get; set; } = true;
    public bool HighlightCarsAlongside { get; set; } = true;
    public string RowStyle { get; set; } = "Solid";
    public string DriverNameStyle { get; set; } = "Short";
    public bool ShowSessionInfo { get; set; } = true;
    public bool ShowTrackInfo { get; set; } = true;
    public bool ShowCarInfo { get; set; } = true;
    // Radar / start-helper controls are persisted alongside the same set of widget settings.
    public int RadarRangeMeters { get; set; } = 55;
    public bool RadarShowDistanceLabels { get; set; } = true;
    public bool StartHelperEnabled { get; set; } = true;
    // Fuel is deliberately field-based: drivers can keep the compact estimate-only view or
    // expose the raw consumption inputs when they are managing strategy.
    public bool FuelShowLevel { get; set; } = true;
    public bool FuelShowUsePerHour { get; set; } = true;
    public bool FuelShowAveragePerLap { get; set; } = true;
    public bool FuelShowLapsRemaining { get; set; } = true;
    public bool FuelShowTimeRemaining { get; set; } = true;
    // 14/09/2026: "Em Standings as classes não se misturam, igual no Kapps e eu posso escolher
    // quantos eu quero mostrar da minha classe e das outras" -- 0 means "show all" for either field.
    public int StandingsMyClassRows { get; set; } = 0;
    public int StandingsOtherClassRows { get; set; } = 3;
    // "quero em standings ter a opção de interval, não só gap" -- INTERVAL (gap to the car directly
    // ahead) instead of GAP (gap to the leader). False = GAP, matching the existing default.
    public bool StandingsShowInterval { get; set; } = false;
}

/// <summary>Keyed replacement for AppSettings's old flat Left/Top/Width/Height fields -- one entry
/// per widget ("coach", "p2p", "relative", "standings", and any future key), so adding a widget
/// later never needs a schema change, just a new Get(key, ...) call. Plain JSON under
/// %APPDATA%\iracing-live-coach\settings.json -- the SAME file AppSettings already used, with a
/// "Widgets" dictionary added alongside the pre-existing ImportKey field (both classes read/write
/// disjoint parts of one JSON document via System.Text.Json's own tolerance for unknown
/// properties, so neither class needs to know about the other's fields).</summary>
public class WidgetLayoutStore
{
    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iracing-live-coach",
        "settings.json");

    public Dictionary<string, WidgetLayout> Widgets { get; set; } = new();

    public IReadOnlyDictionary<string, WidgetLayout> All => Widgets;

    public WidgetLayout Get(string key, double defaultWidth, double defaultHeight)
    {
        if (Widgets.TryGetValue(key, out var existing)) return existing;
        var layout = new WidgetLayout { Width = defaultWidth, Height = defaultHeight };
        Widgets[key] = layout;
        return layout;
    }

    public static WidgetLayoutStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Widgets", out var widgetsElement))
                {
                    var widgets = JsonSerializer.Deserialize<Dictionary<string, WidgetLayout>>(widgetsElement.GetRawText());
                    if (widgets is not null) return new WidgetLayoutStore { Widgets = widgets };
                }
                // Pre-this-task shape: flat Left/Top/Width/Height with no "Widgets" dictionary at
                // all. Migrate the coach widget's own already-saved position so an upgrading
                // driver doesn't lose it -- every other (new) widget just starts at its defaults.
                var store = new WidgetLayoutStore();
                if (doc.RootElement.TryGetProperty("Width", out var widthEl))
                {
                    var coach = new WidgetLayout
                    {
                        Left = doc.RootElement.TryGetProperty("Left", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetDouble() : null,
                        Top = doc.RootElement.TryGetProperty("Top", out var t) && t.ValueKind != JsonValueKind.Null ? t.GetDouble() : null,
                        Width = widthEl.GetDouble(),
                        Height = doc.RootElement.TryGetProperty("Height", out var h) ? h.GetDouble() : 200,
                    };
                    store.Widgets["coach"] = coach;
                }
                return store;
            }
        }
        catch
        {
            // Corrupted/unreadable settings file -- fall back to defaults rather than crash on startup.
        }
        return new WidgetLayoutStore();
    }

    public void Save()
    {
        try
        {
            // Preserve ImportKey (owned by AppSettings, read here only to avoid clobbering it --
            // this store never interprets or validates that field, just round-trips it).
            string? importKey = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    var existingDoc = JsonDocument.Parse(File.ReadAllText(FilePath));
                    if (existingDoc.RootElement.TryGetProperty("ImportKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                        importKey = keyEl.GetString();
                }
                catch { /* ignore -- best effort preservation only */ }
            }

            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var merged = new Dictionary<string, object?> { ["Widgets"] = Widgets, ["ImportKey"] = importKey };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort -- a failed save shouldn't crash the overlay, just means layout won't persist.
        }
    }
}
