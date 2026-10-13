using Ams2.Shared.Liveries;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Bandeiras de pais (PNGs ISO alpha-2 em Assets\Flags ao lado do exe, os mesmos do V3), decodificadas via WIC sob demanda e guardadas como
/// bitmaps D2D do device. Pais sem PNG (ou invalido) devolvem null: o chamador nao desenha nada, nunca um codigo ou emoji.
/// </summary>
public sealed unsafe class FlagBitmaps : IDisposable
{
    static readonly Guid WicImagingFactoryClsid = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    static readonly Guid WicPixelFormat32bppPbgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");

    readonly DeviceResources _gfx;
    readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _bitmaps = new(StringComparer.Ordinal);
    readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    ComPtr<IWICImagingFactory> _wic;
    int _generation;

    public FlagBitmaps(DeviceResources gfx) { _gfx = gfx; _generation = gfx.Generation; }

    /// <summary>Caminho do PNG de <paramref name="country"/> (alpha-3 do jogo ou alpha-2), ou null se o pais e desconhecido.</summary>
    public static string? PathFor(string? country)
    {
        string? a2 = CountryCodes.ToAlpha2(country);
        return a2 is null ? null : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", a2 + ".png");
    }

    /// <summary>Bitmap da bandeira ou null; valido ate o proximo Find (nao liberar).</summary>
    public ID2D1Bitmap* Find(string? country)
    {
        string? a2 = CountryCodes.ToAlpha2(country);
        if (a2 is null) return null;
        if (_generation != _gfx.Generation) { Release(); _generation = _gfx.Generation; }
        if (_bitmaps.TryGetValue(a2, out var cached)) return cached.Get();
        if (_missing.Contains(a2)) return null;
        string path = PathFor(a2)!;
        if (!File.Exists(path)) { _missing.Add(a2); return null; }
        try
        {
            var loaded = Load(path);
            _bitmaps[a2] = loaded;
            return loaded.Get();
        }
        catch { _missing.Add(a2); return null; }
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
