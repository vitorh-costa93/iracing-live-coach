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
/// Weather Report widget (Phase 4, rewritten 2026-09-18 -- the original pass drew a decorative
/// "WEATHER REPORT" title, directly violating spec §5/§15, and was missing wind, rubber state, and
/// the current-rain field entirely, despite <see cref="WeatherStatus"/> already carrying them).
///
/// Wetness comes from the SDK's own TrackWetness enum only -- never inferred from precipitation
/// (spec §8: "não converta categorias em porcentagem sem fundamento"). PrecipitationPct is labeled
/// as current measured intensity, never as a forecast/probability (spec §8's explicit warning
/// against conflating the two). Rubber state is a field independent of wetness, drawn separately.
/// </summary>
public sealed unsafe class WeatherWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private WeatherStatus? _status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private const float WidthDip = 280f;
    private const float RowHeightDip = 32f;

    public WeatherWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory)
    {
        ComPtr<IDWriteTextFormat> labelFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 12f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        ComPtr<IDWriteTextFormat> valueFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 16f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(valueFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(valueFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _valueFormat = valueFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.WeatherUpdated += OnWeatherUpdated;
        _telemetry.Start();
    }

    private void OnWeatherUpdated(WeatherStatus status)
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
        WeatherStatus? status;
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
        Metric(dc, "AIR", $"{status.AirTempC:0.#}°C", x, y, colWidth, PaletteTokens.TextPrimary);
        Metric(dc, "TRACK", $"{status.TrackTempC:0.#}°C", x + colWidth + 8f, y, colWidth, PaletteTokens.TextPrimary);

        var wetnessColor = status.TrackWetness > 1 ? PaletteTokens.WeatherWet : PaletteTokens.WeatherDry;
        Metric(dc, "TRACK WETNESS", Wetness(status.TrackWetness), x, y + RowHeightDip, width, wetnessColor);

        // Current rain intensity -- explicitly NOT a forecast/probability (spec §8).
        Metric(dc, "RAIN NOW", $"{status.PrecipitationPct:0.#}%", x, y + RowHeightDip * 2, colWidth,
            status.PrecipitationPct > 0 ? PaletteTokens.WeatherWet : PaletteTokens.TextSecondary);

        // Wind -- independently optional real telemetry (spec §8 requires it as a distinct field).
        string windText = status.WindSpeedMs is double speed
            ? $"{speed:0.#} m/s" + (status.WindDirectionDeg is double dir ? $" {dir:0}°" : "")
            : "—";
        Metric(dc, "WIND", windText, x + colWidth + 8f, y + RowHeightDip * 2, colWidth, PaletteTokens.TextPrimary);

        // Rubber is a field independent of wetness/humidity (spec §8) -- never derived from it.
        Metric(dc, "RUBBER", string.IsNullOrWhiteSpace(status.TrackRubberState) ? "—" : status.TrackRubberState!,
            x, y + RowHeightDip * 3, width, PaletteTokens.TextSecondary);
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

    private static string Wetness(int wetness) => wetness switch
    {
        1 => "DRY",
        2 => "MOSTLY DRY",
        3 => "VERY LIGHTLY WET",
        4 => "LIGHTLY WET",
        5 => "MODERATELY WET",
        6 => "VERY WET",
        7 => "EXTREMELY WET",
        _ => "—"
    };

    public void Dispose()
    {
        _telemetry.WeatherUpdated -= OnWeatherUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _brush.Dispose();
    }
}
