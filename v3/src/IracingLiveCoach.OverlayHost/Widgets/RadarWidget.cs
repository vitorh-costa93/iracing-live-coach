using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// Radar widget (Phase 4, rewritten 2026-09-18 -- the original pass drew only the two blind-spot
/// boxes and silently showed nothing for both "clear track" and "telemetry unknown/disconnected",
/// which spec §10 explicitly forbids conflating). No decorative title, per spec §5/§15.
///
/// Renders two independent things the SDK actually publishes, kept visually distinct on purpose
/// (see the plan's Global Constraints): <see cref="RadarStatus.BlindSpotLeft"/>/<see cref="RadarStatus.BlindSpotRight"/>
/// (a coarse "something is right next to you" signal, not a per-car position) as side boxes, and
/// <see cref="RadarStatus.Blips"/> (real per-car signed distance along the track) as a vertical
/// scale with one marker per car. Never draws a fabricated XY position or lateral distance --
/// spec §10: "não desenhe carros em coordenadas XY precisas... a partir de dados insuficientes."
/// </summary>
public sealed unsafe class RadarWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private RadarStatus? _status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private const float WidthDip = 180f;
    private const float HeightDip = 130f;
    private const float BlindSpotBoxHeightDip = 28f;
    private const float ScaleRangeMeters = 60f; // +/- this many meters of track distance fits the vertical scale

    public RadarWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory)
    {
        ComPtr<IDWriteTextFormat> labelFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 12f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.RadarUpdated += OnRadarUpdated;
        _telemetry.Start();
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
        RadarStatus? status;
        lock (_lock) { status = _status; }

        // Spec §10: "diferencie pista livre de telemetria desconectada/desconhecida" -- these are
        // two genuinely different states and must never look the same. No telemetry at all (or the
        // sim hasn't confirmed a usable track length yet) is shown explicitly, not left blank.
        if (!_telemetry.HasRecentTelemetry || status is null || !status.HasTrackLength)
        {
            SetBrushColor(PaletteTokens.TextDisabled);
            const string text = "RADAR —";
            fixed (char* p = text)
            {
                var rect = new RectF(x, y, x + width, y + BlindSpotBoxHeightDip);
                dc->DrawText(p, (uint)text.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            return;
        }

        // Blind-spot boxes: a coarse per-side signal, never a per-car position (spec §10).
        if (status.BlindSpotLeft) DrawBlindSpotBox(dc, "LEFT", x, y, width * 0.48f);
        if (status.BlindSpotRight) DrawBlindSpotBox(dc, "RIGHT", x + width * 0.52f, y, width * 0.48f);
        if (!status.BlindSpotLeft && !status.BlindSpotRight && status.Blips.Count == 0)
        {
            // Genuinely clear track -- an explicit, deliberate state, not the same rendering as
            // "unknown" above.
            SetBrushColor(PaletteTokens.TextSecondary);
            const string clear = "CLEAR";
            fixed (char* p = clear)
            {
                var rect = new RectF(x, y, x + width, y + BlindSpotBoxHeightDip);
                dc->DrawText(p, (uint)clear.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }

        DrawBlipScale(dc, status.Blips, x, y + BlindSpotBoxHeightDip + 6f, width);
    }

    private void DrawBlindSpotBox(ID2D1DeviceContext* dc, string label, float x, float y, float width)
    {
        SetBrushColor(PaletteTokens.Warning);
        var box = new RectF(x, y, x + width, y + BlindSpotBoxHeightDip);
        dc->FillRectangle(&box, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.TextOnLight);
        fixed (char* p = label)
        {
            dc->DrawText(p, (uint)label.Length, _labelFormat.Get(), &box, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    /// <summary>Real per-car signed track distance (spec §10's <see cref="RadarBlip"/>), drawn as a
    /// vertical scale centered on the player -- never a fabricated 2D lane position.</summary>
    private void DrawBlipScale(ID2D1DeviceContext* dc, List<RadarBlip> blips, float x, float y, float width)
    {
        float scaleHeight = HeightDip - BlindSpotBoxHeightDip - 6f;
        SetBrushColor(PaletteTokens.Grid);
        var centerLine = new RectF(x, y + scaleHeight / 2 - 1f, x + width, y + scaleHeight / 2 + 1f);
        dc->FillRectangle(&centerLine, (ID2D1Brush*)_brush.Get());

        foreach (var blip in blips)
        {
            float clamped = (float)Math.Clamp(blip.DistanceMeters, -ScaleRangeMeters, ScaleRangeMeters);
            float normalized = clamped / ScaleRangeMeters; // -1 (behind) .. +1 (ahead)
            float markerY = y + scaleHeight / 2 - normalized * (scaleHeight / 2 - 10f);

            SetBrushColor(Math.Abs(blip.DistanceMeters) < 10 ? PaletteTokens.Critical : PaletteTokens.Warning);
            var marker = new RectF(x + width / 2 - 20f, markerY - 6f, x + width / 2 + 20f, markerY + 6f);
            dc->FillRectangle(&marker, (ID2D1Brush*)_brush.Get());

            SetBrushColor(PaletteTokens.TextOnLight);
            string label = $"{blip.DriverCode} {blip.DistanceMeters:+0;-0}m";
            fixed (char* p = label)
            {
                dc->DrawText(p, (uint)label.Length, _labelFormat.Get(), &marker, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
    }

    public void Dispose()
    {
        _telemetry.RadarUpdated -= OnRadarUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _brush.Dispose();
    }
}
