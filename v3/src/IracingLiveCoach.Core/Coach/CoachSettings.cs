using System;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.Core.Coach;

/// <summary>User-facing coach options, stored at %APPDATA%\iracing-live-coach\coach\coach-settings.json.
/// The Control Center writes it; the OverlayHost re-reads it when the file changes.</summary>
public sealed class CoachSettings
{
    public bool Enabled { get; set; } = true;
    public SkillLevel Skill { get; set; } = SkillLevel.Intermediate;
    public string Language { get; set; } = "en";
    public bool VoiceEnabled { get; set; } = true;
    public bool CornerTips { get; set; } = true;
    public bool LapSummary { get; set; } = true;
    public bool BrakeTones { get; set; } = false;
    public bool VoiceCommands { get; set; } = false;
    /// <summary>Race engineer master switch and its call families (fuel, flags/session, gaps/position/laps, pit).</summary>
    public bool EngineerEnabled { get; set; } = true;
    public bool FuelCalls { get; set; } = true;
    public bool FlagCalls { get; set; } = true;
    public bool GapCalls { get; set; } = true;
    public bool PitCalls { get; set; } = true;
    public int VoiceVolume { get; set; } = 80;
    public int ToneVolume { get; set; } = 60;
    public int VoiceRate { get; set; } = 0;
    public string? VoiceName { get; set; }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "coach");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "coach-settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Never fails: defaults when the file is missing or unreadable.</summary>
    public static CoachSettings Load(string? path = null) => TryLoad(path) ?? new CoachSettings();

    /// <summary>Defaults when the file does not exist; null when it exists but cannot be read or parsed
    /// (locked mid-write, invalid JSON...), so a caller can keep its current settings and retry later.</summary>
    public static CoachSettings? TryLoad(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new CoachSettings();
            return JsonSerializer.Deserialize<CoachSettings>(File.ReadAllText(path), Json);
        }
        catch (FileNotFoundException) { return new CoachSettings(); }
        catch (DirectoryNotFoundException) { return new CoachSettings(); }
        catch { return null; }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
