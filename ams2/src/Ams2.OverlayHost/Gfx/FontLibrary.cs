using Vortice.Win32;
using Vortice.Win32.Graphics.DirectWrite;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Coleção de fontes própria do DirectWrite, montada direto dos .ttf (não depende de instalar nada no Windows).
/// Procura a pasta de fontes ao lado do exe (<c>fonts\</c>) e, em desenvolvimento, em <c>ams2\fonts</c> subindo a árvore.
/// </summary>
public sealed unsafe class FontLibrary : IDisposable
{
    ComPtr<IDWriteFontCollection1> _collection;
    public IDWriteFontCollection1* Collection => _collection.Get();
    public IReadOnlyList<string> Families { get; private set; } = [];
    public string? Directory { get; private set; }

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
        catch (Exception ex) { Console.Error.WriteLine($"[Fonts] coleção própria falhou ({ex.GetType().Name}: {ex.Message}); usando fontes do sistema."); }
        return lib;
    }

    void Initialize(IDWriteFactory* factory)
    {
        Directory = FindFontDirectory();
        if (Directory is null) { Console.Error.WriteLine("[Fonts] pasta de fontes não encontrada."); return; }

        ComPtr<IDWriteFactory3> factory3 = default;
        if (factory->QueryInterface(__uuidof<IDWriteFactory3>(), (void**)factory3.GetAddressOf()).Failure)
        {
            Console.Error.WriteLine("[Fonts] IDWriteFactory3 indisponível.");
            return;
        }
        using var f3 = factory3;
        using ComPtr<IDWriteFontSetBuilder> builder = default;
        ThrowIfFailed(f3.Get()->CreateFontSetBuilder(builder.GetAddressOf()));

        int added = 0;
        foreach (string path in System.IO.Directory.GetFiles(Directory, "*.ttf"))
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

    public void Dispose() => _collection.Dispose();
}
