using System.Text.Json;

namespace IracingLiveCoach.V2;

public static class ProfileStore
{
    // A dedicated override keeps UI smoke runs isolated from a driver's persisted layout. Normal
    // production launches continue to use the standard roaming-AppData profile unchanged.
    private static string PathName => Environment.GetEnvironmentVariable("IRACING_LIVE_COACH_PROFILE_PATH")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "v2-profile.json");
    public static OverlayProfile Load()
    {
        try { return JsonSerializer.Deserialize<OverlayProfile>(System.IO.File.ReadAllText(PathName)) ?? OverlayProfile.CreateDefault(); }
        catch { return OverlayProfile.CreateDefault(); }
    }
    public static void Save(OverlayProfile profile)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
        System.IO.File.WriteAllText(PathName, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }
}
