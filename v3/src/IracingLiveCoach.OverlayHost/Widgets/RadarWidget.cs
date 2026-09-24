using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// Kapps-style proximity radar. The player's car sits in the middle; every car within
/// <see cref="RangeMeters"/> along the lap is drawn as a car shape at its REAL longitudinal distance
/// (iRacing's per-car lap distance), in the lane <see cref="RadarSideAssigner"/> gives it from the
/// CarLeftRight flag -- iRacing publishes no lateral position, so the side is inferred, the vertical
/// position is exact. A red bar along the edge marks a side iRacing reports as occupied. Nothing is
/// drawn while the track around the player is clear (the widget stays out of the way).
/// </summary>
public sealed unsafe class RadarWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private RadarStatus? _status;
    private RadarStatus? _simulated;

    /// <summary>Non-null while showing fictitious data (simulation mode / Control Center preview).</summary>
    public void SetSimulatedStatus(RadarStatus? status) => _simulated = status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 120f;
    private const float HeightDip = 190f;
    /// <summary>+/- this many metres of track fit the height of the radar.</summary>
    public const float RangeMeters = 15f;
    private const float CarLengthMeters = 4.8f;
    private const float CarWidthDip = 16f;
    private const float LaneOffsetDip = 30f;

    /// <summary>Size the radar draws at (the overlay fits its window to it).</summary>
    public (float Width, float Height) LastDrawnSize => (WidthDip * _appearance.FontScale, HeightDip * _appearance.FontScale);

    public RadarWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.RadarUpdated += OnRadarUpdated;
        _telemetry.Start();
    }

    private void CreateTextFormats()
    {
        _labelFormat.Dispose();
        ComPtr<IDWriteTextFormat> labelFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 12f * _appearance.FontScale, fontWeight: FontWeight.SemiBold, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    private void OnRadarUpdated(RadarStatus status)
    {
        lock (_lock) { _status = status; }
    }

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    /// <summary>Draws at (x, y). Must be called between <see cref="DeviceResources.BeginFrame"/> and
    /// <see cref="DeviceResources.EndFrame"/>.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip)
    {
        RadarStatus? status = _simulated;
        bool simulated = status is not null;
        if (!simulated) { lock (_lock) { status = _status; } }
        if (status is null || !status.HasTrackLength || (!simulated && !_telemetry.HasRecentTelemetry)) return;
        // Shown ONLY while a car overlaps side by side (iRacing CarLeftRight 2..6 -- RadarSideOffsets.IsVisible,
        // the moment the side car turns red), like Kapps; otherwise nothing at all. The drawing itself is the
        // driver's preferred original radar (player + nearby cars, the overlapping one in red).
        bool anySide = status.BlindSpotLeft || status.BlindSpotRight;
        if (!anySide) return;

        float scale = _appearance.FontScale;
        float w = WidthDip * scale, h = HeightDip * scale;
        float cx = x + w / 2f, cy = y + h / 2f;
        float pxPerMeter = (h / 2f - 10f * scale) / RangeMeters;
        float carHeight = CarLengthMeters * pxPerMeter;
        float carWidth = CarWidthDip * scale;
        float lane = LaneOffsetDip * scale;

        var panel = new RectF(x, y, x + w, y + h);
        // Item 11: a dedicated, more translucent token -- never the shared PanelBackground.
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.RadarPanelBackground);

        // Distance guides every 10 m.
        SetBrushColor(PaletteTokens.PanelDivider);
        for (int m = -10; m <= 10; m += 5)
        {
            if (m == 0) continue;
            float gy = cy - m * pxPerMeter;
            var guide = new RectF(x + 10f, gy, x + w - 10f, gy + 1f);
            dc->FillRectangle(&guide, (ID2D1Brush*)_brush.Get());
        }

        // Occupied sides (iRacing's own flag): red bars along the edges.
        if (status.BlindSpotLeft) DrawSideBar(dc, x + 3f, y + 8f, y + h - 8f);
        if (status.BlindSpotRight) DrawSideBar(dc, x + w - 7f, y + 8f, y + h - 8f);

        // The player.
        DrawCar(dc, cx, cy, carWidth, carHeight, PaletteTokens.PlayerHighlight, filled: true);

        foreach (var blip in status.Blips)
        {
            float laneX = blip.Side switch { RadarSide.Left => cx - lane, RadarSide.Right => cx + lane, _ => cx };
            float carY = cy - (float)Math.Clamp(blip.DistanceMeters, -RangeMeters, RangeMeters) * pxPerMeter;
            double gap = Math.Abs(blip.DistanceMeters);
            var color = gap <= RadarSideAssigner.OverlapMeters && blip.Side != RadarSide.Center ? PaletteTokens.Critical
                : gap <= 12 ? PaletteTokens.Warning
                : PaletteTokens.TextSecondary;
            DrawCar(dc, laneX, carY, carWidth, carHeight, color, filled: blip.Side != RadarSide.Center || gap <= 12);
        }
        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private void DrawCar(ID2D1DeviceContext* dc, float centerX, float centerY, float width, float height, Color4 color, bool filled)
    {
        var body = new RectF(centerX - width / 2f, centerY - height / 2f, centerX + width / 2f, centerY + height / 2f);
        var rounded = new RoundedRect { rect = body, radiusX = width * 0.3f, radiusY = width * 0.3f };
        var c = color;
        if (!filled) c = new Color4(color.R, color.G, color.B, 0.55f);
        _brush.Get()->SetColor(&c);
        if (filled) dc->FillRoundedRectangle(&rounded, (ID2D1Brush*)_brush.Get());
        else dc->DrawRoundedRectangle(&rounded, (ID2D1Brush*)_brush.Get(), 1.6f, null);
    }

    private void DrawSideBar(ID2D1DeviceContext* dc, float x, float top, float bottom)
    {
        var bar = new RectF(x, top, x + 4f, bottom);
        var rounded = new RoundedRect { rect = bar, radiusX = 2f, radiusY = 2f };
        SetBrushColor(PaletteTokens.Critical);
        dc->FillRoundedRectangle(&rounded, (ID2D1Brush*)_brush.Get());
    }

    public void Dispose()
    {
        _telemetry.RadarUpdated -= OnRadarUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _brush.Dispose();
    }
}
