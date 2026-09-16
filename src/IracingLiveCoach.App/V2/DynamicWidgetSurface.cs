using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IracingLiveCoach.App;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using FlowDirection = System.Windows.FlowDirection;

namespace IracingLiveCoach.V2;

/// <summary>
/// Lightweight retained drawing surface for the two widgets that move while driving.
///
/// The first implementation of these widgets used an ItemsControl (one WPF visual per radar
/// contact) and animated ProgressBars.  On a layered transparent window that turns a small
/// telemetry update into a full layout and animation pass.  This surface has one visual only and
/// paints the latest telemetry directly during WPF's composition pass.
/// </summary>
public sealed class DynamicWidgetSurface : FrameworkElement
{
    public static readonly DependencyProperty WidgetProperty = DependencyProperty.Register(
        nameof(Widget), typeof(WidgetProfile), typeof(DynamicWidgetSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RadarProperty = DependencyProperty.Register(
        nameof(Radar), typeof(RadarStatus), typeof(DynamicWidgetSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RaceStartProperty = DependencyProperty.Register(
        nameof(RaceStart), typeof(RaceStartStatus), typeof(DynamicWidgetSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public WidgetProfile? Widget { get => (WidgetProfile?)GetValue(WidgetProperty); set => SetValue(WidgetProperty, value); }
    public RadarStatus? Radar { get => (RadarStatus?)GetValue(RadarProperty); set => SetValue(RadarProperty, value); }
    public RaceStartStatus? RaceStart { get => (RaceStartStatus?)GetValue(RaceStartProperty); set => SetValue(RaceStartProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Widget?.IsRadar == true) DrawRadar(dc);
        else if (Widget?.IsStartHelper == true) DrawStartHelper(dc);
    }

    private void DrawRadar(DrawingContext dc)
    {
        var size = Math.Max(1d, Math.Min(ActualWidth, ActualHeight));
        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
        var outer = size * .48d;
        var thin = new Pen(Brush("#706AD6EF"), 1d);
        var faint = new Pen(Brush("#305A718C"), 1d);
        dc.DrawEllipse(null, thin, center, outer, outer);
        dc.DrawEllipse(null, new Pen(Brush("#306AD6EF"), 1d), center, outer * .65d, outer * .65d);
        dc.DrawLine(faint, new Point(center.X, center.Y - outer), new Point(center.X, center.Y + outer));
        dc.DrawLine(faint, new Point(center.X - outer, center.Y), new Point(center.X + outer, center.Y));

        var radar = Radar;
        if (radar is null) return;
        var range = Math.Max(15d, Widget?.RadarRange ?? 55d);
        var sideBySide = radar.BlindSpotLeft || radar.BlindSpotRight;
        var contacts = sideBySide
            ? radar.Blips.Where(blip => Math.Abs(blip.DistanceMeters) <= Math.Min(range, 15d)).OrderBy(blip => Math.Abs(blip.DistanceMeters)).Take(8)
            : Enumerable.Empty<RadarBlip>();

        foreach (var blip in contacts)
        {
            var normalized = Math.Clamp(blip.DistanceMeters / range, -1d, 1d);
            var y = center.Y - normalized * outer * .82d;
            var x = center.X;
            if (Math.Abs(blip.DistanceMeters) < 10d)
                x = radar.BlindSpotLeft && !radar.BlindSpotRight ? center.X - outer * .68d : radar.BlindSpotRight && !radar.BlindSpotLeft ? center.X + outer * .68d : center.X;
            var color = blip.DistanceMeters >= 0 ? "#FFFFB84A" : "#FFB649FF";
            dc.DrawEllipse(Brush(color), new Pen(Brush("#FF07101D"), 2), new Point(x, y), 7, 7);
            DrawText(dc, $"{blip.DistanceMeters:+0;-0;0}m", new Point(x, y + 9), 8, Brushes.White, TextAlignment.Center);
        }

        dc.DrawRoundedRectangle(Brush("#FF55DDF5"), null, new Rect(center.X - 15, center.Y - 22, 30, 44), 14, 14);
        DrawText(dc, "YOU", center, 7, Brush("#FF07101D"), TextAlignment.Center, true);
        var nearest = radar.Blips.OrderBy(blip => Math.Abs(blip.DistanceMeters)).FirstOrDefault();
        if (nearest is not null) DrawText(dc, $"{nearest.DistanceMeters:+0;-0;0} m", new Point(ActualWidth - 4, 4), 9, Brushes.White, TextAlignment.Right);
        var side = radar.BlindSpotLeft && radar.BlindSpotRight ? "BOTH SIDES" : radar.BlindSpotLeft ? "LEFT" : radar.BlindSpotRight ? "RIGHT" : "CLEAR";
        DrawText(dc, side, new Point(center.X, ActualHeight - 13), 9, Brush("#FFB5C2D4"), TextAlignment.Center);
    }

    private void DrawStartHelper(DrawingContext dc)
    {
        var start = RaceStart;
        var width = Math.Max(1d, ActualWidth);
        DrawText(dc, "START HELPER", new Point(0, 0), 11, Brushes.White, TextAlignment.Left, true);
        DrawText(dc, "LIVE", new Point(width, 0), 9, Brush("#FF69DDEC"), TextAlignment.Right, true);
        DrawMeter(dc, "CLUTCH", start?.ClutchPct ?? 0d, 22);
        DrawMeter(dc, "THROTTLE", start?.ThrottlePct ?? 0d, 46);
    }

    private void DrawMeter(DrawingContext dc, string label, double value, double top)
    {
        const double labelWidth = 78, valueWidth = 40, barHeight = 10;
        var barWidth = Math.Max(0d, ActualWidth - labelWidth - valueWidth - 4d);
        var bar = new Rect(labelWidth, top, barWidth, barHeight);
        var clamped = Math.Clamp(value, 0d, 100d);
        dc.DrawRoundedRectangle(Brush("#FF262D37"), null, bar, 2, 2);
        if (clamped > 0)
            dc.DrawRoundedRectangle(Brush("#FF00C66B"), null, new Rect(bar.X, bar.Y, bar.Width * clamped / 100d, bar.Height), 2, 2);
        DrawText(dc, label, new Point(0, top - 1), 9, Brush("#FFB5C2D4"), TextAlignment.Left, true);
        DrawText(dc, $"{clamped:0}%", new Point(ActualWidth, top - 1), 10, Brushes.White, TextAlignment.Right, true);
    }

    // "Satoshi" is bundled as a packed app resource (see App.xaml's OverlayFont resource), not
    // installed system-wide -- a bare FontFamily("Satoshi") looks it up in the system font table,
    // finds nothing, and silently falls back to the OS default. This is why Radar/Start Helper
    // (the two widgets drawn here instead of through XAML's own OverlayFont-styled TextBlocks)
    // lost their typography as soon as they moved to this direct-draw surface.
    private static readonly FontFamily SatoshiFont = new("pack://application:,,,/IracingLiveCoach.App;component/V2/Assets/Fonts/#Satoshi");

    private void DrawText(DrawingContext dc, string value, Point origin, double size, Brush brush, TextAlignment alignment, bool bold = false)
    {
        var face = new Typeface(SatoshiFont, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Medium, FontStretches.Normal);
        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = alignment
        };
        dc.DrawText(formatted, origin);
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
