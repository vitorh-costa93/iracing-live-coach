using System.Globalization;
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
    private FuelStatus? _simulatedStatus;

    /// <summary>Spec §12's simulation preview -- see StandingsWidget.SetSimulatedRows for the same
    /// rationale. Never used as a stand-in for real telemetry.</summary>
    public void SetSimulatedStatus(FuelStatus? status) => _simulatedStatus = status;

    private FuelConfig _config = FuelConfig.Default;
    public void SetConfig(FuelConfig config) => _config = config;

    /// <summary>Resolves spec §9's consumption-source choice into one liters/lap figure. Manual and
    /// Max are only used when the user actually opted in (Manual needs a positive value; Max/LastLap
    /// need at least one clean sample) -- an unusable choice falls back to the average rather than
    /// silently showing "—" for a control the user explicitly set.</summary>
    private double? ResolveLitersPerLap(FuelStatus status) => _config.Source switch
    {
        FuelConsumptionSource.Manual => _config.ManualLitersPerLap > 0 ? _config.ManualLitersPerLap : status.AverageFuelPerLapLiters,
        FuelConsumptionSource.LastLap => (_config.ExcludePitLaps && status.LastLapAffectedByPit) || status.LastLapFuelUsedLiters is not > 0
            ? status.AverageFuelPerLapLiters
            : status.LastLapFuelUsedLiters,
        FuelConsumptionSource.Max => status.MaxFuelPerLapLiters ?? status.AverageFuelPerLapLiters,
        _ => status.AverageFuelPerLapLiters,
    };

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 300f;
    private const float RowHeightDip = 32f;

    public FuelWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.FuelUpdated += OnFuelUpdated;
        _telemetry.Start();
    }

    private void CreateTextFormats()
    {
        _labelFormat.Dispose();
        _valueFormat.Dispose();

        float scale = _appearance.FontScale;
        ComPtr<IDWriteTextFormat> labelFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 12f * scale, fontWeight: FontWeight.SemiBold, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        ComPtr<IDWriteTextFormat> valueFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 20f * scale, fontWeight: FontWeight.SemiBold, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(valueFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(valueFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _valueFormat = valueFormat;
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
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
        lock (_lock) { status = _simulatedStatus ?? _status; }

        DrawPanel(dc, x, y, width, RowHeightDip * 3);

        if (_simulatedStatus is null && (!_telemetry.HasRecentTelemetry || status is null))
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

        // Static fuel-pump pictogram (mockup parity) -- a generic pictogram, not a brand/nationality
        // asset spec §18 governs, so it's drawn as vector geometry rather than requiring an image.
        DrawFuelPumpIcon(dc, x + width - 20f, y + 2f);

        float colWidth = width / 2 - 4f;

        // Effective per-lap figure per spec §9's consumption-source choice, with the configured
        // reserve subtracted from the displayed Autonomy (never shown as a negative -- a driver
        // already below reserve needs "0.0 laps", not a confusing negative number).
        double? litersPerLap = ResolveLitersPerLap(status);
        double? rawLapsRemaining = litersPerLap is double perLap and > 0 ? status.FuelLevelLiters / perLap : null;
        double? lapsRemaining = rawLapsRemaining is double raw ? Math.Max(0, raw - _config.ReserveLaps) : null;
        double? timeRemaining = lapsRemaining is double laps2 && status.AverageLapTimeSeconds is double lapTime ? laps2 * lapTime : null;

        // Fuel level and autonomy get emphasis (larger value text), per spec §9's "dê destaque a
        // combustível e autonomia, com métricas auxiliares menores".
        Metric(dc, "FUEL", $"{status.FuelLevelLiters:0.0} L", x, y, colWidth, PaletteTokens.TextPrimary);
        string autonomy = lapsRemaining is double laps ? $"{laps:0.0} laps" : "—";
        Metric(dc, "AUTONOMY", autonomy, x + colWidth + 8f, y, colWidth, PaletteTokens.TextPrimary);

        string sourceLabel = _config.Source switch
        {
            FuelConsumptionSource.LastLap => "LAST LAP",
            FuelConsumptionSource.Max => "MAX/LAP",
            FuelConsumptionSource.Manual => "MANUAL",
            _ => "AVG/LAP"
        };
        string avg = litersPerLap is double avgVal ? $"{avgVal:0.00} L" : "—";
        MetricSmall(dc, sourceLabel, avg, x, y + RowHeightDip, colWidth);

        string time = timeRemaining is double seconds
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

    /// <summary>Graphite surface + outer border (spec §16) -- this widget previously had no
    /// background/border at all.</summary>
    private void DrawPanel(ID2D1DeviceContext* dc, float x, float y, float width, float height)
    {
        SetBrushColor(PaletteTokens.OverlayBackground);
        var background = new RectF(x, y, x + width, y + height);
        dc->FillRectangle(&background, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.WidgetOuterBorder);
        dc->DrawRectangle(&background, (ID2D1Brush*)_brush.Get(), PaletteTokens.BorderAndGridThicknessPx, null);
    }

    /// <summary>Simple vector fuel-pump pictogram: a body rectangle, a small display notch, and a
    /// nozzle/hose -- static, since (unlike weather) there's no real-time condition it should react
    /// to besides the numbers already shown next to it.</summary>
    private void DrawFuelPumpIcon(ID2D1DeviceContext* dc, float x, float y)
    {
        SetBrushColor(PaletteTokens.TextSecondary);
        var body = new RectF(x, y + 3f, x + 10f, y + 16f);
        dc->DrawRectangle(&body, (ID2D1Brush*)_brush.Get(), 1.3f, null);
        var display = new RectF(x + 2f, y + 5f, x + 8f, y + 8f);
        dc->FillRectangle(&display, (ID2D1Brush*)_brush.Get());
        dc->DrawLine(new System.Numerics.Vector2(x + 10f, y + 6f), new System.Numerics.Vector2(x + 15f, y + 6f), (ID2D1Brush*)_brush.Get(), 1.3f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 15f, y + 6f), new System.Numerics.Vector2(x + 15f, y + 14f), (ID2D1Brush*)_brush.Get(), 1.3f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 13.5f, y + 14f), new System.Numerics.Vector2(x + 16.5f, y + 14f), (ID2D1Brush*)_brush.Get(), 1.3f, null);
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
