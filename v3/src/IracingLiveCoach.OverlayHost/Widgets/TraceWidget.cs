using System.Globalization;
using IracingLiveCoach.Core.Coach;
using IracingLiveCoach.OverlayHost.Coach;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// "Live Telemetry": rolling last-10-seconds traces of brake (red), throttle (green), steering (blue)
/// and speed (white) from the coach engine's live trace, each against the reference lap (thin ghost
/// line, from the TracePoint's Ref* values), plus the current gear / speed and vertical pedal bars.
/// Reads an immutable <see cref="CoachSnapshot"/> (copied under the host's lock) -- never touches the
/// engine from the render thread. Empty (not broken) when the coach is off or has no data.
/// </summary>
public sealed unsafe class TraceWidget : IDisposable
{
    private readonly CoachHost? _coach;
    private CoachSnapshot? _simulated;

    /// <summary>Spec §12's simulation preview -- see FuelWidget.SetSimulatedStatus for the rationale.</summary>
    public void SetSimulated(CoachSnapshot? snapshot) => _simulated = snapshot;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _bigFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<IDWriteTextFormat> _unitFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 560f;
    private const float HeightDip = 210f;
    private const float SteeringRangeRad = 3.0f;

    private static readonly Color4 BrakeRed = new(1.0f, 0.32f, 0.32f, 1f);
    private static readonly Color4 ThrottleGreen = new(0.20f, 0.83f, 0.51f, 1f);
    private static readonly Color4 SteeringBlue = new(0.35f, 0.65f, 1.0f, 1f);

