using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Bandeiras de pais: os PNGs de Assets\Flags (os mesmos do V3) carregados sob demanda via WIC para bitmaps D2D do dispositivo atual.
/// Chave = <see cref="Ams2.Shared.Liveries.CountryKey.Of"/> ("br", "gb-eng"). Sem arquivo para o pais, <see cref="Find"/> devolve null:
/// a celula fica vazia (nunca sigla nem emoji). <see cref="Reset"/> descarta os bitmaps quando o dispositivo e recriado.
/// </summary>
public sealed unsafe class FlagBitmaps : IDisposable
{
    static readonly Guid WicImagingFactoryClsid = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    static readonly Guid WicPixelFormat32bppPbgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");

    readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _bitmaps = new(StringComparer.Ordinal);
    readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    ComPtr<IWICImagingFactory> _wic;

    /// <summary>Bitmap da bandeira (valido enquanto o dispositivo nao for recriado) ou null se nao houver PNG para a chave.</summary>
    public ID2D1Bitmap* Find(ID2D1DeviceContext* dc, string? key)
    {
        if (string.IsNullOrEmpty(key) || dc == null) return null;
        if (_bitmaps.TryGetValue(key, out var cached)) return cached.Get();
        if (_missing.Contains(key)) return null;
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", key + ".png");
        if (!File.Exists(path)) { _missing.Add(key); return null; }
        try
        {
            var loaded = Load(dc, path);
            _bitmaps[key] = loaded;
            return loaded.Get();
        }
        catch { _missing.Add(key); return null; }
    }

    /// <summary>Descarta os bitmaps do dispositivo anterior (o decodificador WIC continua valido).</summary>
    public void Reset()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _missing.Clear();
    }

    /// <summary>Encaixa o bitmap em <paramref name="box"/> sem deformar nem cortar, centralizado.</summary>
    public static RectF Contain(ID2D1Bitmap* bitmap, RectF box)
    {
        var size = bitmap->GetSize();
        if (size.Width <= 0 || size.Height <= 0) return box;
        float bw = box.Right - box.Left, bh = box.Bottom - box.Top;
        float scale = Math.Min(bw / size.Width, bh / size.Height);
        float fw = size.Width * scale, fh = size.Height * scale;
        float ox = box.Left + (bw - fw) / 2f, oy = box.Top + (bh - fh) / 2f;
        return new RectF(ox, oy, ox + fw, oy + fh);
    }

    ComPtr<ID2D1Bitmap> Load(ID2D1DeviceContext* dc, string path)
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
        {
            ThrowIfFailed(_wic.Get()->CreateDecoderFromFilename(filename, null, NativeFileAccess.GenericRead, WICDecodeOptions.CacheOnLoad, decoder.GetAddressOf()));
        }
        using ComPtr<IWICBitmapFrameDecode> frame = default;
        ThrowIfFailed(decoder.Get()->GetFrame(0, frame.GetAddressOf()));
        using ComPtr<IWICFormatConverter> converter = default;
        ThrowIfFailed(_wic.Get()->CreateFormatConverter(converter.GetAddressOf()));
        var format = WicPixelFormat32bppPbgra;
        ThrowIfFailed(converter.Get()->Initialize((IWICBitmapSource*)frame.Get(), &format, WICBitmapDitherType.None, null, 0, WICBitmapPaletteType.Custom));
        ComPtr<ID2D1Bitmap> bitmap = default;
        ThrowIfFailed(dc->CreateBitmapFromWicBitmap((IWICBitmapSource*)converter.Get(), null, bitmap.GetAddressOf()));
        decoder.Dispose();
        return bitmap;
    }

    public void Dispose()
    {
        Reset();
        _wic.Dispose();
    }
}
