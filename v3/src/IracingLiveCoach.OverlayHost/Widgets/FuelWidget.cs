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
/// Fuel Calculator widget (Phase 4, rewritten 2026-09-18 -- the original pass drew a decorative
/// "FUEL CALCULATOR" title, directly violating spec §5/§15, and only showed fuel level, refuel
/// need, average/lap and laps remaining -- missing time remaining, entirely).
///
/// Still open, honestly: spec §9 also asks for a distinct "last valid lap" consumption figure and
/// a "conservative mode" toggle -- <see cref="FuelStatus"/> only carries a rolling average, not a
/// last-lap-specific value, and there is no config surface yet (Phase 5) to offer a conservative
/// margin. Shown here: current fuel, average/lap, laps remaining, time remaining, and fuel needed
/// to finish -- all real fields already computed by <see cref="TelemetryReader"/>, none fabricated.
/// </summary>
public sealed unsafe class FuelWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private FuelStatus? _status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private const float WidthDip = 300f;
    private const float RowHeightDip = 32f;

    public FuelWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory)
    {
        ComPtr<IDWriteTextFormat> labelFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 12f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        ComPtr<IDWriteTextFormat> valueFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 20f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(valueFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(valueFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _valueFormat = valueFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.FuelUpdated += OnFuelUpdated;
        _telemetry.Start();
    }

    private void OnFuelUpdated(FuelStatus status)
    {
        lock (_lock) { _status = status; }
    }

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    /// <summary>Draws at (x, y). Must be called between <see cref="DeviceResources.BeginFrame"/> and
    /// <see cref="DeviceResources.EndFrame"/>. No decorative title, per spec §5/§15.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip)
    {
        FuelStatus? status;
        lock (_lock) { status = _status; }

        if (!_telemetry.HasRecentTelemetry || status is null)
        {
            SetBrushColor(PaletteTokens.TextDisabled);
            const string text = "Aguardando iRacing...";
            fixed (char* p = text)
            {
                var rect = new RectF(x, y, x + width, y + RowHeightDip);
                dc->DrawText(p, (uint)text.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            return;
        }

        float colWidth = width / 2 - 4f;

        // Fuel level and autonomy get emphasis (larger value text), per spec §9's "dê destaque a
        // combustível e autonomia, com métricas auxiliares menores".
        Metric(dc, "FUEL", $"{status.FuelLevelLiters:0.0} L", x, y, colWidth, PaletteTokens.TextPrimary);
        string autonomy = status.LapsRemaining is double laps ? $"{laps:0.0} laps" : "—";
        Metric(dc, "AUTONOMY", autonomy, x + colWidth + 8f, y, colWidth, PaletteTokens.TextPrimary);

        string avg = status.AverageFuelPerLapLiters is double avgVal ? $"{avgVal:0.00} L" : "—";
        MetricSmall(dc, "AVG/LAP", avg, x, y + RowHeightDip, colWidth);

        string time = status.TimeRemainingSeconds is double seconds
            ? $"{TimeSpan.FromSeconds(seconds):mm\\:ss}"
            : "—";
        MetricSmall(dc, "TIME LEFT", time, x + colWidth + 8f, y + RowHeightDip, colWidth);

        // Fuel needed to finish -- only shown when actually calculable (spec §9: never fabricate a
        // number from zero samples), colored as a warning when a top-up is actually required.
        if (status.FuelNeededForFinishLiters is double needed)
        {
            var color = needed > 0 ? PaletteTokens.Warning : PaletteTokens.PositiveDelta;
            string text = needed > 0 ? $"+{needed:0.0} L NEEDED" : "ENOUGH TO FINISH";
            MetricSmall(dc, "TO FINISH", text, x, y + RowHeightDip * 2, width, color);
        }
        else
        {
            MetricSmall(dc, "TO FINISH", "—", x, y + RowHeightDip * 2, width);
        }
    }

    private void Metric(ID2D1DeviceContext* dc, string label, string value, float x, float y, float width, Color4 valueColor)
    {
        SetBrushColor(PaletteTokens.TextSecondary);
        fixed (char* p = label)
        {
            var rect = new RectF(x, y, x + width, y + 14f);
            dc->DrawText(p, (uint)label.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        SetBrushColor(valueColor);
        fixed (char* p = value)
        {
            var rect = new RectF(x, y + 13f, x + width, y + RowHeightDip);
            dc->DrawText(p, (uint)value.Length, _valueFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void MetricSmall(ID2D1DeviceContext* dc, string label, string value, float x, float y, float width, Color4? valueColor = null)
    {
        SetBrushColor(PaletteTokens.TextSecondary);
        string text = $"{label} {value}";
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        if (valueColor is Color4 color)
        {
            SetBrushColor(color);
            fixed (char* p = value)
            {
                var rect = new RectF(x, y, x + width, y + RowHeightDip);
                dc->DrawText(p, (uint)value.Length, _labelFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
    }

    public void Dispose()
    {
        _telemetry.FuelUpdated -= OnFuelUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _brush.Dispose();
    }
}
