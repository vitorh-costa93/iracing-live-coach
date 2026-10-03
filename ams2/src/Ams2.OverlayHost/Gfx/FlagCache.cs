using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Bandeiras PNG (Assets\Flags\xx.png) carregadas sob demanda via WIC para bitmaps D2D do device atual
/// (mesma abordagem do V3). Pais sem imagem devolve null: a celula fica vazia, nunca texto.
/// A vida acompanha o <see cref="DeviceResources"/>: ao recuperar de device lost o cache e recriado.
/// </summary>
public sealed unsafe class FlagCache : IDisposable
{
    static readonly Guid WicImagingFactoryClsid = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    static readonly Guid WicPixelFormat32bppPbgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");

    readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _bitmaps = new(StringComparer.Ordinal);
    readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    readonly ID2D1DeviceContext* _dc;
    ComPtr<IWICImagingFactory> _wic;

    public FlagCache(ID2D1DeviceContext* dc)
    {
        _dc = dc;
        ComPtr<IWICImagingFactory> factory = default;
        var clsid = WicImagingFactoryClsid;
        ThrowIfFailed(CoCreateInstance(&clsid, null, 0x1u, __uuidof<IWICImagingFactory>(), (void**)factory.GetAddressOf()));
        _wic = factory;
    }

    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Flags");

    public ID2D1Bitmap* Find(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        string key = iso.Trim().ToLowerInvariant();
        if (key.Any(ch => !(char.IsAsciiLetterLower(ch) || ch == '-'))) return null; // so "br", "gb-eng"... (evita path traversal)
        if (_bitmaps.TryGetValue(key, out var cached)) return cached.Get();
        if (_missing.Contains(key)) return null;
        string path = Path.Combine(AssetsDirectory, key + ".png");
        if (!File.Exists(path)) { _missing.Add(key); return null; }
        try { var b = Load(path); _bitmaps[key] = b; return b.Get(); }
        catch { _missing.Add(key); return null; }
    }

    ComPtr<ID2D1Bitmap> Load(string path)
    {
        ComPtr<IWICBitmapDecoder> decoder = default;
        fixed (char* filename = path)
            ThrowIfFailed(_wic.Get()->CreateDecoderFromFilename(filename, null, NativeFileAccess.GenericRead, WICDecodeOptions.CacheOnLoad, decoder.GetAddressOf()));
        using var _d = decoder;
        using ComPtr<IWICBitmapFrameDecode> frame = default;
        ThrowIfFailed(decoder.Get()->GetFrame(0, frame.GetAddressOf()));
        using ComPtr<IWICFormatConverter> converter = default;
        ThrowIfFailed(_wic.Get()->CreateFormatConverter(converter.GetAddressOf()));
        var format = WicPixelFormat32bppPbgra;
        ThrowIfFailed(converter.Get()->Initialize((IWICBitmapSource*)frame.Get(), &format, WICBitmapDitherType.None, null, 0, WICBitmapPaletteType.Custom));
        ComPtr<ID2D1Bitmap> bitmap = default;
        ThrowIfFailed(_dc->CreateBitmapFromWicBitmap((IWICBitmapSource*)converter.Get(), null, bitmap.GetAddressOf()));
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _wic.Dispose();
    }
}
