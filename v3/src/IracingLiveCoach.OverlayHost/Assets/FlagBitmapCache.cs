using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Imaging;
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

        LoadKnownAssets(dc);
    }

    public ID2D1Bitmap* Find(string? flagEmoji) =>
        TryGetAssetKey(flagEmoji, out var key) && _flagBitmaps.TryGetValue(key, out var bitmap)
            ? bitmap.Get()
            : null;

    /// <summary>Returns an untinted transparent PNG logo for known manufacturers. Unknown or
    /// unsupported manufacturers intentionally return null: the caller may show text, but the
    /// renderer never invents a generic badge or substitutes a different make.</summary>
    public ID2D1Bitmap* FindBrand(string? manufacturer) =>
        TryGetBrandAssetKey(manufacturer, out var key) && _brandBitmaps.TryGetValue(key, out var bitmap)
            ? bitmap.Get()
            : null;

    /// <summary>Rebuilds only the device-dependent D2D bitmaps after DeviceResources recovered
    /// from a removed/reset GPU. The WIC decoder factory remains valid and no disk/catalog lookup
    /// is performed during normal frames.</summary>
    public void Recreate(ID2D1DeviceContext* dc)
    {
        ReleaseBitmaps();
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
        string flagsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags");
        foreach (string key in new[] { "br", "us", "jp" })
        {
            string path = Path.Combine(flagsDirectory, key + ".png");
            if (!File.Exists(path)) continue;
            _flagBitmaps[key] = LoadBitmap(dc, path);
        }

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
        "aston", "audi", "bmw", "cadillac", "chevrolet", "ferrari", "ford",
        "lamborghini", "mclaren", "mercedes", "porsche"
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

    private static bool TryGetAssetKey(string? emoji, out string key)
    {
        key = emoji switch
        {
            "🇧🇷" => "br",
            "🇺🇸" => "us",
            "🇯🇵" => "jp",
            _ => string.Empty
        };
        return key.Length != 0;
    }

    private static bool TryGetBrandAssetKey(string? manufacturer, out string key)
    {
        string normalized = manufacturer?.Trim().ToUpperInvariant() ?? string.Empty;
        key = normalized switch
        {
            var value when value.Contains("ASTON") => "aston",
            var value when value.Contains("AUDI") => "audi",
            var value when value.Contains("BMW") => "bmw",
            var value when value.Contains("CADILLAC") => "cadillac",
            var value when value.Contains("CHEVROLET") || value.Contains("CORVETTE") => "chevrolet",
            var value when value.Contains("FERRARI") => "ferrari",
            var value when value.Contains("FORD") => "ford",
            var value when value.Contains("LAMBORGHINI") => "lamborghini",
            var value when value.Contains("MCLAREN") || value.Contains("MC LAREN") => "mclaren",
            var value when value.Contains("MERCEDES") => "mercedes",
            var value when value.Contains("PORSCHE") => "porsche",
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
