using System.Globalization;
using IracingLiveCoach.Core.Coach;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>Text pieces of the Pit Rejoin widget (pure, unit-tested).</summary>
public static class PitRejoinText
{
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>"P5 → P9" (class order); practice: "Traffic at exit".</summary>
    public static string Headline(PitRejoinPrediction p) =>
        p.RaceOrder && p.PredictedPosition > 0 ? $"P{p.CurrentPosition} → P{p.PredictedPosition}" : "Traffic at exit";

    /// <summary>"Overall P12 → P18" for multiclass races, else empty.</summary>
    public static string Overall(PitRejoinPrediction p) =>
        p.RaceOrder && p.MultiClass && p.OverallPredictedPosition > 0 ? $"Overall P{p.OverallCurrentPosition} → P{p.OverallPredictedPosition}" : "";

    /// <summary>"#12 V. Costa" plus the lap difference ("+1L" / "-1L") when not on the same lap.</summary>
    public static string Car(RejoinNeighbour n)
    {
        string s = ("#" + n.Number + " " + n.Name).Trim();
        if (n.Laps != 0) s += n.Laps > 0 ? $" +{n.Laps}L" : $" {n.Laps}L";
        if (n.InPit) s += " (pit)";
        return s;
    }

    /// <summary>"+1.8s" ahead / "-3.1s" behind.</summary>
    public static string Gap(RejoinNeighbour n, bool ahead) => (ahead ? "+" : "-") + F1(n.GapSec) + "s";

    /// <summary>"Loss 31.4s (lane 22.9 + service 8.5)".</summary>
    public static string Loss(PitRejoinPrediction p)
    {
        if (p.InPit) return $"Remaining {F1(p.LossSec)}s";
        string s = $"Loss {F1(p.LossSec)}s";
        if (!double.IsNaN(p.LaneLossSec) && !double.IsNaN(p.ServiceSec)) s += $" (lane {F1(p.LaneLossSec)} + service {F1(p.ServiceSec)})";
        return s;
    }

    /// <summary>"Est: service from other cars' stops" (empty when learned).</summary>
    public static string Estimate(PitRejoinPrediction p) =>
        p.Estimated ? "Est: " + (string.IsNullOrEmpty(p.Note) ? "not learned yet" : p.Note) : "";

    public static string Unavailable(PitRejoinPrediction? p) =>
        "Pit rejoin: " + (p == null ? "waiting for data" : string.IsNullOrEmpty(p.Reason) ? "unavailable" : p.Reason);
}

/// <summary>
/// "Pit Rejoin": where the player comes out if they pit now -- class position now → after the stop, the
/// same-class cars just ahead/behind at the exit, and in multiclass races the nearest car of every other class
/// (faster classes coming from behind highlighted, slower traffic ahead), plus the pit loss used and whether
/// it is an estimate. Collapses to one line while there is no prediction.
/// </summary>
public sealed unsafe class PitRejoinWidget : IDisposable
{
    private readonly TelemetryReader? _telemetry;
    private readonly object _lock = new();
    private PitRejoinPrediction? _prediction;
    private PitRejoinPrediction? _simulated;

    public void SetSimulated(PitRejoinPrediction? prediction) => _simulated = prediction;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _bigFormat;
    private ComPtr<IDWriteTextFormat> _rowFormat;
    private ComPtr<IDWriteTextFormat> _smallFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 340f;
    private const float CollapsedHeightDip = 30f;
    private const int MaxOtherClasses = 3;
    private static readonly Color4 Amber = new(1.0f, 0.78f, 0.17f, 1f);

