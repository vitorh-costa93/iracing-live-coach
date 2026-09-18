using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Assets;

/// <summary>
/// Loads the existing V2 flag PNGs once through WIC and retains device-local Direct2D bitmaps.
/// Text/emoji are intentionally never used as a rendering fallback: a missing local asset simply
/// leaves the flag cell empty, which is preferable to showing a country code in the overlay.
/// </summary>
public sealed unsafe class FlagBitmapCache : IDisposable
{
    private static readonly Guid WicImagingFactoryClsid = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    private static readonly Guid WicPixelFormat32bppPbgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");

    private readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _flagBitmaps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComPtr<ID2D1Bitmap>> _brandBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private ComPtr<IWICImagingFactory> _wicFactory;

    public FlagBitmapCache(ID2D1DeviceContext* dc)
    {
        ComPtr<IWICImagingFactory> factory = default;
        var clsid = WicImagingFactoryClsid;
        // CLSCTX_INPROC_SERVER. The generated Win32 binding exposes this argument as uint.
        ThrowIfFailed(CoCreateInstance(&clsid, null, 0x1u,
            __uuidof<IWICImagingFactory>(), (void**)factory.GetAddressOf()));
        _wicFactory = factory;
        _dc = dc;

        LoadKnownAssets(dc);
    }

    private ID2D1DeviceContext* _dc;
    private readonly HashSet<string> _missingFlags = new(StringComparer.Ordinal);

    /// <summary>Returns the bundled flag image for a flag-emoji key produced by Core's CountryFlags
    /// (any country, plus the UK nations), loading its PNG on first use -- ~220 flags ship, but a
    /// race only ever needs a handful, so none are decoded up front. A country with no bundled
    /// image (or the globe fallback) simply returns null: the cell stays empty, never a code or an
    /// emoji glyph.</summary>
    public ID2D1Bitmap* Find(string? flagEmoji)
    {
        string? key = IracingLiveCoach.Core.CountryFlags.ToAssetKey(flagEmoji);
        if (key is null) return null;
        if (_flagBitmaps.TryGetValue(key, out var cached)) return cached.Get();
        if (_missingFlags.Contains(key) || _dc is null) return null;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", key + ".png");
        if (!File.Exists(path)) { _missingFlags.Add(key); return null; }
        try
        {
            var loaded = LoadBitmap(_dc, path);
            _flagBitmaps[key] = loaded;
            return loaded.Get();
        }
        catch { _missingFlags.Add(key); return null; }
    }

    /// <summary>Returns an untinted transparent PNG logo for known manufacturers. Unknown or
    /// unsupported manufacturers intentionally return null: the caller may show text, but the
    /// renderer never invents a generic badge or substitutes a different make.</summary>
    public ID2D1Bitmap* FindBrand(string? manufacturer) =>
        TryGetBrandAssetKey(manufacturer, out var key) && _brandBitmaps.TryGetValue(key, out var bitmap)
            ? bitmap.Get()
            : null;

    /// <summary>Fits <paramref name="bitmap"/> into <paramref name="box"/> preserving its own aspect
    /// ratio and centering it -- spec §18: "ajuste proporcional contain, centralizado verticalmente,
    /// sem deformar ou recortar". Every flag/brand DrawBitmap call must use this instead of stretching
    /// the source pixels directly into the box's own (usually differently-proportioned) rectangle.</summary>
    public static RectF Contain(ID2D1Bitmap* bitmap, RectF box)
    {
        var size = bitmap->GetSize();
        if (size.Width <= 0 || size.Height <= 0) return box;
        float boxWidth = box.Right - box.Left;
        float boxHeight = box.Bottom - box.Top;
        float scale = Math.Min(boxWidth / size.Width, boxHeight / size.Height);
        float fittedWidth = size.Width * scale;
        float fittedHeight = size.Height * scale;
        float offsetX = box.Left + (boxWidth - fittedWidth) / 2f;
        float offsetY = box.Top + (boxHeight - fittedHeight) / 2f;
        return new RectF(offsetX, offsetY, offsetX + fittedWidth, offsetY + fittedHeight);
    }

