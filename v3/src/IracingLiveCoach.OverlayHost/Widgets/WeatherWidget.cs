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
    private WeatherStatus? _simulatedStatus;

    /// <summary>Spec §12's simulation preview -- see StandingsWidget.SetSimulatedRows for the same
    /// rationale. Never used as a stand-in for real telemetry.</summary>
    public void SetSimulatedStatus(WeatherStatus? status) => _simulatedStatus = status;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 280f;
    private const float RowHeightDip = 32f;

    public WeatherWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.WeatherUpdated += OnWeatherUpdated;
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

        ComPtr<IDWriteTextFormat> valueFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 16f * scale, fontWeight: FontWeight.SemiBold, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
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
        lock (_lock) { status = _simulatedStatus ?? _status; }

        float panelHeight = RowHeightDip * 4;
        DrawPanel(dc, x, y, width, panelHeight);

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

        // Dynamic condition icon (mockup parity) -- driven by the SAME real TrackWetness/precipitation
        // fields the text already uses, never a separate guess. Sun = dry, cloud = damp track but no
        // measured rain, rain = actual measured precipitation right now (spec §8: never a forecast).
        DrawWeatherIcon(dc, x + width - 22f, y + 2f, status.TrackWetness, status.PrecipitationPct);

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

    /// <summary>Graphite surface + outer border (spec §16's OverlayBackground/WidgetOuterBorder)
    /// behind the whole widget -- previously this widget had no background/border at all, just text
    /// floating directly on the desktop/game.</summary>
    private void DrawPanel(ID2D1DeviceContext* dc, float x, float y, float width, float height)
    {
        SetBrushColor(PaletteTokens.OverlayBackground);
        var background = new RectF(x, y, x + width, y + height);
        dc->FillRectangle(&background, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.WidgetOuterBorder);
        dc->DrawRectangle(&background, (ID2D1Brush*)_brush.Get(), PaletteTokens.BorderAndGridThicknessPx, null);
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

    /// <summary>Vector-drawn (never a bitmap emblem -- this is a generic condition pictogram, not a
    /// brand/nationality asset spec §18 governs) icon that changes with real telemetry: a sun for a
    /// genuinely dry track, a cloud once the track is damp but no rain is currently falling, and a
    /// cloud with rain streaks once <see cref="WeatherStatus.PrecipitationPct"/> is actually
    /// positive. Never animated/embellished beyond what the data supports.</summary>
    private void DrawWeatherIcon(ID2D1DeviceContext* dc, float x, float y, int wetness, double precipitationPct)
    {
        const float size = 18f;
        float cx = x + size / 2f;
        float cy = y + size / 2f;

        if (precipitationPct > 0 || wetness >= 4)
        {
            // Cloud + rain streaks.
            SetBrushColor(PaletteTokens.TextSecondary);
            DrawCloud(dc, cx, cy - 2f, size);
            SetBrushColor(PaletteTokens.WeatherWet);
            for (int i = 0; i < 3; i++)
            {
                float sx = x + 4f + i * 5f;
                var line = new RectF(sx, cy + 4f, sx + 1.4f, cy + 9f);
                dc->FillRectangle(&line, (ID2D1Brush*)_brush.Get());
            }
        }
        else if (wetness >= 2)
        {
            // Damp/drying track, no active rain -- cloud only.
            SetBrushColor(PaletteTokens.TextSecondary);
            DrawCloud(dc, cx, cy, size);
        }
        else
        {
            // Genuinely dry -- sun.
            SetBrushColor(PaletteTokens.WeatherDry);
            var sun = new Ellipse { point = new System.Numerics.Vector2(cx, cy), radiusX = size * 0.28f, radiusY = size * 0.28f };
            dc->FillEllipse(&sun, (ID2D1Brush*)_brush.Get());
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                float innerR = size * 0.4f, outerR = size * 0.5f;
                float x1 = cx + (float)(Math.Cos(angle) * innerR), y1 = cy + (float)(Math.Sin(angle) * innerR);
                float x2 = cx + (float)(Math.Cos(angle) * outerR), y2 = cy + (float)(Math.Sin(angle) * outerR);
                dc->DrawLine(new System.Numerics.Vector2(x1, y1), new System.Numerics.Vector2(x2, y2), (ID2D1Brush*)_brush.Get(), 1.4f, null);
            }
        }
    }

    private void DrawCloud(ID2D1DeviceContext* dc, float cx, float cy, float size)
    {
        var left = new Ellipse { point = new System.Numerics.Vector2(cx - size * 0.22f, cy + size * 0.05f), radiusX = size * 0.22f, radiusY = size * 0.18f };
        var right = new Ellipse { point = new System.Numerics.Vector2(cx + size * 0.15f, cy), radiusX = size * 0.28f, radiusY = size * 0.22f };
        dc->FillEllipse(&left, (ID2D1Brush*)_brush.Get());
        dc->FillEllipse(&right, (ID2D1Brush*)_brush.Get());
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
