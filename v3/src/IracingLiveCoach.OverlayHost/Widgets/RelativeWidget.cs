using System.Globalization;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Assets;
using IracingLiveCoach.OverlayHost.Layout;
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
/// Font: bundled Barlow Semi Condensed, registered privately by the V3 host.
/// </summary>
public sealed unsafe class RelativeWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly FlagBitmapCache _flags;
    private readonly object _lock = new();
    private List<RelativeRow> _rows = new();
    private List<RelativeRow>? _simulatedRows;
    private SessionStatus? _sessionStatus;
    private PlayerCarStatus? _playerStatus;

    private ComPtr<IDWriteTextFormat> _nameFormat;
    private ComPtr<IDWriteTextFormat> _statusFormat;
    private ComPtr<IDWriteTextFormat> _numericFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;
    private float RowHeightDip => (_appearance.RowHeightDip > 0 ? _appearance.RowHeightDip : BaseRowHeightDip) + _appearance.RowSpacingDip;

    private const float BaseRowHeightDip = 22f;
    private const float HeaderHeightDip = 18f;
    private const float ClassStripWidthDip = 3f;
    private const float OffsetColumnWidthDip = 30f;
    private const float CarNumberColumnWidthDip = 38f;
    private const float FlagColumnWidthDip = 22f;
    private const float BrandColumnWidthDip = 64f;
    private const float NameColumnWidthDip = 150f;
    private const float LicenseColumnWidthDip = 40f;
    private const float IRatingColumnWidthDip = 56f;
    private const float GapColumnWidthDip = 60f;
    private const float OvertakeColumnWidthDip = 52f;
    private const float ColumnGapDip = 6f;

    private const float ColumnsLeftMarginDip = ClassStripWidthDip + 4f;

    /// <summary>Live, user-configurable column set -- same rationale as StandingsWidget's own
    /// <see cref="StandingsWidget.BuildDefaultColumns"/>.</summary>
    private List<ColumnDefinition> _columns = BuildDefaultColumns();

    public static List<ColumnDefinition> BuildDefaultColumns() =>
    [
        new("offset", ColumnWidthMode.Fixed, OffsetColumnWidthDip, OffsetColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 0),
        new("carNumber", ColumnWidthMode.Fixed, CarNumberColumnWidthDip, CarNumberColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 1),
        new("flag", ColumnWidthMode.Fixed, FlagColumnWidthDip, FlagColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 2),
        new("brand", ColumnWidthMode.Fixed, BrandColumnWidthDip, BrandColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 3),
        new("name", ColumnWidthMode.Flexible, NameColumnWidthDip, 60f, ColumnAlignment.Left, 0, ColumnGapDip, true, 4),
        new("license", ColumnWidthMode.Fixed, LicenseColumnWidthDip, LicenseColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 5),
        new("irating", ColumnWidthMode.Fixed, IRatingColumnWidthDip, IRatingColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 6),
        new("gap", ColumnWidthMode.Fixed, GapColumnWidthDip, GapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 7, DecimalPlaces: 3),
        new("overtake", ColumnWidthMode.Fixed, OvertakeColumnWidthDip, OvertakeColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 8),
    ];

    public void SetColumns(List<ColumnDefinition> columns) => _columns = columns;
    public IReadOnlyList<ColumnDefinition> Columns => _columns;

    private NumberFormatConfig _numberFormatConfig = NumberFormatConfig.Default;
    public void SetNumberFormat(NumberFormatConfig config) => _numberFormatConfig = config;

    /// <summary>Sum of every visible column's footprint plus the left margin -- replaces the
    /// previous hardcoded constant (spec §12's auto-width formula).</summary>
    private float TableWidthDip => ColumnsLeftMarginDip + WidgetLayoutEngine.SumVisibleColumnFootprints(_columns);

    public RelativeWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags, IDWriteFontCollection1* fontCollection = null)
    {
        _flags = flags;
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.FullRelativeUpdated += OnFullRelativeUpdated;
        _telemetry.SessionStatusUpdated += OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated += OnPlayerCarStatusUpdated;
        _telemetry.Start();
    }

    private void CreateTextFormats()
    {
        _nameFormat.Dispose();
        _statusFormat.Dispose();
        _numericFormat.Dispose();

        float scale = _appearance.FontScale;
        ComPtr<IDWriteTextFormat> nameFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 15f * scale, fontWeight: FontWeight.Medium, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(nameFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(nameFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _nameFormat = nameFormat;

        ComPtr<IDWriteTextFormat> statusFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 14f * scale, fontWeight: FontWeight.SemiBold, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(statusFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _statusFormat = statusFormat;

        ComPtr<IDWriteTextFormat> numericFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 14f * scale, fontWeight: FontWeight.Medium, fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(numericFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(numericFormat.Get()->SetTextAlignment(TextAlignment.Trailing));
        ThrowIfFailed(numericFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _numericFormat = numericFormat;
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    private void OnFullRelativeUpdated(List<RelativeRow> rows)
    {
        lock (_lock) { _rows = rows; }
    }

    private void OnSessionStatusUpdated(SessionStatus status)
    {
        lock (_lock) { _sessionStatus = status; }
    }

    private void OnPlayerCarStatusUpdated(PlayerCarStatus status)
    {
        lock (_lock) { _playerStatus = status; }
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
            DrawHeader(dc, x, y, null, null);
            float simRowY = y + HeaderHeightDip;
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

        SessionStatus? session;
        PlayerCarStatus? player;
        lock (_lock) { session = _sessionStatus; player = _playerStatus; }
        DrawHeader(dc, x, y, session, player);
        float rowY = y + HeaderHeightDip;
        bool drewAnyRow = false;

        // Graphite translucent body background (spec §5/§16) behind every row -- previously this
        // table had no fill at all, just text floating over the desktop/game.
        SetBrushColor(PaletteTokens.OverlayBackground);
        var bodyBackground = new RectF(x, rowY, x + TableWidthDip, rowY + rows.Count * RowHeightDip);
        dc->FillRectangle(&bodyBackground, (ID2D1Brush*)_brush.Get());

        foreach (var row in rows)
        {
            if (drewAnyRow)
            {
                SetBrushColor(PaletteTokens.Grid);
                var separator = new RectF(x, rowY - PaletteTokens.BorderAndGridThicknessPx, x + TableWidthDip, rowY);
                dc->FillRectangle(&separator, (ID2D1Brush*)_brush.Get());
            }
            DrawRow(dc, x, rowY, row);
            rowY += RowHeightDip;
            drewAnyRow = true;
        }
        SetBrushColor(PaletteTokens.WidgetOuterBorder);
        var outer = new RectF(x, y, x + TableWidthDip, rowY);
        dc->DrawRectangle(&outer, (ID2D1Brush*)_brush.Get(), PaletteTokens.BorderAndGridThicknessPx, null);
    }

    private void DrawHeader(ID2D1DeviceContext* dc, float x, float y, SessionStatus? session, PlayerCarStatus? player)
    {
        SetBrushColor(PaletteTokens.SessionHeaderBand);
        var band = new RectF(x, y, x + TableWidthDip, y + HeaderHeightDip);
        dc->FillRectangle(&band, (ID2D1Brush*)_brush.Get());
        // Spec §5/§15: no decorative "RELATIVE" title -- only real session/player info, distributed
        // across the header's full width (BB / track temp / rubber on the left, LOCAL clock right).
        if (session is not null)
        {
            string sessionText = $"{session.CarClassShortName}  {session.SessionTypeText}  LAP {session.CurrentLap?.ToString(CultureInfo.InvariantCulture) ?? "—"}/{session.TotalLaps?.ToString(CultureInfo.InvariantCulture) ?? "—"}";
            SetBrushColor(PaletteTokens.TextPrimary);
            fixed (char* p = sessionText)
            {
                var rect = new RectF(x + 8f, y, x + TableWidthDip * 0.5f, y + HeaderHeightDip);
                dc->DrawText(p, (uint)sessionText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
        if (player is not null)
        {
            // spec §7: brake bias / track temp / rubber (emborrachamento), spec §8: rubber is
            // independent from humidity/wetness -- shown here as its own field, not folded in.
            var bias = player.BrakeBiasPct is double b ? $"BB {b:0.0}%" : null;
            var track = player.TrackTempC is double t ? $"TRACK {t:0.#}°C" : null;
            var rubber = player.TrackRubberState is { Length: > 0 } r ? $"RUBBER {r.ToUpperInvariant()}" : null;
            string playerText = string.Join("   ", new[] { bias, track, rubber }.Where(s => s is not null));
            if (playerText.Length > 0)
            {
                SetBrushColor(PaletteTokens.TextSecondary);
                fixed (char* p = playerText)
                {
                    var rect = new RectF(x + TableWidthDip * 0.5f, y, x + TableWidthDip - 76f, y + HeaderHeightDip);
                    dc->DrawText(p, (uint)playerText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
                }
            }
        }
        string local = "LOCAL " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        SetBrushColor(PaletteTokens.TextSecondary);
        fixed (char* p = local)
        {
            var rect = new RectF(x + TableWidthDip - 76f, y, x + TableWidthDip - 6f, y + HeaderHeightDip);
            dc->DrawText(p, (uint)local.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, RelativeRow row)
    {
        var stripColor = PaletteTokens.ResolveClassColor(row.CarClassId, row.ClassShortName, row.ClassColorHex);
        SetBrushColor(stripColor);
        var stripRect = new RectF(x, y, x + ClassStripWidthDip, y + RowHeightDip);
        dc->FillRectangle(&stripRect, (ID2D1Brush*)_brush.Get());

        float rowLeft = x + ColumnsLeftMarginDip;
        var layout = WidgetLayoutEngine.LayoutTable(_columns, rowCount: 1, RowHeightDip, 0, 0, 0, float.MaxValue, float.MaxValue);
        foreach (var placement in layout.Columns)
        {
            float cellX = rowLeft + placement.OffsetXPx;
            float cellWidth = placement.ResolvedWidthPx;
            switch (placement.Column.Key)
            {
                case "offset":
                    // "P" for the player's own row (never a fabricated "0"/"+0"), otherwise a signed
                    // offset (spec §7 preset: 3 ahead negative, 3 behind positive, centered on the player).
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextSecondary);
                    DrawCell(dc, row.IsPlayer ? "P" : row.PositionOffset.ToString("+0;-0", CultureInfo.InvariantCulture), cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
                case "carNumber":
                    SetBrushColor(PaletteTokens.TextSecondary);
                    DrawCell(dc, string.IsNullOrWhiteSpace(row.CarNumber) ? "—" : $"#{row.CarNumber}", cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
                case "flag":
                    var flag = _flags.Find(row.FlagEmoji);
                    if (flag != null)
                    {
                        var box = new RectF(cellX + 1f, y + 4f, cellX + cellWidth - 1f, y + RowHeightDip - 4f);
                        var destination = FlagBitmapCache.Contain(flag, box);
                        dc->DrawBitmap(flag, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
                    }
                    break;
                case "brand":
                    var brandBitmap = _flags.FindBrand(row.ManufacturerBadge);
                    if (brandBitmap != null)
                    {
                        var box = new RectF(cellX + 2f, y + 3f, cellX + cellWidth - 2f, y + RowHeightDip - 3f);
                        var destination = FlagBitmapCache.Contain(brandBitmap, box);
                        dc->DrawBitmap(brandBitmap, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
                    }
                    else if (!string.IsNullOrWhiteSpace(row.ManufacturerBadge))
                    {
                        SetBrushColor(PaletteTokens.TextSecondary);
                        DrawCell(dc, row.ManufacturerBadge, cellX, y, cellWidth, ColumnAlignment.Center);
                    }
                    break;
                case "name":
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, NameDisplay.Format(row.DriverCode, _numberFormatConfig.NameFormat), cellX, y, cellWidth, ColumnAlignment.Left);
                    break;
                case "license":
                    DrawLicenseBadge(dc, cellX, y, cellWidth, row.LicString, row.LicColorHex);
                    break;
                case "irating":
                    // iRating only -- no delta here (spec §7: "Não inclua ΔiRating neste widget").
                    SetBrushColor(PaletteTokens.TextSecondary);
                    DrawCell(dc, _numberFormatConfig.FormatIRating(row.IRating), cellX, y, cellWidth, placement.Column.Alignment);
                    break;
                case "gap":
                    // Player's own row always shows a neutral 0.000, never a computed value.
                    SetBrushColor(row.IsPlayer ? PaletteTokens.NeutralDeltaOrGap : PaletteTokens.TextPrimary);
                    string gapText = row.IsPlayer ? "0" + DecimalSuffix(placement.Column.DecimalPlaces)
                        : row.GapSeconds is double gap ? gap.ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: true), CultureInfo.InvariantCulture) : "—";
                    DrawCell(dc, gapText, cellX, y, cellWidth, placement.Column.Alignment);
                    break;
                case "overtake":
                    DrawOvertakeCell(dc, cellX, y, cellWidth, row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown);
                    break;
            }
        }
    }

    private static string DecimalSuffix(int? decimalPlaces)
    {
        int decimals = Math.Clamp(decimalPlaces ?? 3, 0, 6);
        return decimals > 0 ? "." + new string('0', decimals) : "";
    }

    private static string DecimalFormat(int? decimalPlaces, bool signed)
    {
        string digits = DecimalSuffix(decimalPlaces);
        return signed ? $"+0{digits};-0{digits};0{digits}" : $"0{digits}";
    }

    private static TextAlignment ToDWrite(ColumnAlignment alignment) => alignment switch
    {
        ColumnAlignment.Left => TextAlignment.Leading,
        ColumnAlignment.Right => TextAlignment.Trailing,
        _ => TextAlignment.Center
    };

    /// <summary>Draws one line respecting a per-column alignment -- see StandingsWidget's identical
    /// helper for why the format's alignment is swapped per call rather than kept one-per-alignment.</summary>
    private void DrawCell(ID2D1DeviceContext* dc, string text, float x, float y, float width, ColumnAlignment alignment)
    {
        ThrowIfFailed(_statusFormat.Get()->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        ThrowIfFailed(_statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
    }

    private void DrawLicenseBadge(ID2D1DeviceContext* dc, float x, float y, float width, string license, string? colorHex)
    {
        var color = ParseHexOrFallback(colorHex, PaletteTokens.LicenseUnknown);
        SetBrushColor(color);
        var rr = new RoundedRect
        {
            rect = new RectF(x, y + 3f, x + width, y + RowHeightDip - 3f),
            radiusX = PaletteTokens.BadgeCornerRadiusPx,
            radiusY = PaletteTokens.BadgeCornerRadiusPx
        };
        dc->FillRoundedRectangle(&rr, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.TextPrimary);
        string text = string.IsNullOrWhiteSpace(license) ? "—" : _numberFormatConfig.FormatLicense(license);
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawOvertakeCell(ID2D1DeviceContext* dc, float x, float y, float width, bool? active, double? seconds, bool cooldown)
    {
        Color4 color = active is null ? PaletteTokens.OvertakeUnknown
            : active == true ? PaletteTokens.OvertakeActive
            : cooldown ? PaletteTokens.OvertakeCooldown
            : seconds is <= 0 ? PaletteTokens.OvertakeDepleted
            : PaletteTokens.OvertakeAvailable;
        string text = seconds is double s ? $"{Math.Clamp((int)Math.Round(s), 0, TelemetryReader.P2PMaxSeconds)}s" : "—";
        SetBrushColor(color);
        fixed (char* p = text)
        {
            var textRect = new RectF(x, y, x + width, y + 12f);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &textRect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        var track = new RectF(x + 2f, y + 16f, x + width - 2f, y + 19f);
        SetBrushColor(PaletteTokens.BarTrackEmpty);
        dc->FillRectangle(&track, (ID2D1Brush*)_brush.Get());
        if (seconds is not double bank) return;
        float fraction = Math.Clamp((float)(bank / TelemetryReader.P2PMaxSeconds), 0f, 1f);
        if (fraction <= 0f) return;
        var fill = new RectF(x + 2f, y + 16f, x + 2f + (width - 4f) * fraction, y + 19f);
        SetBrushColor(color);
        dc->FillRectangle(&fill, (ID2D1Brush*)_brush.Get());
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
        _telemetry.SessionStatusUpdated -= OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated -= OnPlayerCarStatusUpdated;
        _telemetry.Dispose();
        _brush.Dispose();
        _numericFormat.Dispose();
        _statusFormat.Dispose();
        _nameFormat.Dispose();
    }
}
