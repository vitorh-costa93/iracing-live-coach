using Vortice.Win32;
using Vortice.Win32.Graphics.DirectWrite;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// ColeÃ§Ã£o de fontes prÃ³pria do DirectWrite, montada direto dos .ttf (nÃ£o depende de instalar nada no Windows).
/// Procura a pasta de fontes ao lado do exe (<c>fonts\</c>) e, em desenvolvimento, em <c>ams2\fonts</c> subindo a Ã¡rvore.
/// </summary>
public sealed unsafe class FontLibrary : IDisposable
{
    ComPtr<IDWriteFontCollection1> _collection;
    public IDWriteFontCollection1* Collection => _collection.Get();
    public IReadOnlyList<string> Families { get; private set; } = [];
    public string? Directory { get; private set; }

    /// <summary>Pastas das fontes originais do GPxPatch carregadas em runtime (vazio = nenhuma encontrada ou desligada).</summary>
    public IReadOnlyList<string> OriginalDirectories { get; private set; } = [];
    public string? OriginalDirectory => OriginalDirectories.Count > 0 ? OriginalDirectories[0] : null;

    /// <summary>Instalacao padrao do GPxPatch (GP3).</summary>
    public const string DefaultGpxFontDirectory = @"C:\Microprose\Grand Prix 3\gpxpdata\gfx\fonts";

    /// <summary>
    /// Pastas das fontes do GPxPatch, em ordem de prioridade: AMS2_GPX_FONTS (lista separada por ';'; "off" desliga), a pasta local
    /// fonts\gpxpatch (ao lado do exe ou ams2\fonts\gpxpatch no repo, fora do git) e a instalacao padrao do GP3.
    /// </summary>
    public static IReadOnlyList<string> FindOriginalFontDirectories(string? themeFontDirectory = null)
    {
        string? env = Environment.GetEnvironmentVariable("AMS2_GPX_FONTS");
        if (string.Equals(env, "off", StringComparison.OrdinalIgnoreCase)) return [];
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(env)) candidates.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "fonts", "gpxpatch"));
        if (themeFontDirectory is not null) candidates.Add(Path.Combine(themeFontDirectory, "gpxpatch"));
        candidates.Add(DefaultGpxFontDirectory);
        return candidates.Where(d => !string.IsNullOrWhiteSpace(d) && System.IO.Directory.Exists(d) && System.IO.Directory.GetFiles(d!, "*.ttf").Length > 0)
            .Select(d => Path.GetFullPath(d!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? FindFontDirectory()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "fonts");
        if (System.IO.Directory.Exists(local) && System.IO.Directory.GetFiles(local, "*.ttf").Length > 0) return local;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string c = Path.Combine(dir.FullName, "ams2", "fonts");
            if (System.IO.Directory.Exists(c)) return c;
            c = Path.Combine(dir.FullName, "fonts");
            if (dir.Name == "ams2" && System.IO.Directory.Exists(c)) return c;
        }
        return null;
    }

    public static FontLibrary Build(IDWriteFactory* factory)
    {
        var lib = new FontLibrary();
        try { lib.Initialize(factory); }
        catch (Exception ex) { Console.Error.WriteLine($"[Fonts] coleÃ§Ã£o prÃ³pria falhou ({ex.GetType().Name}: {ex.Message}); usando fontes do sistema."); }
        return lib;
    }

    void Initialize(IDWriteFactory* factory)
    {
        Directory = FindFontDirectory();
        if (Directory is null) Console.Error.WriteLine("[Fonts] pasta de fontes nÃ£o encontrada.");
        // Fontes originais do GPxPatch (terceiros): fonte primaria dos modos GPxSAT, lidas em runtime; a copia local fica fora do git.
        OriginalDirectories = FindOriginalFontDirectories(Directory);
        var files = new List<string>();
        if (Directory is not null) files.AddRange(System.IO.Directory.GetFiles(Directory, "*.ttf"));
        // Mesmo arquivo em mais de uma pasta: vale o da pasta de maior prioridade.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in OriginalDirectories)
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.ttf"))
                if (seen.Add(Path.GetFileName(f))) files.Add(f);
        if (files.Count == 0) return;

        ComPtr<IDWriteFactory3> factory3 = default;
        if (factory->QueryInterface(__uuidof<IDWriteFactory3>(), (void**)factory3.GetAddressOf()).Failure)
        {
            Console.Error.WriteLine("[Fonts] IDWriteFactory3 indisponÃ­vel.");
            return;
        }
        using var f3 = factory3;
        using ComPtr<IDWriteFontSetBuilder> builder = default;
        ThrowIfFailed(f3.Get()->CreateFontSetBuilder(builder.GetAddressOf()));

        int added = 0;
        foreach (string path in files)
        {
            using ComPtr<IDWriteFontFaceReference> faceRef = default;
            fixed (char* p = path)
            {
                var hr = f3.Get()->CreateFontFaceReference(p, null, 0, FontSimulations.None, faceRef.GetAddressOf());
                if (hr.Failure) { Console.Error.WriteLine($"[Fonts] {Path.GetFileName(path)}: {hr}"); continue; }
            }
            ThrowIfFailed(builder.Get()->AddFontFaceReference(faceRef.Get()));
            added++;
        }
        if (added == 0) return;

        using ComPtr<IDWriteFontSet> fontSet = default;
        ThrowIfFailed(builder.Get()->CreateFontSet(fontSet.GetAddressOf()));
        ComPtr<IDWriteFontCollection1> collection = default;
        ThrowIfFailed(f3.Get()->CreateFontCollectionFromFontSet(fontSet.Get(), collection.GetAddressOf()));
        _collection = collection;

        var names = new List<string>();
        uint count = collection.Get()->GetFontFamilyCount();
        for (uint i = 0; i < count; i++)
        {
            using ComPtr<IDWriteFontFamily> fam = default;
            if (collection.Get()->GetFontFamily(i, fam.GetAddressOf()).Failure) continue;
            using ComPtr<IDWriteLocalizedStrings> ls = default;
            if (fam.Get()->GetFamilyNames(ls.GetAddressOf()).Failure) continue;
            uint len = 0;
            if (ls.Get()->GetStringLength(0, &len).Failure) continue;
            var buf = new char[len + 1];
            fixed (char* pb = buf)
                if (ls.Get()->GetString(0, pb, len + 1).Success) names.Add(new string(buf, 0, (int)len));
        }
        Families = names;
    }

    public bool Has(string family) => Families.Contains(family);

    readonly Dictionary<string, bool> _system = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>true se a famÃ­lia estÃ¡ instalada no Windows (coleÃ§Ã£o do sistema). Usado pelo tema 2018 (Verdana) e pela fonte escolhida no Control Center.</summary>
    public bool SystemHas(IDWriteFactory* factory, string family)
    {
        if (_system.TryGetValue(family, out var known)) return known;
        bool exists = false;
        try
        {
            using ComPtr<IDWriteFontCollection> sys = default;
            if (factory->GetSystemFontCollection(sys.GetAddressOf(), false).Success)
            {
                uint index; Bool32 ok;
                fixed (char* p = family)
                    if (sys.Get()->FindFamilyName(p, &index, &ok).Success) exists = ok;
            }
        }
        catch (Exception) { exists = false; }
        _system[family] = exists;
        return exists;
    }

    public void Dispose() => _collection.Dispose();
}
