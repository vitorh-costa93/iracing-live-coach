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

    private static readonly Color4 ValueAmber = new(1.0f, 0.78f, 0.24f, 1f); // mockups: wetness/rubber values in amber

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 300f;
    private const float PanelHeightDip = 116f;

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
        ComPtr<IDWriteTextFormat> labelFormat = FontCatalog.CreateFormat(_dwriteFactory, _fontCollection, _appearance.FontFamily, 15f * scale, FontWeight.SemiBold, _appearance.FontWeight);
        ThrowIfFailed(labelFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(labelFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _labelFormat = labelFormat;

        ComPtr<IDWriteTextFormat> valueFormat = FontCatalog.CreateFormat(_dwriteFactory, _fontCollection, _appearance.FontFamily, 13.5f * scale, FontWeight.SemiBold, _appearance.FontWeight);
        ThrowIfFailed(valueFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(valueFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _valueFormat = valueFormat;
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight || appearance.FontFamily != _appearance.FontFamily;
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

        var panel = new RectF(x, y, x + width, y + PanelHeightDip);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.PanelBackground);

        if (_simulatedStatus is null && (!_telemetry.HasRecentTelemetry || status is null))
        {
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "Waiting for iRacing...", x + 12f, y, width - 24f, PanelHeightDip, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
            return;
        }

        // Dynamic condition icon -- driven by the SAME real TrackWetness/precipitation fields the
        // text uses, never a separate guess (spec §8: never a forecast).
        DrawWeatherIcon(dc, x + 14f, y + 20f, status!.TrackWetness, status.PrecipitationPct, 42f);

        float textX = x + 68f;
        const float line = 23f;
        var labelColor = PaletteTokens.TextPrimary;
        var wetnessColor = status.TrackWetness > 2 ? PaletteTokens.WeatherWet : ValueAmber;
        PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _labelFormat.Get(), "TRACK WETNESS: ", Wetness(status.TrackWetness),
            textX, y + 6f, line, labelColor, wetnessColor);
        string rubber = string.IsNullOrWhiteSpace(status.TrackRubberState) ? "—" : status.TrackRubberState!.ToUpperInvariant();
        PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _labelFormat.Get(), "RUBBER: ", rubber,
            textX, y + 6f + line, line, labelColor, ValueAmber);
        PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(),
            $"TRACK {status.TrackTempC:0}°C   ·   AIR {status.AirTempC:0}°C", textX, y + 6f + line * 2, width - 74f, line, labelColor);

        // Footer: rain / humidity / wind, evenly split by thin dividers. Rain is what is falling NOW
        // (spec §8), never a forecast probability.
        float footerY = y + 6f + line * 3 + 4f;
        PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, footerY);
        var cells = new List<string> { $"RAIN {status.PrecipitationPct:0}%" };
        if (status.RelativeHumidityPct is double humidity) cells.Add($"HUMIDITY {humidity:0}%");
        if (status.WindSpeedMs is double speed)
            cells.Add($"WIND {speed * 3.6:0} km/h" + (status.WindDirectionDeg is double dir ? " " + Compass(dir) : ""));
        float cellWidth = (width - 16f) / cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            float cx = x + 8f + cellWidth * i;
            if (i > 0) PanelChrome.VerticalDivider(dc, _brush.Get(), cx, footerY + 5f, y + PanelHeightDip - 6f);
            var color = i == 0 && status.PrecipitationPct > 0 ? PaletteTokens.WeatherWet : PaletteTokens.TextPrimary;
            PanelChrome.DrawText(dc, _brush.Get(), _valueFormat.Get(), cells[i], cx, footerY + 1f, cellWidth, y + PanelHeightDip - footerY - 2f, color, TextAlignment.Center);
        }
        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private static string Compass(double degrees)
    {
        string[] names = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        int index = (int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8;
        return names[index];
    }

    /// <summary>Vector-drawn (never a bitmap emblem -- this is a generic condition pictogram, not a
    /// brand/nationality asset spec §18 governs) icon that changes with real telemetry: a sun for a
    /// genuinely dry track, a cloud once the track is damp but no rain is currently falling, and a
    /// cloud with rain streaks once <see cref="WeatherStatus.PrecipitationPct"/> is actually
    /// positive. Never animated/embellished beyond what the data supports.</summary>
    private void DrawWeatherIcon(ID2D1DeviceContext* dc, float x, float y, int wetness, double precipitationPct, float size)
    {
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
                float sx = x + size * 0.22f + i * size * 0.27f;
                var line = new RectF(sx, cy + size * 0.22f, sx + size * 0.08f, cy + size * 0.5f);
                dc->FillRectangle(&line, (ID2D1Brush*)_brush.Get());
            }
        }
        else if (wetness >= 3)
        {
            // Damp track, no active rain -- cloud only.
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
                float innerR = size * 0.38f, outerR = size * 0.52f;
                float x1 = cx + (float)(Math.Cos(angle) * innerR), y1 = cy + (float)(Math.Sin(angle) * innerR);
                float x2 = cx + (float)(Math.Cos(angle) * outerR), y2 = cy + (float)(Math.Sin(angle) * outerR);
                dc->DrawLine(new System.Numerics.Vector2(x1, y1), new System.Numerics.Vector2(x2, y2), (ID2D1Brush*)_brush.Get(), size * 0.08f, null);
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
