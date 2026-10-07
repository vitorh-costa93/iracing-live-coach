using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

/// <summary>
/// Perfis em JSON: <c>{raiz}\profiles\{tema}\{nome}.json</c>, mais <c>{raiz}\state.json</c> com o tema e o perfil ativo por tema.
/// Raiz padrao: %AppData%\ams2-live-coach. Escrita atomica (arquivo temporario + troca). Host e Control Center usam esta mesma classe.
/// </summary>
public sealed class ProfileStore
{
    public const string DefaultProfileName = "Padrão";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record StateFile(string ActiveTheme, Dictionary<string, string> ActiveProfiles);

    readonly object _gate = new();
    public string Root { get; }

    public ProfileStore(string? root = null)
        => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ams2-live-coach");

    string ThemeDir(string themeId) { EnsureMigrated(); return Path.Combine(Root, "profiles", Sanitize(ThemeCatalog.Canonical(themeId))); }
    string PathOf(string themeId, string name) => Path.Combine(ThemeDir(themeId), Sanitize(name) + ".json");
    string StatePath => Path.Combine(Root, "state.json");

    bool _migrated;

    /// <summary>
    /// Migracao de temas substituidos (<see cref="ThemeCatalog.LegacyIds"/>, p.ex. f1-2010s -> f1-2018), uma vez por instancia:
    /// os perfis da pasta antiga sao COPIADOS para a pasta nova (com o ThemeId trocado; um perfil que ja exista na nova nao e
    /// sobrescrito) e a pasta antiga fica intacta, apenas ignorada. O state.json troca o id antigo pelo novo (tema ativo e perfil ativo).
    /// </summary>
    void EnsureMigrated()
    {
        if (_migrated) return;
        _migrated = true;
        try
        {
            foreach (var (oldId, newId) in ThemeCatalog.LegacyIds)
            {
                var oldDir = Path.Combine(Root, "profiles", Sanitize(oldId));
                if (!Directory.Exists(oldDir)) continue;
                var newDir = Path.Combine(Root, "profiles", Sanitize(newId));
                foreach (var f in Directory.EnumerateFiles(oldDir, "*.json"))
                {
                    var dest = Path.Combine(newDir, Path.GetFileName(f));
                    if (File.Exists(dest)) continue;
                    var p = TryRead(f);
                    if (p is null) continue;
                    WriteAtomic(dest, JsonSerializer.Serialize(p with { ThemeId = newId }, Json));
                }
            }
            if (File.Exists(StatePath))
            {
                var s = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(StatePath), Json);
                if (s is not null)
                {
                    bool changed = s.ActiveTheme is { } at && ThemeCatalog.Canonical(at) != at;
                    var profiles = new Dictionary<string, string>();
                    foreach (var (k, v) in s.ActiveProfiles ?? [])
                    {
                        string nk = ThemeCatalog.Canonical(k);
                        if (nk != k) changed = true;
                        if (nk == k || !profiles.ContainsKey(nk)) profiles[nk] = v;   // o id novo ja registrado vence o antigo
                    }
                    if (changed) WriteState(new StateFile(ThemeCatalog.Canonical(s.ActiveTheme ?? ThemeCatalog.Default), profiles));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Nome de arquivo seguro: sem caracteres invalidos, sem pontos/espacos nas pontas.</summary>
    public static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return s.Length == 0 ? "perfil" : s.Length > 80 ? s[..80] : s;
    }

    public IReadOnlyList<string> List(string themeId)
    {
        lock (_gate)
        {
            var dir = ThemeDir(themeId);
            if (!Directory.Exists(dir)) return [];
            var names = new List<string>();
            foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            {
                var p = TryRead(f);
                if (p is not null) names.Add(p.Name);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }
    }

    public bool Exists(string themeId, string name) { lock (_gate) return File.Exists(PathOf(themeId, name)); }

    /// <summary>Le e normaliza o perfil; null se nao existir, corrompido ou de esquema mais novo.</summary>
    public Profile? Load(string themeId, string name, int screenWidth = 1920, int screenHeight = 1080)
    {
        lock (_gate) return TryRead(PathOf(themeId, name))?.Normalized(screenWidth, screenHeight);
    }

    static Profile? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json);
            if (p is null || p.SchemaVersion > Profile.CurrentSchemaVersion || string.IsNullOrWhiteSpace(p.Name)) return null;
            return p;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public void Save(Profile profile)
    {
        lock (_gate) WriteAtomic(PathOf(profile.ThemeId, profile.Name), JsonSerializer.Serialize(profile.Normalized(), Json));
    }

    public void Delete(string themeId, string name)
    {
        lock (_gate)
        {
            var path = PathOf(themeId, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public Profile Duplicate(string themeId, string name, string newName)
    {
        lock (_gate)
        {
            var src = Load(themeId, name) ?? throw new FileNotFoundException($"Perfil '{name}' nao existe.");
            if (Exists(themeId, newName)) throw new InvalidOperationException($"Ja existe um perfil '{newName}'.");
            var copy = src with { Name = newName };
            Save(copy);
            return copy;
        }
    }

    public Profile Rename(string themeId, string name, string newName)
    {
        lock (_gate)
        {
            if (string.Equals(name, newName, StringComparison.Ordinal)) return Load(themeId, name) ?? throw new FileNotFoundException(name);
            var copy = Duplicate(themeId, name, newName);
            Delete(themeId, name);
            var state = ReadState();
            string key = ThemeCatalog.Canonical(themeId);
            if (state.ActiveProfiles.TryGetValue(key, out var act) && act == name)
            {
                state.ActiveProfiles[key] = newName;
                WriteState(state);
            }
            return copy;
        }
    }

    /// <summary>Garante que o tema tenha ao menos um perfil e devolve a lista.</summary>
    public IReadOnlyList<string> EnsureDefault(string themeId, int screenWidth = 1920, int screenHeight = 1080)
    {
        lock (_gate)
        {
            if (List(themeId).Count == 0) Save(ProfileFactory.CreateDefault(DefaultProfileName, ThemeCatalog.Canonical(themeId), screenWidth, screenHeight));
            return List(themeId);
        }
    }

    StateFile ReadState()
    {
        EnsureMigrated();
        try
        {
            if (File.Exists(StatePath))
            {
                var s = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(StatePath), Json);
                if (s is not null) return new StateFile(ThemeCatalog.Canonical(s.ActiveTheme ?? ThemeCatalog.Default), s.ActiveProfiles ?? []);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new StateFile(ThemeCatalog.Default, []);
    }

    void WriteState(StateFile s) => WriteAtomic(StatePath, JsonSerializer.Serialize(s, Json));

    public string GetActiveTheme() { lock (_gate) return ReadState().ActiveTheme; }

    public void SetActiveTheme(string themeId)
    {
        lock (_gate) { var s = ReadState(); WriteState(s with { ActiveTheme = ThemeCatalog.Canonical(themeId) }); }
    }

    /// <summary>Perfil ativo do tema; se o registrado nao existe mais, cai no padrao/primeiro (criando o padrao se preciso).</summary>
    public string GetActiveProfile(string themeId, int screenWidth = 1920, int screenHeight = 1080)
    {
        lock (_gate)
        {
            var names = EnsureDefault(themeId, screenWidth, screenHeight);
            var s = ReadState();
            if (s.ActiveProfiles.TryGetValue(ThemeCatalog.Canonical(themeId), out var n) && names.Contains(n)) return n;
            return names.Contains(DefaultProfileName) ? DefaultProfileName : names[0];
        }
    }

    public void SetActiveProfile(string themeId, string name)
    {
        lock (_gate) { var s = ReadState(); s.ActiveProfiles[ThemeCatalog.Canonical(themeId)] = name; WriteState(s); }
    }

    void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
