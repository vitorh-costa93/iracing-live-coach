using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IracingLiveCoach.App;

/// <summary>Normalize transparent padding at render time, preserving the original logo assets.</summary>
internal static class BrandImageLoader
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    public static ImageSource? Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        var bitmap = new BitmapImage(new Uri(path));
        var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var bytes = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(bytes, stride, 0);
        int left = pixels.PixelWidth, top = pixels.PixelHeight, right = -1, bottom = -1;
        for (int y = 0; y < pixels.PixelHeight; y++)
        for (int x = 0; x < pixels.PixelWidth; x++)
        {
            if (bytes[y * stride + x * 4 + 3] < 48) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        BitmapSource result = right >= left && bottom >= top
            ? new CroppedBitmap(pixels, new Int32Rect(left, top, right - left + 1, bottom - top + 1)) : bitmap;
        result.Freeze();
        Cache[path] = result;
        return result;
    }
}