    public PitRejoinWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null, bool startTelemetry = true)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.PitRejoinUpdated += OnPitRejoin;
        if (startTelemetry) _telemetry.Start();
    }

    private void OnPitRejoin(PitRejoinPrediction p)
    {
        lock (_lock) { _prediction = p; }
    }

    private ComPtr<IDWriteTextFormat> MakeFormat(float size, FontWeight weight)
    {
        ComPtr<IDWriteTextFormat> format = FontCatalog.CreateFormat(_dwriteFactory, _fontCollection, _appearance.FontFamily, size * _appearance.FontScale, weight, _appearance.FontWeight);
        ThrowIfFailed(format.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(format.Get()->SetWordWrapping(WordWrapping.NoWrap));
        return format;
    }

    private void CreateTextFormats()
    {
        _labelFormat.Dispose();
        _bigFormat.Dispose();
        _rowFormat.Dispose();
        _smallFormat.Dispose();
        _labelFormat = MakeFormat(13f, FontWeight.SemiBold);
        _bigFormat = MakeFormat(26f, FontWeight.SemiBold);
        _rowFormat = MakeFormat(14f, FontWeight.Medium);
        _smallFormat = MakeFormat(12f, FontWeight.Medium);
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight || appearance.FontFamily != _appearance.FontFamily;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    public (float Width, float Height) LastDrawnSize { get; private set; } = (WidthDip, CollapsedHeightDip);

    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip, float height = CollapsedHeightDip)
    {
        PitRejoinPrediction? p;
        lock (_lock) { p = _simulated ?? _prediction; }
        float scale = Math.Max(0.5f, _appearance.FontScale);
        width = Math.Max(width, 260f);
        const float pad = 10f;

        if (p is not { Available: true })
        {
            float h = CollapsedHeightDip * scale;
            LastDrawnSize = (width, h);
            var small = new RectF(x, y, x + width, y + h);
            PanelChrome.FillPanel(dc, _brush.Get(), small, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));
            PanelChrome.DrawText(dc, _brush.Get(), _smallFormat.Get(), PitRejoinText.Unavailable(p), x + pad, y, width - 2 * pad, h, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), small, PaletteTokens.PanelBorder);
            return;
        }

        float headH = 20f * scale, bigH = 34f * scale, rowH = 20f * scale, footH = 18f * scale;
        var classes = p.OtherClasses.Take(MaxOtherClasses).ToList();
        int rows = (p.Ahead != null ? 1 : 0) + (p.Behind != null ? 1 : 0);
        string est = PitRejoinText.Estimate(p);
        float total = 6f + headH + bigH + rows * rowH + (classes.Count > 0 ? 6f + classes.Count * rowH : 0) + 6f + footH + (est.Length > 0 ? footH : 0) + 6f;
        LastDrawnSize = (width, total);

        var panel = new RectF(x, y, x + width, y + total);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));
        float cy = y + 6f;

        // header
        PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), p.InPit ? "PIT REJOIN - IN PIT" : "PIT REJOIN", x + pad, cy, width - 2 * pad, headH, PaletteTokens.TextSecondary);
        if (p.Estimated)
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "EST", x + pad, cy, width - 2 * pad, headH, Amber, TextAlignment.Trailing);
        cy += headH;

        // P5 -> P9 (+ overall on the right in multiclass)
        bool worse = p.RaceOrder && p.PredictedPosition > p.CurrentPosition;
        PanelChrome.DrawText(dc, _brush.Get(), _bigFormat.Get(), PitRejoinText.Headline(p), x + pad, cy, width - 2 * pad, bigH,
            p.RaceOrder ? (worse ? PaletteTokens.TextPrimary : PaletteTokens.PositiveDelta) : PaletteTokens.TextPrimary);
        string overall = PitRejoinText.Overall(p);
        if (overall.Length > 0)
            PanelChrome.DrawText(dc, _brush.Get(), _smallFormat.Get(), (p.ClassName.Length > 0 ? p.ClassName + " | " : "") + overall,
                x + pad, cy, width - 2 * pad, bigH, PaletteTokens.TextSecondary, TextAlignment.Trailing);
        cy += bigH;

        var ownColor = PaletteTokens.ResolveClassColor(p.ClassId, p.ClassName, p.ClassColorHex, p.ClassRank);
        if (p.Ahead != null) { DrawRow(dc, x, cy, width, rowH, p.RaceOrder && p.MultiClass ? ownColor : (Color4?)null, "Ahead", p.Ahead, true, PaletteTokens.TextPrimary); cy += rowH; }
        if (p.Behind != null) { DrawRow(dc, x, cy, width, rowH, p.RaceOrder && p.MultiClass ? ownColor : (Color4?)null, "Behind", p.Behind, false, PaletteTokens.TextPrimary); cy += rowH; }

        if (classes.Count > 0)
        {
            PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, cy + 3f);
            cy += 6f;
            foreach (var c in classes)
            {
                var color = PaletteTokens.ResolveClassColor(c.ClassId, c.ClassName, c.ClassColorHex, c.ClassRank);
                // faster class: the car coming from behind matters; slower class: the traffic ahead.
                var n = c.Faster ? c.Behind ?? c.Ahead : c.Ahead ?? c.Behind;
                if (n == null) continue;
                bool ahead = ReferenceEquals(n, c.Ahead);
                string label = (c.ClassName.Length > 0 ? c.ClassName : "Class") + (ahead ? " ahead" : " behind");
                var tone = c.Faster && !ahead ? Amber : !c.Faster && ahead ? PaletteTokens.TextPrimary : PaletteTokens.TextSecondary;
                DrawRow(dc, x, cy, width, rowH, color, label, n, ahead, tone);
                cy += rowH;
            }
        }

        PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, cy + 3f);
        cy += 6f;
        PanelChrome.DrawText(dc, _brush.Get(), _smallFormat.Get(), PitRejoinText.Loss(p), x + pad, cy, width - 2 * pad, footH, PaletteTokens.TextSecondary);
        cy += footH;
        if (est.Length > 0)
            PanelChrome.DrawText(dc, _brush.Get(), _smallFormat.Get(), est, x + pad, cy, width - 2 * pad, footH, Amber);

        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, float width, float h, Color4? strip, string label, RejoinNeighbour n, bool ahead, Color4 tone)
    {
        const float pad = 10f;
        float tx = x + pad;
        if (strip is { } color)
        {
            SetBrushColor(color);
            var r = new RectF(x + pad, y + 4f, x + pad + 4f, y + h - 4f);
            dc->FillRectangle(&r, (ID2D1Brush*)_brush.Get());
            tx += 9f;
        }
        float labelW = 96f * Math.Max(0.5f, _appearance.FontScale);
        float gapW = 64f * Math.Max(0.5f, _appearance.FontScale);
        PanelChrome.DrawText(dc, _brush.Get(), _smallFormat.Get(), label, tx, y, labelW, h, PaletteTokens.TextSecondary);
        PanelChrome.DrawText(dc, _brush.Get(), _rowFormat.Get(), PitRejoinText.Car(n), tx + labelW, y, width - (tx - x) - labelW - gapW - pad, h, tone);
        PanelChrome.DrawText(dc, _brush.Get(), _rowFormat.Get(), PitRejoinText.Gap(n, ahead), x + width - pad - gapW, y, gapW, h,
            ahead ? PaletteTokens.TextPrimary : n.Faster ? Amber : PaletteTokens.TextPrimary, TextAlignment.Trailing);
    }

    public void Dispose()
    {
        if (_telemetry != null)
        {
            _telemetry.PitRejoinUpdated -= OnPitRejoin;
            _telemetry.Dispose();
        }
        _labelFormat.Dispose();
        _bigFormat.Dispose();
        _rowFormat.Dispose();
        _smallFormat.Dispose();
        _brush.Dispose();
    }
}