    /// <summary>Rebuilds only the device-dependent D2D bitmaps after DeviceResources recovered
    /// from a removed/reset GPU. The WIC decoder factory remains valid and no disk/catalog lookup
    /// is performed during normal frames.</summary>
    public void Recreate(ID2D1DeviceContext* dc)
    {
        ReleaseBitmaps();
        _dc = dc;
        _missingFlags.Clear();
        LoadKnownAssets(dc);
    }

    private void ReleaseBitmaps()
    {
        foreach (var bitmap in _flagBitmaps.Values) bitmap.Dispose();
        foreach (var bitmap in _brandBitmaps.Values) bitmap.Dispose();
        _flagBitmaps.Clear();
        _brandBitmaps.Clear();
    }

    private void LoadKnownAssets(ID2D1DeviceContext* dc)
    {
        string brandsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        foreach (string key in KnownBrandAssetKeys)
        {
            string path = Path.Combine(brandsDirectory, key + ".png");
            if (!File.Exists(path)) continue;
            _brandBitmaps[key] = LoadBitmap(dc, path);
        }
    }

    private static readonly string[] KnownBrandAssetKeys =
    [
        "acura", "aston", "audi", "bmw", "cadillac", "chevrolet", "dallara", "ferrari", "ford",
        "honda", "lamborghini", "mclaren", "mercedes", "porsche", "toyota"
    ];

    private ComPtr<ID2D1Bitmap> LoadBitmap(ID2D1DeviceContext* dc, string path)
    {
        ComPtr<IWICBitmapDecoder> decoder = default;
        fixed (char* filename = path)
        {
            ThrowIfFailed(_wicFactory.Get()->CreateDecoderFromFilename(
                filename, null, NativeFileAccess.GenericRead, WICDecodeOptions.CacheOnLoad,
                decoder.GetAddressOf()));
        }

        using ComPtr<IWICBitmapFrameDecode> frame = default;
        ThrowIfFailed(decoder.Get()->GetFrame(0, frame.GetAddressOf()));

        using ComPtr<IWICFormatConverter> converter = default;
        ThrowIfFailed(_wicFactory.Get()->CreateFormatConverter(converter.GetAddressOf()));
        var format = WicPixelFormat32bppPbgra;
        ThrowIfFailed(converter.Get()->Initialize((IWICBitmapSource*)frame.Get(), &format,
            WICBitmapDitherType.None, null, 0, WICBitmapPaletteType.Custom));

        ComPtr<ID2D1Bitmap> bitmap = default;
        ThrowIfFailed(dc->CreateBitmapFromWicBitmap((IWICBitmapSource*)converter.Get(), null,
            bitmap.GetAddressOf()));
        return bitmap;
    }

    private static bool TryGetBrandAssetKey(string? manufacturer, out string key)
    {
        string normalized = manufacturer?.Trim().ToUpperInvariant() ?? string.Empty;
        key = normalized switch
        {
            var value when value.Contains("ACURA") => "acura",
            var value when value.Contains("ASTON") => "aston",
            var value when value.Contains("AUDI") => "audi",
            var value when value.Contains("BMW") => "bmw",
            var value when value.Contains("CADILLAC") => "cadillac",
            var value when value.Contains("CHEVROLET") || value.Contains("CORVETTE") => "chevrolet",
            var value when value.Contains("DALLARA") => "dallara",
            var value when value.Contains("FERRARI") => "ferrari",
            var value when value.Contains("FORD") => "ford",
            var value when value.Contains("HONDA") => "honda",
            var value when value.Contains("LAMBORGHINI") => "lamborghini",
            var value when value.Contains("MCLAREN") || value.Contains("MC LAREN") => "mclaren",
            var value when value.Contains("MERCEDES") => "mercedes",
            var value when value.Contains("PORSCHE") => "porsche",
            var value when value.Contains("TOYOTA") => "toyota",
            _ => string.Empty
        };
        return key.Length != 0;
    }

    public void Dispose()
    {
        ReleaseBitmaps();
        _wicFactory.Dispose();
    }
}