    public TraceWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null, CoachHost? coach = null)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        _coach = coach;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;
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
        _valueFormat.Dispose();
        _unitFormat.Dispose();
        _labelFormat = MakeFormat(15f, FontWeight.SemiBold);
        _bigFormat = MakeFormat(40f, FontWeight.SemiBold);
        _valueFormat = MakeFormat(24f, FontWeight.SemiBold);
        _unitFormat = MakeFormat(12f, FontWeight.Medium);
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

    private static Color4 WithAlpha(Color4 c, float a) => new(c.R, c.G, c.B, a);

    /// <summary>Size of the last frame (the overlay auto-fits the window to it when auto-size is on).</summary>
    public (float Width, float Height) LastDrawnSize { get; private set; } = (WidthDip, HeightDip);

    /// <summary>Draws at (x, y) inside a width x height panel. Must be called between BeginFrame/EndFrame.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip, float height = HeightDip)
    {
        width = Math.Max(width, 240f);
        height = Math.Max(height, 120f);
        LastDrawnSize = (width, height);
        var snapshot = _simulated ?? _coach?.Snapshot(includeTrace: true);

        var panel = new RectF(x, y, x + width, y + height);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));

        var trace = snapshot?.Trace;
        if (snapshot is null || !snapshot.Settings.Enabled || trace is null || trace.Count < 2)
        {
            string text = _coach is null && _simulated is null ? "Coach unavailable" : snapshot is { Settings.Enabled: false } ? "Coach off" : "Waiting for coach data...";
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), text, x + 12f, y, width - 24f, height, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
            return;
        }

        var last = trace[^1];
        const float pad = 10f;
        float sideW = 92f;
        float chartLeft = x + pad;
        float chartRight = x + width - sideW - pad;
        float chartW = Math.Max(40f, chartRight - chartLeft);

        // Legend row.
        float legendY = y + 4f;
        const float legendH = 16f;
        float lx = chartLeft;
        foreach (var (name, color) in new[] { ("Brake", BrakeRed), ("Throttle", ThrottleGreen), ("Steering", SteeringBlue), ("Speed", PaletteTokens.TextPrimary) })
        {
            SetBrushColor(color);
            var swatch = new RectF(lx, legendY + 6f, lx + 10f, legendY + 9f);
            dc->FillRectangle(&swatch, (ID2D1Brush*)_brush.Get());
            float w = PanelChrome.MeasureWidth(_dwriteFactory, _unitFormat.Get(), name);
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), name, lx + 14f, legendY, w + 4f, legendH, PaletteTokens.TextSecondary);
            lx += 14f + w + 12f;
        }
        if (trace.Any(p => p.RefSpeedMs.HasValue))
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "thin = reference", lx, legendY, chartRight - lx, legendH, PaletteTokens.TextDisabled);

        // Lanes: pedals + steering on top, speed below.
        float chartTop = legendY + legendH + 2f;
        float chartBottom = y + height - pad;
        float gap = 6f;
        float pedalH = Math.Max(30f, (chartBottom - chartTop - gap) * 0.62f);
        float speedTop = chartTop + pedalH + gap;
        float speedH = Math.Max(20f, chartBottom - speedTop);

        DrawLaneBackground(dc, chartLeft, chartTop, chartW, pedalH);
        DrawLaneBackground(dc, chartLeft, speedTop, chartW, speedH);
        PanelChrome.HorizontalDivider(dc, _brush.Get(), chartLeft, chartLeft + chartW, chartTop + pedalH * 0.5f);

        double tEnd = last.SessionTime;
        double tStart = tEnd - CoachEngine.LiveTraceSeconds;

        double maxSpeedKmh = 100;
        foreach (var p in trace)
        {
            maxSpeedKmh = Math.Max(maxSpeedKmh, p.SpeedMs * 3.6);
            if (p.RefSpeedMs is double rs) maxSpeedKmh = Math.Max(maxSpeedKmh, rs * 3.6);
        }
        maxSpeedKmh = Math.Ceiling(maxSpeedKmh / 50.0) * 50.0;

        // Reference ghosts first (thin, translucent), then the driver's lines on top.
        DrawSeries(dc, trace, p => p.RefSpeedMs is double v ? v * 3.6 : null, chartLeft, speedTop, chartW, speedH, tStart, 0, maxSpeedKmh, WithAlpha(PaletteTokens.TextSecondary, 0.75f), 1f);
        DrawSeries(dc, trace, p => p.RefBrake, chartLeft, chartTop, chartW, pedalH, tStart, 0, 1, WithAlpha(BrakeRed, 0.55f), 1f);
        DrawSeries(dc, trace, p => p.RefThrottle, chartLeft, chartTop, chartW, pedalH, tStart, 0, 1, WithAlpha(ThrottleGreen, 0.55f), 1f);

        DrawSeries(dc, trace, p => p.SpeedMs * 3.6, chartLeft, speedTop, chartW, speedH, tStart, 0, maxSpeedKmh, PaletteTokens.TextPrimary, 2f);
        DrawSeries(dc, trace, p => Math.Clamp(p.SteeringRad / SteeringRangeRad, -1, 1) * 0.5 + 0.5, chartLeft, chartTop, chartW, pedalH, tStart, 0, 1, SteeringBlue, 1.6f);
        DrawSeries(dc, trace, p => p.Throttle, chartLeft, chartTop, chartW, pedalH, tStart, 0, 1, ThrottleGreen, 2f);
        DrawSeries(dc, trace, p => p.Brake, chartLeft, chartTop, chartW, pedalH, tStart, 0, 1, BrakeRed, 2f);

        // Right column: gear, speed, pedal bars.
        float sx = x + width - sideW;
        string gear = last.Gear switch { < 0 => "R", 0 => "N", var g => g.ToString(CultureInfo.InvariantCulture) };
        PanelChrome.DrawText(dc, _brush.Get(), _bigFormat.Get(), gear, sx, y + 4f, sideW - pad, 44f, PaletteTokens.TextPrimary, TextAlignment.Center);
        string speed = Math.Round(last.SpeedMs * 3.6).ToString("0", CultureInfo.InvariantCulture);
        PanelChrome.DrawText(dc, _brush.Get(), _valueFormat.Get(), speed, sx, y + 46f, sideW - pad, 26f, PaletteTokens.TextPrimary, TextAlignment.Center);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "km/h", sx, y + 68f, sideW - pad, 14f, PaletteTokens.TextSecondary, TextAlignment.Center);

        float barsTop = y + 90f;
        float labelH = 14f;
        float barsBottom = y + height - pad - labelH;
        if (barsBottom - barsTop > 20f)
        {
            float barW = 22f;
            float bx0 = sx + (sideW - pad) / 2f - barW - 5f;
            float bx1 = sx + (sideW - pad) / 2f + 5f;
            DrawBar(dc, bx0, barsTop, barW, barsBottom - barsTop, last.Brake, BrakeRed);
            DrawBar(dc, bx1, barsTop, barW, barsBottom - barsTop, last.Throttle, ThrottleGreen);
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "BRK", bx0 - 6f, barsBottom + 1f, barW + 12f, labelH, PaletteTokens.TextSecondary, TextAlignment.Center);
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "THR", bx1 - 6f, barsBottom + 1f, barW + 12f, labelH, PaletteTokens.TextSecondary, TextAlignment.Center);
        }

        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private void DrawLaneBackground(ID2D1DeviceContext* dc, float x, float y, float w, float h)
    {
        SetBrushColor(WithAlpha(PaletteTokens.PanelSurface, 0.85f));
        var rect = new RectF(x, y, x + w, y + h);
        dc->FillRectangle(&rect, (ID2D1Brush*)_brush.Get());
    }

    private void DrawBar(ID2D1DeviceContext* dc, float x, float y, float w, float h, double value, Color4 color)
    {
        SetBrushColor(WithAlpha(PaletteTokens.PanelSurface, 0.95f));
        var back = new RectF(x, y, x + w, y + h);
        dc->FillRectangle(&back, (ID2D1Brush*)_brush.Get());
        float fill = (float)Math.Clamp(value, 0, 1) * h;
        if (fill <= 0.5f) return;
        SetBrushColor(color);
        var bar = new RectF(x, y + h - fill, x + w, y + h);
        dc->FillRectangle(&bar, (ID2D1Brush*)_brush.Get());
    }

    /// <summary>Polyline of <paramref name="select"/> over the trace; a gap (>0.5 s) or a jump back in
    /// time breaks the line, and points are decimated to about one per horizontal pixel.</summary>
    private void DrawSeries(ID2D1DeviceContext* dc, IReadOnlyList<TracePoint> trace, Func<TracePoint, double?> select,
        float x, float y, float w, float h, double tStart, double min, double max, Color4 color, float thickness)
    {
        SetBrushColor(color);
        int step = Math.Max(1, (int)(trace.Count / Math.Max(1f, w)));
        System.Numerics.Vector2? prev = null;
        double prevT = 0;
        double span = CoachEngine.LiveTraceSeconds;
        for (int i = 0; i < trace.Count; i += step)
        {
            var p = trace[i];
            var v = select(p);
            if (v is null) { prev = null; continue; }
            double fx = (p.SessionTime - tStart) / span;
            if (fx < 0) continue;
            double fy = (Math.Clamp(v.Value, min, max) - min) / (max - min);
            var point = new System.Numerics.Vector2(x + (float)(fx * w), y + h - (float)fy * h);
            if (prev is { } a && p.SessionTime >= prevT && p.SessionTime - prevT <= 0.5 * Math.Max(1, step))
                dc->DrawLine(a, point, (ID2D1Brush*)_brush.Get(), thickness, null);
            prev = point;
            prevT = p.SessionTime;
        }
    }

    public void Dispose()
    {
        _labelFormat.Dispose();
        _bigFormat.Dispose();
        _valueFormat.Dispose();
        _unitFormat.Dispose();
        _brush.Dispose();
    }
}
