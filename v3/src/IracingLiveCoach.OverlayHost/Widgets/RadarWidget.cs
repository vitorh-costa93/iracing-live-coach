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
    private const float KappsBarHeightDip = 127f;
    private const float CarLengthMeters = 4.8f;
    private const float CarWidthDip = 16f;
    private const float LaneOffsetDip = 30f;
    /// <summary>Side bar track width -- report_backend's radar section: "~20x127 px em 1080p" (the
    /// 127 px height is this widget's own player-car length in pixels, already computed as
    /// <c>carHeight</c>; only the width is a fixed constant).</summary>
    private const float SideBarWidthDip = 14f;
    private const float SideBarInsetDip = 4f;

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
        // Kapps: nothing at all unless a car overlaps side by side (RadarSideOffsets.IsVisible), and then
        // only the side bars -- no panel, no guides, no cars ahead/behind.
        bool anySide = status.BlindSpotLeft || status.BlindSpotRight;
        if (!anySide) return;

        float scale = _appearance.FontScale;
        float w = WidthDip * scale, h = HeightDip * scale;
        float cy = y + h / 2f;

        // One amber bar per side iRacing reports occupied (report_backend's radar section). Its track stands
        // for the PLAYER's car (rear at the bottom, nose at the top); the amber fill is the stretch of it the
        // side car covers (RadarSideOffsets.Fill). Kapps' track is ~127 px tall at 1080p.
        float barHalf = Math.Min(KappsBarHeightDip * scale, h - 8f) / 2f;
        float barTop = cy - barHalf, barBottom = cy + barHalf;
        if (status.BlindSpotLeft) DrawSideBar(dc, x + 3f, barTop, barBottom, status.LeftCarOffsetMeters, scale);
        if (status.BlindSpotRight) DrawSideBar(dc, x + w - 3f - SideBarWidthDip * scale, barTop, barBottom, status.RightCarOffsetMeters, scale);
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

    /// <summary>Kapps-style side bar (report_backend's radar section, evidence: kapps_radar1.png).
    /// The track (dark, rounded) represents the PLAYER's own car, rear at <paramref name="bottom"/>
    /// and nose at <paramref name="top"/>; the amber fill is the fraction of it
    /// <see cref="RadarSideOffsets.Fill"/> says the side car covers, inset a few px inside the
    /// track. A null offset (side reported occupied but no car resolved there) fills the whole
    /// track, matching Fill's own documented fallback.</summary>
    private void DrawSideBar(ID2D1DeviceContext* dc, float left, float top, float bottom, double? offsetMeters, float scale)
    {
        float width = SideBarWidthDip * scale;
        var track = new RectF(left, top, left + width, bottom);
        var trackRounded = new RoundedRect { rect = track, radiusX = 3f * scale, radiusY = 3f * scale };
        SetBrushColor(PaletteTokens.RadarSideBarTrack);
        dc->FillRoundedRectangle(&trackRounded, (ID2D1Brush*)_brush.Get());

        var (from, to) = RadarSideOffsets.Fill(offsetMeters, CarLengthMeters);
        float inset = SideBarInsetDip * scale;
        float trackHeight = bottom - top;
        // "From"/"To" are measured bottom (rear, 0) to top (nose, 1); Direct2D's y grows downward,
        // so the near-rear end ("From") maps to the LOWER y coordinate (closer to bottom).
        float fillTop = bottom - (float)(to * trackHeight);
        float fillBottom = bottom - (float)(from * trackHeight);
        if (fillBottom - fillTop < 1f) return; // no measurable overlap -- nothing to draw
        var fill = new RectF(left + inset, fillTop, left + width - inset, fillBottom);
        var fillRounded = new RoundedRect { rect = fill, radiusX = 2f * scale, radiusY = 2f * scale };
        SetBrushColor(PaletteTokens.RadarSideBarFill);
        dc->FillRoundedRectangle(&fillRounded, (ID2D1Brush*)_brush.Get());
    }

    public void Dispose()
    {
        _telemetry.RadarUpdated -= OnRadarUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _brush.Dispose();
    }
}
