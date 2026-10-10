using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ams2.Shared.Liveries;

/// <summary>Uma pintura do AMS2: o piloto/pais vem de UserData/CustomAIDrivers, a equipe da pasta da textura BODY em
/// Vehicles/Textures/CustomLiveries/Overrides. O jogo nao informa nada disso pela memoria compartilhada.</summary>
public sealed record LiveryEntry(string Livery, string Driver, string Country, string Team, string Model)
{
    /// <summary>Texto usado na busca do seletor.</summary>
    public string SearchText => $"{Livery} {Driver} {Country} {Team} {Model}";
    /// <summary>Classe do jogo (ex.: "F-V8_Gen1"), do arquivo de pilotos de IA que cita a pintura; "" se nenhum cita.</summary>
    public string Class { get; init; } = "";
}

/// <summary>
/// Catalogo de pinturas lido dos XMLs do jogo (somente leitura). Pinturas dos Overrides ganham equipe e modelo (nome da pasta);
/// pilotos de CustomAIDrivers sem override entram com o modelo = classe (nome do arquivo sem sufixo de campeonato).
/// </summary>
public static class LiveryCatalog
{
    const string GameFolder = "Automobilista 2";

    static readonly Regex NumberSuffix = new(@"\s*#\d+\s*$", RegexOptions.Compiled);

    /// <summary>Raiz do jogo: <paramref name="saved"/> se valido, senao a Steam (registro, pasta padrao e bibliotecas extras).</summary>
    public static string? FindGameRoot(string? saved = null)
    {
        if (IsGameRoot(saved)) return saved;
        var libs = new List<string>();
        foreach (var steam in SteamRoots())
        {
            libs.Add(steam);
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)"""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
        }
        foreach (var lib in libs)
        {
            string g = Path.Combine(lib, "steamapps", "common", GameFolder);
            if (IsGameRoot(g)) return g;
        }
        return null;
    }

    public static bool IsGameRoot(string? dir)
        => !string.IsNullOrWhiteSpace(dir) && (Directory.Exists(Path.Combine(dir, "UserData", "CustomAIDrivers")) || Directory.Exists(OverridesDir(dir)));

    static string OverridesDir(string root) => Path.Combine(root, "Vehicles", "Textures", "CustomLiveries", "Overrides");

    static IEnumerable<string> SteamRoots()
    {
        var found = new List<string>();
        try
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p && p.Length > 0) found.Add(p.Replace('/', '\\'));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            if (pf.Length > 0) found.Add(Path.Combine(pf, "Steam"));
        return found.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Le o catalogo de <paramref name="gameRoot"/>; arquivos ilegiveis ou malformados sao ignorados. Ordenado por piloto.</summary>
    public static List<LiveryEntry> Load(string gameRoot)
    {
        var drivers = new Dictionary<string, (string Name, string Country, string Class)>(StringComparer.OrdinalIgnoreCase);
        string aiDir = Path.Combine(gameRoot, "UserData", "CustomAIDrivers");
        if (Directory.Exists(aiDir))
            foreach (var file in Directory.EnumerateFiles(aiDir, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var doc = TryLoad(file);
                if (doc is null) continue;
                string cls = ClassOf(Path.GetFileNameWithoutExtension(file));
                foreach (var d in doc.Descendants("driver"))
                {
                    string live = (string?)d.Attribute("livery_name") ?? "";
                    string name = ((string?)d.Element("name") ?? "").Trim();
                    if (live.Length == 0 || name.Length == 0) continue;
                    drivers.TryAdd(live.Trim(), (name, ((string?)d.Element("country") ?? "").Trim(), cls));
                }
            }

        var result = new Dictionary<(string, string), LiveryEntry>();
        string ovDir = OverridesDir(gameRoot);
        if (Directory.Exists(ovDir))
            foreach (var dir in Directory.EnumerateDirectories(ovDir))
            {
                string model = Path.GetFileName(dir);
                foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
                {
                    var doc = TryLoad(file);
                    if (doc is null) continue;
                    foreach (var lo in doc.Descendants("LIVERY_OVERRIDE"))
                    {
                        string live = ((string?)lo.Attribute("NAME") ?? "").Trim();
                        if (live.Length == 0) continue;
                        string team = TeamOf(lo);
                        drivers.TryGetValue(live, out var ai);
                        string driver = ai.Name ?? NumberSuffix.Replace(live, "").Trim();
                        result.TryAdd((model, live), new LiveryEntry(live, driver, ai.Country ?? "", team, model) { Class = ai.Class ?? "" });
                    }
                }
            }
        foreach (var (live, ai) in drivers)
            if (!result.Keys.Any(k => string.Equals(k.Item2, live, StringComparison.OrdinalIgnoreCase)))
                result[(ai.Class, live)] = new LiveryEntry(live, ai.Name, ai.Country, "", ai.Class) { Class = ai.Class };

        return result.Values.OrderBy(e => e.Driver, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Model, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Filtro do seletor: todos os termos (separados por espaco) presentes em qualquer coluna, sem diferenciar caixa.</summary>
    public static IEnumerable<LiveryEntry> Filter(IEnumerable<LiveryEntry> all, string? query)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terms.Length == 0 ? all : all.Where(e => terms.All(t => e.SearchText.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Equipe = pasta que contem a textura BODY (ex.: "...\McLaren\Livery X.dds" -> "McLaren"); vazio se nao der para deduzir.</summary>
    static string TeamOf(XElement liveryOverride)
    {
        string? path = liveryOverride.Elements("TEXTURE").FirstOrDefault(t => string.Equals((string?)t.Attribute("NAME"), "BODY", StringComparison.OrdinalIgnoreCase))?.Attribute("PATH")?.Value
                       ?? liveryOverride.Element("PREVIEWIMAGE")?.Attribute("PATH")?.Value;
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2].Trim() : "";
    }

    /// <summary>"F-V8_Gen1_2006_Equal" -> "F-V8_Gen1": a classe e o que vem antes do primeiro "_" seguido de ano ou de nivel.</summary>
    static string ClassOf(string file)
    {
        var m = Regex.Match(file, @"^(?<c>.+?)(_\d{4}.*|_Equal|_Realistic)?$");
        return m.Success ? m.Groups["c"].Value : file;
    }

    static XDocument? TryLoad(string file)
    {
        try { return XDocument.Load(file); }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException) { return null; }
    }
}
