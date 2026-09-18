using System.Text.Json;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Theme;

namespace IracingLiveCoach.OverlayHost.Persistence;

/// <summary>
/// Versioned on-disk snapshot of a <see cref="WidgetPlacementStore"/> (spec §3: "persistência local
/// versionada, migrações, gravação atômica"; §12: "duplicar, renomear, importar/exportar"). Lives in
/// OverlayHost rather than Core (the plan originally pointed at a Core copy of V2's
/// WidgetLayoutStore.cs) because it persists this project's own <see cref="WidgetPlacement"/> type,
/// which is itself an OverlayHost/Layout concept -- Core has no dependency on OverlayHost and should
/// not gain one just to host this file; V2's WidgetLayoutStore.cs, on inspection, is also a
/// differently-shaped, largely legacy structure V2's own active UI doesn't read from (it uses
/// ProfileStore.cs/v2-profile.json instead) -- copying its shape verbatim would not have matched
/// what V3 actually needs to persist.
/// </summary>
public sealed record PlacementProfile(
    int SchemaVersion,
    Dictionary<string, WidgetPlacement> Widgets,
    bool FuelRelativeLinkEnabled,
    float FuelRelativeLinkSpacingDip,
    Dictionary<string, string>? ClassColorOverrides = null,
    Dictionary<string, List<ColumnDefinition>>? ColumnOverrides = null,
    StandingsPresentationOptions? StandingsRules = null,
    FuelConfig? FuelConfig = null,
    Dictionary<string, WidgetAppearance>? AppearanceOverrides = null)
{
    public const int CurrentSchemaVersion = 1;
}

public static class PlacementPersistence
{
    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iracing-live-coach", "v3-layout.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Loads the saved profile into <paramref name="store"/> in place, or leaves it
    /// untouched (defaults survive) if the file is missing, corrupted, or an unrecognized schema
    /// version -- never crashes startup and never guesses at data it doesn't understand.</summary>
    public static void Load(WidgetPlacementStore store, string? path = null)
    {
        string filePath = path ?? DefaultPath;
        try
        {
            if (!File.Exists(filePath)) return;
            var profile = JsonSerializer.Deserialize<PlacementProfile>(File.ReadAllText(filePath), Options);
            ApplyProfile(store, profile);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupted/unreadable file -- the store's own already-set defaults remain in effect,
            // matching V2's WidgetLayoutStore.Load() fallback behaviour for the same failure class.
        }
    }

    private static void ApplyProfile(WidgetPlacementStore store, PlacementProfile? profile)
    {
        if (profile is null) return;

        // A single version exists today; this is still the real migration seam spec §3 asks for --
        // a future SchemaVersion bump gets its own branch here, translating old shapes forward,
        // rather than silently misreading fields that changed meaning.
        if (profile.SchemaVersion != PlacementProfile.CurrentSchemaVersion) return;

        foreach (var (key, placement) in profile.Widgets)
            store.Set(key, placement);
        store.FuelRelativeLink = new FuelRelativeLink(profile.FuelRelativeLinkEnabled, profile.FuelRelativeLinkSpacingDip);

        // Spec §16: "Permita personalização e restauração por token, paleta de classe e perfil."
        // Applied to both the store (so a later Save doesn't lose them) and PaletteTokens directly
        // (so this process's widgets pick them up immediately -- no separate live-IPC channel for
        // color exists yet, so a running OverlayHost.exe only sees a new override on its own next
        // Load call, i.e. next launch; documented as an honest gap in OverlayPreviewHost/the Cores
        // tab, not silently pretended to be instant).
        if (profile.ClassColorOverrides is not null)
        {
            foreach (var (className, hex) in profile.ClassColorOverrides)
            {
                store.ClassColorOverrides[className] = hex;
                if (TryParseHexColor(hex, out var color)) PaletteTokens.SetNameOverride(className, color);
            }
        }

        if (profile.ColumnOverrides is not null)
        {
            foreach (var (widgetKey, columns) in profile.ColumnOverrides)
                store.ColumnOverrides[widgetKey] = columns;
        }

        if (profile.StandingsRules is not null)
            store.StandingsRules = profile.StandingsRules;

        if (profile.FuelConfig is not null)
            store.FuelConfig = profile.FuelConfig;

        if (profile.AppearanceOverrides is not null)
        {
            foreach (var (widgetKey, appearance) in profile.AppearanceOverrides)
                store.AppearanceOverrides[widgetKey] = appearance;
        }
    }

    private static bool TryParseHexColor(string hex, out Vortice.Win32.Numerics.Color4 color)
    {
        color = default;
        var span = hex.AsSpan().Trim().TrimStart('#');
        if (span.Length != 6) return false;
        try
        {
            byte r = byte.Parse(span[..2], System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(span.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(span.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
            color = new Vortice.Win32.Numerics.Color4(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Atomic write: serializes to a temp file in the same directory, then renames over
    /// the real path. A crash or power loss mid-write leaves either the old file or the fully-
    /// written new one, never a half-written one (spec §3: "gravação atômica").</summary>
    public static void Save(WidgetPlacementStore store, string? path = null)
    {
        string filePath = path ?? DefaultPath;
        try
        {
            var profile = new PlacementProfile(
                PlacementProfile.CurrentSchemaVersion,
                store.All.ToDictionary(kv => kv.Key, kv => kv.Value),
                store.FuelRelativeLink.Enabled,
                store.FuelRelativeLink.SpacingDip,
                new Dictionary<string, string>(store.ClassColorOverrides, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, List<ColumnDefinition>>(store.ColumnOverrides, StringComparer.OrdinalIgnoreCase),
                store.StandingsRules,
                store.FuelConfig,
                new Dictionary<string, WidgetAppearance>(store.AppearanceOverrides, StringComparer.OrdinalIgnoreCase));

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(profile, Options));
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort, matching V2's WidgetLayoutStore.Save() -- a failed save must never take
            // the overlay down; it just means this edit won't survive a restart.
        }
    }

    /// <summary>Spec §12: "importar/exportar" -- copies the current on-disk profile to a
    /// user-chosen path. Exports whatever was last <see cref="Save"/>d, not the in-memory store
    /// directly, so what's exported is guaranteed to be exactly what a re-import would produce.</summary>
    public static bool TryExport(string destinationPath, string? sourcePath = null)
    {
        try
        {
            string source = sourcePath ?? DefaultPath;
            if (!File.Exists(source)) return false;
            File.Copy(source, destinationPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Spec §12: validates the file is a recognized schema before touching the live store
    /// -- an invalid/foreign file is rejected outright, never partially applied.</summary>
    public static bool TryImport(WidgetPlacementStore store, string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return false;
            var profile = JsonSerializer.Deserialize<PlacementProfile>(File.ReadAllText(sourcePath), Options);
            if (profile is null || profile.SchemaVersion != PlacementProfile.CurrentSchemaVersion) return false;
            ApplyProfile(store, profile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }
}
