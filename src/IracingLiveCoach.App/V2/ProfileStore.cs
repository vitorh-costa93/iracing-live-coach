using System.Text.Json;

namespace IracingLiveCoach.V2;

public static class ProfileStore
{
    private static readonly string PathName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "v2-profile.json");
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
