using System.Globalization;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// Start Helper widget (Phase 4, rewritten 2026-09-18 -- the original pass drew only clutch/
/// throttle bars, always in the "in range" color, with no RPM readout at all despite spec §11
/// explicitly requiring "RPM atual e faixa-alvo"). No decorative title, per spec §5/§15.
///
/// Clutch/throttle are real telemetry (spec-confirmed). RPM is the same real "RPM" channel
/// <see cref="TelemetryReader"/> already reads elsewhere. The target RPM band below is a documented
/// DEFAULT PLACEHOLDER, not a per-car calibration -- spec §11 asks for a "referência configurada/
/// calibrada", which needs Phase 5's Control Center calibration UI to be meaningful; until that
/// exists, this widget shows a generic band rather than inventing a specific car's real launch RPM.
/// </summary>
public sealed unsafe class StartHelperWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private RaceStartStatus? _status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private const float WidthDip = 280f;
    private const float RowHeightDip = 22f;
    private const float RowGapDip = 4f;

    // Placeholder default target band -- see the class doc comment. Not a real per-car calibration.
    private const double DefaultTargetRpmLow = 5500;
    private const double DefaultTargetRpmHigh = 7000;
    private const double DefaultCriticalRpm = 8500;

    public StartHelperWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory)
    {
        ComPtr<IDWriteTextFormat> labelFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 13f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.RaceStartUpdated += OnRaceStartUpdated;
        _telemetry.Start();
    }

    private void OnRaceStartUpdated(RaceStartStatus status)
    {
        lock (_lock) { _status = status; }
    }

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    /// <summary>Draws at (x, y). Must be called between <see cref="DeviceResources.BeginFrame"/> and
    /// <see cref="DeviceResources.EndFrame"/>. Spec §11: shown only while preparing to launch,
    /// hidden otherwise -- <see cref="RaceStartStatus.ShouldShow"/> already encodes that rule.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip)
    {
        RaceStartStatus? status;
        lock (_lock) { status = _status; }
        if (status?.ShouldShow != true) return;

        float rowY = y;
        DrawRpmRow(dc, status.RpmValue, x, rowY, width);
        rowY += RowHeightDip + RowGapDip;
        DrawBar(dc, "CLUTCH", status.ClutchPct, PaletteTokens.StartHelperInRange, x, rowY, width);
        rowY += RowHeightDip + RowGapDip;
        DrawBar(dc, "THROTTLE", status.ThrottlePct, PaletteTokens.StartHelperInRange, x, rowY, width);
    }

    private void DrawRpmRow(ID2D1DeviceContext* dc, double rpm, float x, float y, float width)
    {
        var color = rpm >= DefaultCriticalRpm ? PaletteTokens.StartHelperCritical
            : rpm >= DefaultTargetRpmLow && rpm <= DefaultTargetRpmHigh ? PaletteTokens.StartHelperInRange
            : PaletteTokens.StartHelperOutOfRange;

        SetBrushColor(PaletteTokens.OverlayBackground);
        var bg = new RectF(x, y, x + width, y + RowHeightDip);
        dc->FillRectangle(&bg, (ID2D1Brush*)_brush.Get());

        SetBrushColor(PaletteTokens.TextSecondary);
        const string label = "RPM";
        fixed (char* p = label)
        {
            var rect = new RectF(x + 6f, y, x + 70f, y + RowHeightDip);
            dc->DrawText(p, (uint)label.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }

        SetBrushColor(color);
        string value = rpm.ToString("0", CultureInfo.InvariantCulture);
        fixed (char* p = value)
        {
            var rect = new RectF(x + width - 90f, y, x + width - 6f, y + RowHeightDip);
            dc->DrawText(p, (uint)value.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawBar(ID2D1DeviceContext* dc, string label, double valuePct, Color4 fillColor, float x, float y, float width)
    {
        SetBrushColor(PaletteTokens.OverlayBackground);
        var bg = new RectF(x, y, x + width, y + RowHeightDip);
        dc->FillRectangle(&bg, (ID2D1Brush*)_brush.Get());

        SetBrushColor(fillColor);
        float clamped = (float)Math.Clamp(valuePct / 100.0, 0, 1);
        var fill = new RectF(x + 90f, y + 4f, x + 90f + (width - 100f) * clamped, y + RowHeightDip - 4f);
        dc->FillRectangle(&fill, (ID2D1Brush*)_brush.Get());

        SetBrushColor(PaletteTokens.TextPrimary);
        string text = $"{label} {valuePct.ToString("0", CultureInfo.InvariantCulture)}%";
        fixed (char* p = text)
        {
            var rect = new RectF(x + 4f, y, x + width - 4f, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    public void Dispose()
    {
        _telemetry.RaceStartUpdated -= OnRaceStartUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _brush.Dispose();
    }
}
