using System.IO;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// Named layout profiles: snapshots of the shared profile file (<c>v3-layout.json</c>) kept under
/// <c>profiles\</c>. The live file is always what the overlay reads; a named profile is a copy that can
/// be activated (copied over the live file, then the overlay reloads it) or refreshed by "Salvar".
/// </summary>
public static class ProfileFiles
{
    private const string ReservedPrefix = "_";

    public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach");
    public static string Root => Path.Combine(DataDir, "profiles");
    public static string LiveFile => Path.Combine(DataDir, "v3-layout.json");
    private static string ActiveFile => Path.Combine(Root, "_active.txt");

    public static string PathFor(string name) => Path.Combine(Root, Sanitize(name) + ".json");

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.TrimStart('_');
    }

    /// <summary>User profiles (names starting with '_' are internal backups and stay hidden).</summary>
    public static List<string> List()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.GetFiles(Root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !n.StartsWith(ReservedPrefix))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? ActiveName
    {
        get
        {
            try { return File.Exists(ActiveFile) ? File.ReadAllText(ActiveFile).Trim() is { Length: > 0 } n && File.Exists(PathFor(n)) ? n : null : null; }
            catch (IOException) { return null; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Root);
                if (string.IsNullOrEmpty(value)) File.Delete(ActiveFile);
                else File.WriteAllText(ActiveFile, value);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Copies the live layout file into <c>profiles\{name}.json</c> (creating or refreshing it).</summary>
    public static bool Save(string name)
    {
        try
        {
            if (Sanitize(name).Length == 0 || !File.Exists(LiveFile)) return false;
            Directory.CreateDirectory(Root);
            File.Copy(LiveFile, PathFor(name), overwrite: true);
            ActiveName = Sanitize(name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Copies the named profile over the live layout file and remembers it as the active one.</summary>
    public static bool Activate(string name)
    {
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return false;
            File.Copy(path, LiveFile, overwrite: true);
            ActiveName = Sanitize(name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Safety net before anything replaces the live layout: keeps the previous one as <c>_anterior.json</c>.</summary>
    public static void BackupCurrent()
    {
        try
        {
            if (!File.Exists(LiveFile)) return;
            Directory.CreateDirectory(Root);
            File.Copy(LiveFile, Path.Combine(Root, "_anterior.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static bool Delete(string name)
    {
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            if (string.Equals(ActiveName, Sanitize(name), StringComparison.OrdinalIgnoreCase)) ActiveName = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool Duplicate(string name, string copyName)
    {
        try
        {
            if (Sanitize(copyName).Length == 0 || File.Exists(PathFor(copyName)) || !File.Exists(PathFor(name))) return false;
            File.Copy(PathFor(name), PathFor(copyName));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
