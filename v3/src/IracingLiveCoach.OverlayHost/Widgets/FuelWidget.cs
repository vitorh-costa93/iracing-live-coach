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
    private ComPtr<IDWriteTextFormat> _bigFormat;
    private ComPtr<IDWriteTextFormat> _unitFormat;

    private SessionStatus? _session;
    private SessionStatus? _simulatedSession;
    public void SetSimulatedSession(SessionStatus? session) => _simulatedSession = session;

    private static readonly Color4 PumpYellow = new(1.0f, 0.78f, 0.17f, 1f);
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 310f;
    private const float PanelHeightDip = 116f;

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
        _telemetry.SessionStatusUpdated += OnSessionStatusUpdated;
        _telemetry.Start();
    }

    private ComPtr<IDWriteTextFormat> MakeFormat(float size, FontWeight weight)
    {
        ComPtr<IDWriteTextFormat> format = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, size * _appearance.FontScale, fontWeight: weight, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(format.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(format.Get()->SetWordWrapping(WordWrapping.NoWrap));
        return format;
    }

    private void CreateTextFormats()
    {
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _bigFormat.Dispose();
        _unitFormat.Dispose();
        _labelFormat = MakeFormat(15f, FontWeight.SemiBold);
        _valueFormat = MakeFormat(24f, FontWeight.SemiBold);
        _bigFormat = MakeFormat(32f, FontWeight.SemiBold);
        _unitFormat = MakeFormat(13f, FontWeight.Medium);
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

    private void OnSessionStatusUpdated(SessionStatus status)
    {
        lock (_lock) { _session = status; }
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
        SessionStatus? session;
        lock (_lock) { status = _simulatedStatus ?? _status; session = _simulatedSession ?? _session; }

        var panel = new RectF(x, y, x + width, y + PanelHeightDip);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.PanelBackground);

        if (_simulatedStatus is null && (!_telemetry.HasRecentTelemetry || status is null))
        {
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "Waiting for iRacing...", x + 12f, y, width - 24f, PanelHeightDip, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
            return;
        }

        // Effective per-lap figure per spec §9's consumption-source choice, with the configured
        // reserve subtracted from the displayed autonomy (never negative: a driver already below the
        // reserve needs "0.0 laps", not a confusing negative number).
        double? litersPerLap = ResolveLitersPerLap(status!);
        // Kapps' "Laps Remain": counted from the start of the lap in progress (FuelLapsRemain).
        double? rawLapsRemaining = FuelLapsRemain.Compute(status!.FuelLevelLiters, litersPerLap, status.PlayerLapDistPct);
        double? fuelLaps = rawLapsRemaining is double raw ? Math.Max(0, raw - _config.ReserveLaps) : null;
        double? raceLapsLeft = status!.RaceLapsRemaining;

        float rowTop = y + 4f;
        const float topRowHeight = 54f;
        float divA = x + width * 0.46f;
        float divB = x + width * 0.73f;

        // Row 1: pump + level | autonomy in laps | race laps left.
        DrawFuelPumpIcon(dc, x + 14f, rowTop + 10f);
        string level = $"{status!.FuelLevelLiters:0.0}";
        float levelWidth = PanelChrome.MeasureWidth(_dwriteFactory, _bigFormat.Get(), level);
        PanelChrome.DrawText(dc, _brush.Get(), _bigFormat.Get(), level, x + 52f, rowTop, levelWidth + 4f, topRowHeight, PaletteTokens.TextPrimary);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "L", x + 52f + levelWidth + 3f, rowTop + 5f, 20f, topRowHeight, PaletteTokens.TextPrimary);

        PanelChrome.VerticalDivider(dc, _brush.Get(), divA, rowTop + 8f, rowTop + topRowHeight - 4f);
        PanelChrome.VerticalDivider(dc, _brush.Get(), divB, rowTop + 8f, rowTop + topRowHeight - 4f);
        DrawStacked(dc, divA, divB, rowTop, fuelLaps is double laps ? $"{laps:0.0}" : "—", "laps");
        DrawStacked(dc, divB, x + width, rowTop, raceLapsLeft is double left ? left.ToString("0.0", CultureInfo.InvariantCulture) : "—", status.RaceLapsEstimated ? "≈ left" : "left");

        // Row 2: last / average / max consumption per lap.
        float row2 = rowTop + topRowHeight + 4f;
        PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, row2 - 2f);
        DrawFooterRow(dc, x, width, row2,
            [("Last", Liters(status.LastLapFuelUsedLiters), PaletteTokens.TextPrimary),
             ("Avg", Liters(status.AverageFuelPerLapLiters), PaletteTokens.TextPrimary),
             ("Max", Liters(status.MaxFuelPerLapLiters), PaletteTokens.TextPrimary)]);

        // Row 3: total needed to finish and the margin over it. Core reports "additional fuel needed"
        // (negative = surplus), so the total is level + needed and the margin is its negation. Only
        // shown when calculable (spec §9: never fabricate a number from zero samples).
        float row3 = row2 + 26f;
        if (status.FuelNeededForFinishLiters is double needed)
        {
            // Short of fuel: how much to add (Kapps' "Refuel"); enough: the surplus at the flag.
            double margin = -needed;
            var last = margin >= 0
                ? ("Margin", $"+{margin:0.0} L", PaletteTokens.PositiveDelta)
                : ("Refuel", $"{-margin:0.0} L", PaletteTokens.NegativeDelta);
            DrawFooterRow(dc, x, width, row3,
                [("To finish", $"{status.FuelLevelLiters + needed:0.0} L", PaletteTokens.TextPrimary), last]);
        }
        else
        {
            DrawFooterRow(dc, x, width, row3, [("To finish", "—", PaletteTokens.TextDisabled), ("Margin", "—", PaletteTokens.TextDisabled)]);
        }
        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private static string Liters(double? value) => value is double v ? v.ToString("0.00", CultureInfo.InvariantCulture) : "—";

    /// <summary>A large value with its small unit label centred underneath, inside [left, right].</summary>
    private void DrawStacked(ID2D1DeviceContext* dc, float left, float right, float top, string value, string unit)
    {
        PanelChrome.DrawText(dc, _brush.Get(), _valueFormat.Get(), value, left, top + 2f, right - left, 30f, PaletteTokens.TextPrimary, TextAlignment.Center);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), unit, left, top + 30f, right - left, 18f, PaletteTokens.TextSecondary, TextAlignment.Center);
    }

    /// <summary>One footer line: N cells of "label value" split by thin vertical dividers.</summary>
    private void DrawFooterRow(ID2D1DeviceContext* dc, float x, float width, float y, (string Label, string Value, Color4 ValueColor)[] cells)
    {
        float cellWidth = (width - 16f) / cells.Length;
        for (int i = 0; i < cells.Length; i++)
        {
            float cellX = x + 8f + cellWidth * i;
            if (i > 0) PanelChrome.VerticalDivider(dc, _brush.Get(), cellX, y + 4f, y + 22f);
            string text = cells[i].Label + " " + cells[i].Value;
            float textWidth = PanelChrome.MeasureWidth(_dwriteFactory, _labelFormat.Get(), text);
            float start = cellX + Math.Max(4f, (cellWidth - textWidth) / 2f);
            PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _labelFormat.Get(), cells[i].Label + " ", cells[i].Value, start, y, 26f,
                PaletteTokens.TextSecondary, cells[i].ValueColor);
        }
    }

    /// <summary>Vector fuel-pump pictogram in the mockups' yellow: a body with a display window, a
    /// hose and a nozzle. Static -- there is no real-time condition for it to react to.</summary>
    private void DrawFuelPumpIcon(ID2D1DeviceContext* dc, float x, float y)
    {
        SetBrushColor(PumpYellow);
        var body = new RectF(x, y, x + 20f, y + 32f);
        var rounded = new RoundedRect { rect = body, radiusX = 3f, radiusY = 3f };
        dc->FillRoundedRectangle(&rounded, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.PanelBackground);
        var window = new RectF(x + 4f, y + 5f, x + 16f, y + 13f);
        dc->FillRectangle(&window, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PumpYellow);
        dc->DrawLine(new System.Numerics.Vector2(x + 20f, y + 10f), new System.Numerics.Vector2(x + 27f, y + 10f), (ID2D1Brush*)_brush.Get(), 2.4f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 27f, y + 10f), new System.Numerics.Vector2(x + 27f, y + 24f), (ID2D1Brush*)_brush.Get(), 2.4f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 24.5f, y + 24f), new System.Numerics.Vector2(x + 29.5f, y + 24f), (ID2D1Brush*)_brush.Get(), 2.4f, null);
        var foot = new RectF(x - 2f, y + 32f, x + 22f, y + 35f);
        dc->FillRectangle(&foot, (ID2D1Brush*)_brush.Get());
    }

    public void Dispose()
    {
        _telemetry.FuelUpdated -= OnFuelUpdated;
        _telemetry.SessionStatusUpdated -= OnSessionStatusUpdated;
        _telemetry.Dispose();
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _bigFormat.Dispose();
        _unitFormat.Dispose();
        _brush.Dispose();
    }
}
