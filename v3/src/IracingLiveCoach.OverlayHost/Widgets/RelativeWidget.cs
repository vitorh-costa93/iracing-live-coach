using System.Globalization;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;
using static Vortice.Win32.Graphics.Direct2D.Apis;
using static Vortice.Win32.Graphics.DirectWrite.Apis;
using DWriteFactoryType = Vortice.Win32.Graphics.DirectWrite.FactoryType;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// Second real widget consuming live telemetry (Phase 3). Preset: 3 ahead, player, 3 behind
/// (spec §7's mandated 7-row preset) via <see cref="TelemetryReader.FullRelativeUpdated"/>, which
/// already produces exactly that shape -- no row-count logic duplicated here.
///
/// Columns so far: class color strip, position offset (relative to the player, "P" for the
/// player's own row), driver name, iRating, and relative gap. Deliberately NOT included per spec
/// §7: ΔiRating (Standings-only), car number/flag/brand-badge/licence (asset pipeline not wired
/// in yet, same gap as StandingsWidget), the top header band (brake bias/track temp/rubber/clock),
/// and Overtake column. No decorative title is drawn, per spec §5/§15.
///
/// Font: same "Segoe UI" placeholder as StandingsWidget -- see that class's doc comment for why.
/// </summary>
public sealed unsafe class RelativeWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly object _lock = new();
    private List<RelativeRow> _rows = new();
    private List<RelativeRow>? _simulatedRows;

    private ComPtr<IDWriteTextFormat> _nameFormat;
    private ComPtr<IDWriteTextFormat> _statusFormat;
    private ComPtr<IDWriteTextFormat> _numericFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private const float RowHeightDip = 22f;
    private const float ClassStripWidthDip = 3f;
    private const float OffsetColumnWidthDip = 30f;
    private const float FlagColumnWidthDip = 22f;
    private const float BrandColumnWidthDip = 64f;
    private const float NameColumnWidthDip = 150f;
    private const float IRatingColumnWidthDip = 56f;
    private const float GapColumnWidthDip = 60f;
    private const float ColumnGapDip = 6f;

    public RelativeWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory)
    {
        ComPtr<IDWriteTextFormat> nameFormat = dwriteFactory->CreateTextFormat("Segoe UI", 13f, fontWeight: FontWeight.Medium, localeName: "en-us");
        ThrowIfFailed(nameFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(nameFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _nameFormat = nameFormat;

        ComPtr<IDWriteTextFormat> statusFormat = dwriteFactory->CreateTextFormat("Segoe UI", 13f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(statusFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _statusFormat = statusFormat;

        ComPtr<IDWriteTextFormat> numericFormat = dwriteFactory->CreateTextFormat("Segoe UI", 13f, fontWeight: FontWeight.Medium, localeName: "en-us");
        ThrowIfFailed(numericFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(numericFormat.Get()->SetTextAlignment(TextAlignment.Trailing));
        ThrowIfFailed(numericFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _numericFormat = numericFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.FullRelativeUpdated += OnFullRelativeUpdated;
        _telemetry.Start();
    }

    private void OnFullRelativeUpdated(List<RelativeRow> rows)
    {
        lock (_lock) { _rows = rows; }
    }

    /// <summary>Spec §12's simulation preview -- see StandingsWidget.SetSimulatedRows for the same rationale.</summary>
    public void SetSimulatedRows(List<RelativeRow>? rows) => _simulatedRows = rows;

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    /// <summary>Draws at (x, y). Must be called between <see cref="DeviceResources.BeginFrame"/> and
    /// <see cref="DeviceResources.EndFrame"/>.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y)
    {
        if (_simulatedRows is { } simulated)
        {
            SetBrushColor(PaletteTokens.Warning);
            const string simLabel = "SIMULAÇÃO";
            fixed (char* p = simLabel)
            {
                var rect = new RectF(x, y - 16, x + 200, y);
                dc->DrawText(p, (uint)simLabel.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            float simRowY = y;
            foreach (var row in simulated) { DrawRow(dc, x, simRowY, row); simRowY += RowHeightDip; }
            return;
        }

        List<RelativeRow> rows;
        lock (_lock) { rows = _rows; }

        if (!_telemetry.HasRecentTelemetry || rows.Count == 0)
        {
            SetBrushColor(PaletteTokens.TextDisabled);
            string text = "Aguardando iRacing...";
            fixed (char* p = text)
            {
                var rect = new RectF(x, y, x + NameColumnWidthDip + OffsetColumnWidthDip + IRatingColumnWidthDip, y + RowHeightDip);
                dc->DrawText(p, (uint)text.Length, _nameFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            return;
        }

        float rowY = y;
        foreach (var row in rows)
        {
            DrawRow(dc, x, rowY, row);
            rowY += RowHeightDip;
        }
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, RelativeRow row)
    {
        var stripColor = ParseHexOrFallback(row.ClassColorHex, PaletteTokens.ClassUnidentified);
        SetBrushColor(stripColor);
        var stripRect = new RectF(x, y, x + ClassStripWidthDip, y + RowHeightDip);
        dc->FillRectangle(&stripRect, (ID2D1Brush*)_brush.Get());

        float cursorX = x + ClassStripWidthDip + 4f;

        // Position offset: "P" for the player's own row (never a fabricated "0"/"+0"), otherwise a
        // signed offset (spec §7 preset: 3 ahead as negative, 3 behind as positive, centered on the player).
        SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextSecondary);
        string offsetText = row.IsPlayer ? "P" : row.PositionOffset.ToString("+0;-0", CultureInfo.InvariantCulture);
        fixed (char* p = offsetText)
        {
            var rect = new RectF(cursorX, y, cursorX + OffsetColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)offsetText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += OffsetColumnWidthDip;

        // Flag + brand -- same reuse as StandingsWidget (CountryFlags.ToEmoji already-resolved
        // emoji, plain-text brand fallback matching V2's BrandIcons.cs behavior).
        SetBrushColor(PaletteTokens.TextPrimary);
        string flag = row.FlagEmoji;
        fixed (char* p = flag)
        {
            var rect = new RectF(cursorX, y, cursorX + FlagColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)flag.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.EnableColorFont, MeasuringMode.Natural);
        }
        cursorX += FlagColumnWidthDip;

        SetBrushColor(PaletteTokens.TextSecondary);
        string brand = row.ManufacturerBadge;
        fixed (char* p = brand)
        {
            var rect = new RectF(cursorX, y, cursorX + BrandColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)brand.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += BrandColumnWidthDip;

        SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
        string name = row.DriverCode;
        fixed (char* p = name)
        {
            var rect = new RectF(cursorX, y, cursorX + NameColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)name.Length, _nameFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += NameColumnWidthDip + ColumnGapDip;

        // iRating only -- no delta here (spec §7: "Não inclua ΔiRating neste widget").
        SetBrushColor(PaletteTokens.TextSecondary);
        string iratingText = row.IRating.ToString("N0", CultureInfo.InvariantCulture);
        fixed (char* p = iratingText)
        {
            var rect = new RectF(cursorX, y, cursorX + IRatingColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)iratingText.Length, _numericFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += IRatingColumnWidthDip + ColumnGapDip;

        // Relative gap -- player's own row always shows a neutral 0.000, never a computed value.
        SetBrushColor(row.IsPlayer ? PaletteTokens.NeutralDeltaOrGap : PaletteTokens.TextPrimary);
        string gapText = row.IsPlayer ? "0.000" : row.GapSeconds is double gap ? gap.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) : "—";
        fixed (char* p = gapText)
        {
            var rect = new RectF(cursorX, y, cursorX + GapColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)gapText.Length, _numericFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private static Color4 ParseHexOrFallback(string? hex, Color4 fallback)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return fallback;
        try
        {
            var span = hex.AsSpan().TrimStart('#');
            byte r = byte.Parse(span[..2], System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(span.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(span.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
            return new Color4(r / 255f, g / 255f, b / 255f, 1f);
        }
        catch { return fallback; }
    }

    public void Dispose()
    {
        _telemetry.FullRelativeUpdated -= OnFullRelativeUpdated;
        _telemetry.Dispose();
        _brush.Dispose();
        _numericFormat.Dispose();
        _statusFormat.Dispose();
        _nameFormat.Dispose();
    }
}
