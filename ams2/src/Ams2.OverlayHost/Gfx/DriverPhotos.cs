using System.Globalization;
using System.Text;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Fotos dos pilotos (Assets\Drivers\&lt;chave&gt;.png|jpg) e silhuetas dos carros (Assets\Cars\&lt;chave&gt;.png) ao lado do exe, decodificadas via WIC
/// sob demanda e guardadas como bitmaps D2D (mesmo modelo do <see cref="FlagBitmaps"/>). Sem arquivo devolve null: o chamador desenha um
/// placeholder. As imagens sao fornecidas pelo usuario; o programa nao baixa nada.
/// </summary>
public sealed unsafe class DriverPhotos : IDisposable
{
    static readonly Guid WicImagingFactoryClsid = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    static readonly Guid WicPixelFormat32bppPbgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");
    static readonly string[] DriverExtensions = [".png", ".jpg", ".jpeg"];

    readonly DeviceResources _gfx;
    readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _bitmaps = new(StringComparer.Ordinal);
    readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    ComPtr<IWICImagingFactory> _wic;
    int _generation;

    public DriverPhotos(DeviceResources gfx) { _gfx = gfx; _generation = gfx.Generation; }

    /// <summary>
    /// Chave de arquivo: minusculas, sem acentos, cada sequencia de caracteres nao alfanumericos (ASCII) vira um unico '_', sem '_' nas pontas.
    /// "Ayrton Senna da Silva" -> "ayrton_senna_da_silva"; "Jean-Éric Vergne" -> "jean_eric_vergne"; vazio/so simbolos -> "".
    /// </summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool pending = false;
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            char lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pending && sb.Length > 0) sb.Append('_');
                pending = false;
                sb.Append(lower);
            }
            else pending = true;
        }
        return sb.ToString();
    }

    public static string DriversDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Drivers");
    public static string CarsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Cars");

    /// <summary>Caminho do arquivo da foto do piloto (png, jpg ou jpeg) ou null se nao existe.</summary>
    public static string? DriverPath(string? name)
    {
        string key = Key(name);
        if (key.Length == 0) return null;
        foreach (string ext in DriverExtensions)
        {
            string path = Path.Combine(DriversDirectory, key + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static string? CarPath(string? livery)
    {
        string key = Key(livery);
        if (key.Length == 0) return null;
        string path = Path.Combine(CarsDirectory, key + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Foto do piloto ou null; valida ate o proximo Find (nao liberar).</summary>
    public ID2D1Bitmap* FindDriver(string? name) => Find("d:" + Key(name), () => DriverPath(name));

    /// <summary>Silhueta do carro (chave = pintura/nome do carro) ou null.</summary>
    public ID2D1Bitmap* FindCar(string? livery) => Find("c:" + Key(livery), () => CarPath(livery));

    ID2D1Bitmap* Find(string cacheKey, Func<string?> locate)
    {
        if (cacheKey.Length <= 2) return null;
        if (_generation != _gfx.Generation) { Release(); _generation = _gfx.Generation; }
        if (_bitmaps.TryGetValue(cacheKey, out var cached)) return cached.Get();
        if (_missing.Contains(cacheKey)) return null;
        string? path = locate();
        if (path is null) { _missing.Add(cacheKey); return null; }
        try
        {
            var loaded = Load(path);
            _bitmaps[cacheKey] = loaded;
            return loaded.Get();
        }
        catch { _missing.Add(cacheKey); return null; }
    }

    ComPtr<ID2D1Bitmap> Load(string path)
    {
        if (_wic.Get() == null)
        {
            ComPtr<IWICImagingFactory> factory = default;
            var clsid = WicImagingFactoryClsid;
            ThrowIfFailed(CoCreateInstance(&clsid, null, 0x1u, __uuidof<IWICImagingFactory>(), (void**)factory.GetAddressOf()));
            _wic = factory;
        }
        ComPtr<IWICBitmapDecoder> decoder = default;
        fixed (char* filename = path)
            ThrowIfFailed(_wic.Get()->CreateDecoderFromFilename(filename, null, NativeFileAccess.GenericRead, WICDecodeOptions.CacheOnLoad, decoder.GetAddressOf()));
        using var decoderScope = decoder;
        using ComPtr<IWICBitmapFrameDecode> frame = default;
        ThrowIfFailed(decoder.Get()->GetFrame(0, frame.GetAddressOf()));
        using ComPtr<IWICFormatConverter> converter = default;
        ThrowIfFailed(_wic.Get()->CreateFormatConverter(converter.GetAddressOf()));
        var format = WicPixelFormat32bppPbgra;
        ThrowIfFailed(converter.Get()->Initialize((IWICBitmapSource*)frame.Get(), &format, WICBitmapDitherType.None, null, 0, WICBitmapPaletteType.Custom));
        ComPtr<ID2D1Bitmap> bitmap = default;
        ThrowIfFailed(_gfx.Context->CreateBitmapFromWicBitmap((IWICBitmapSource*)converter.Get(), null, bitmap.GetAddressOf()));
        return bitmap;
    }

    void Release()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _missing.Clear();
    }

    public void Dispose()
    {
        Release();
        _wic.Dispose();
    }
}
