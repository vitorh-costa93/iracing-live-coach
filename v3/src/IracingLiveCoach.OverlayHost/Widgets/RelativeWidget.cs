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

    private const float BaseRowHeightDip = 32f;
    private const float HeaderHeightDip = 30f;
    private const float ClassStripWidthDip = 4f;
    private const float OffsetColumnWidthDip = 30f;
    private const float CarNumberColumnWidthDip = 44f;
    private const float FlagColumnWidthDip = 30f;
    private const float BrandColumnWidthDip = 34f;
    private const float NameColumnWidthDip = 118f;
    private const float LicenseColumnWidthDip = 56f;
    // Item 13: the Standings-style iRating pill, narrower than Standings' own combined iRating+Δ
    // badge since this widget never shows a delta.
    private const float IRatingColumnWidthDip = 54f;
    private const float IRatingPillHeightDip = 24f;
    private const float GapColumnWidthDip = 70f;
    private const float OvertakeColumnWidthDip = 62f;
    private const float ColumnGapDip = 6f;

    private const float ColumnsLeftMarginDip = ClassStripWidthDip + 4f;

    /// <summary>Live, user-configurable column set -- same rationale as StandingsWidget's own
    /// <see cref="StandingsWidget.BuildDefaultColumns"/>.</summary>
    private List<ColumnDefinition> _columns = BuildDefaultColumns();

    public static List<ColumnDefinition> BuildDefaultColumns() =>
    [
        new("position", ColumnWidthMode.Fixed, OffsetColumnWidthDip, OffsetColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 0),
        new("carNumber", ColumnWidthMode.Fixed, CarNumberColumnWidthDip, CarNumberColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 1),
        new("brand", ColumnWidthMode.Fixed, BrandColumnWidthDip, BrandColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 2),
        new("flag", ColumnWidthMode.Fixed, FlagColumnWidthDip, FlagColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 3),
        new("name", ColumnWidthMode.Flexible, NameColumnWidthDip, 60f, ColumnAlignment.Left, 0, ColumnGapDip, true, 4),
        new("license", ColumnWidthMode.Fixed, LicenseColumnWidthDip, LicenseColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 5),
        new("gap", ColumnWidthMode.Fixed, GapColumnWidthDip, GapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 6, DecimalPlaces: 1),
        new("irating", ColumnWidthMode.Fixed, IRatingColumnWidthDip, IRatingColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 7),
        new("overtake", ColumnWidthMode.Fixed, OvertakeColumnWidthDip, OvertakeColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 8),
    ];

    public void SetColumns(List<ColumnDefinition> columns)
    {
        _columns = columns;
        RebuildEffectiveColumns();
    }

    /// <summary>The user's columns with the appearance's horizontal padding applied to every padded
    /// column ("Padding (H)"); what layout and width actually use.</summary>
    private List<ColumnDefinition> _effectiveColumns = BuildDefaultColumns();

    private void RebuildEffectiveColumns()
    {
        var padded = _columns.Select(c => _appearance.PaddingHDip >= 0 && c.PaddingRightPx > 0 ? c with { PaddingRightPx = _appearance.PaddingHDip } : c).ToList();
        // No push-to-pass in this session: Overtake hides but the widget keeps its width -- the name takes the space.
        _effectiveColumns = _hasP2P ? padded : WidgetLayoutEngine.HideKeepingWidth(padded, "overtake");
    }

    private bool _hasP2P;
    private readonly Dictionary<(string, int), string> _nameFit = new();

    /// <summary>Item 9: the Overtake column's visibility is a per-SESSION decision, never a
    /// per-frame one -- once any row (across the FULL field this widget received from
    /// <see cref="TelemetryReader.FullRelativeUpdated"/>, not just the currently-windowed
    /// ahead/behind subset) is seen publishing push-to-pass data, the column stays reserved for the
    /// rest of the session. This is a one-way latch on purpose: a session that has push-to-pass
    /// never "blinks off" because one frame's windowed rows happened to omit every car that reports
    /// it, and a missing value on a single row never collapses the layout either.</summary>
    private void SyncP2PColumn(IEnumerable<RelativeRow> rows)
    {
        if (_hasP2P) return;
        if (!rows.Any(r => r.P2PActive is not null)) return;
        _hasP2P = true;
        RebuildEffectiveColumns();
    }

    /// <summary>The weight to use for a text format: the widget's own choice unless the appearance
    /// forces Regular (400) or SemiBold (600) -- the two bundled cuts.</summary>
    private FontWeight Weight(FontWeight own) => _appearance.FontWeight switch
    {
        >= 600 => FontWeight.SemiBold,
        > 0 => FontWeight.Regular,
        _ => own
    };
    public IReadOnlyList<ColumnDefinition> Columns => _columns;

    private NumberFormatConfig _numberFormatConfig = NumberFormatConfig.Default;
    public void SetNumberFormat(NumberFormatConfig config) => _numberFormatConfig = config;

    /// <summary>Sum of every visible column's footprint plus the left margin -- replaces the
    /// previous hardcoded constant (spec §12's auto-width formula).</summary>
    private float TableWidthDip => ColumnsLeftMarginDip + WidgetLayoutEngine.SumVisibleColumnFootprints(_effectiveColumns);

    public RelativeWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags, IDWriteFontCollection1* fontCollection = null)
    {
        _flags = flags;
        _dwriteFactory = dwriteFactory;
        RebuildEffectiveColumns();
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
        _nameFit.Clear();
        _nameFormat.Dispose();
        _statusFormat.Dispose();
        _numericFormat.Dispose();

        float scale = _appearance.FontScale;
        ComPtr<IDWriteTextFormat> nameFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 17f * scale, fontWeight: Weight(FontWeight.Medium), fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(nameFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(nameFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _nameFormat = nameFormat;

        ComPtr<IDWriteTextFormat> statusFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 15.5f * scale, fontWeight: Weight(FontWeight.SemiBold), fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(statusFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _statusFormat = statusFormat;

        ComPtr<IDWriteTextFormat> numericFormat = _dwriteFactory->CreateTextFormat("Barlow", (IDWriteFontCollection*)_fontCollection, 16f * scale, fontWeight: Weight(FontWeight.Medium), fontStretch: FontStretch.SemiCondensed, localeName: "en-us");
        ThrowIfFailed(numericFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(numericFormat.Get()->SetTextAlignment(TextAlignment.Trailing));
        ThrowIfFailed(numericFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _numericFormat = numericFormat;
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight;
        _appearance = appearance;
        RebuildEffectiveColumns();
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
            const string simLabel = "SIMULATION";
            fixed (char* p = simLabel)
            {
                var rect = new RectF(x, y - 16, x + 200, y);
                dc->DrawText(p, (uint)simLabel.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            SyncP2PColumn(simulated);
            DrawPanel(dc, x, y, simulated.Where(r => _relativeRules.Includes(r.PositionOffset)).ToList(), _simulatedSession, _simulatedPlayer);
            return;
        }

        List<RelativeRow> rows;
        lock (_lock) { rows = _rows; }
        // Item 9: decide on the FULL field received this tick, before it gets windowed down to the
        // configured ahead/behind rows below -- see SyncP2PColumn's own doc comment.
        SyncP2PColumn(rows);

        if (!_telemetry.HasRecentTelemetry || rows.Count == 0)
        {
            LastDrawnSize = (TableWidthDip, RowHeightDip);
            SetBrushColor(PaletteTokens.TextDisabled);
            string text = "Waiting for iRacing...";
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
        DrawPanel(dc, x, y, rows.Where(r => r.IsPlayer || _relativeRules.Includes(r.PositionOffset)).ToList(), session, player);
    }

    /// <summary>See StandingsWidget.LastDrawnSize.</summary>
    public (float Width, float Height) LastDrawnSize { get; private set; } = (470f, 100f);

    private RelativeRules _relativeRules = RelativeRules.Default;

    /// <summary>Spec §12 "Relative (acima) / (abaixo)": how many cars each side to show, live.</summary>
    public void SetRelativeRules(RelativeRules rules) => _relativeRules = rules.Clamped();

    private SessionStatus? _simulatedSession;
    private PlayerCarStatus? _simulatedPlayer;
    public void SetSimulatedSession(SessionStatus? session, PlayerCarStatus? player)
    {
        _simulatedSession = session;
        _simulatedPlayer = player;
    }

    /// <summary>Fixed-size table: always the configured number of rows ahead and behind (Control
    /// Center "Relative acima / abaixo"), so the widget never resizes when fewer cars are near --
    /// a slot with no car is drawn as an empty row.</summary>
    private void DrawPanel(ID2D1DeviceContext* dc, float x, float y, IReadOnlyList<RelativeRow> rows, SessionStatus? session, PlayerCarStatus? player)
    {
        // P2P column visibility is already decided in Draw() from the full (unwindowed) field -- see
        // SyncP2PColumn's doc comment for why that must not be redone here from this already-windowed subset.
        var slots = new List<RelativeRow?>();
        for (int offset = -_relativeRules.Ahead; offset <= _relativeRules.Behind; offset++)
            slots.Add(offset == 0 ? rows.FirstOrDefault(r => r.IsPlayer) : rows.FirstOrDefault(r => !r.IsPlayer && r.PositionOffset == offset));

        float height = HeaderHeightDip + slots.Count * RowHeightDip;
        LastDrawnSize = (TableWidthDip, height);
        var panel = new RectF(x, y, x + TableWidthDip, y + height);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.PanelBackground);
        using (PanelChrome.PushClip(dc, panel))
        {
            DrawHeader(dc, x, y, session, player);
            float rowY = y + HeaderHeightDip;
            for (int i = 0; i < slots.Count; i++)
            {
                var row = slots[i];
                if (i > 0 && row is not { IsPlayer: true } && slots[i - 1] is not { IsPlayer: true })
                {
                    SetBrushColor(PaletteTokens.PanelDivider);
                    var separator = new RectF(x + ClassStripWidthDip, rowY, x + TableWidthDip, rowY + 1f);
                    dc->FillRectangle(&separator, (ID2D1Brush*)_brush.Get());
                }
                if (row is not null) DrawRow(dc, x, rowY, row);
                rowY += RowHeightDip;
            }
        }
        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    private List<HeaderFieldConfig> _headerFields = HeaderFields.DefaultRelative();
    public void SetHeaderFields(List<HeaderFieldConfig> fields) => _headerFields = HeaderFields.Complete(fields);

    /// <summary>Header band: the user-configured fields (spec §12) in equal cells, separated by thin
    /// vertical dividers as in the mockups ("BB 54.5% | TRACK 29°C | LOCAL 23:42"). Fields without data
    /// yet are skipped (spec §15: never a placeholder).</summary>
    private void DrawHeader(ID2D1DeviceContext* dc, float x, float y, SessionStatus? session, PlayerCarStatus? player)
    {
        SetBrushColor(PaletteTokens.PanelHeaderBand);
        var band = new RectF(x, y, x + TableWidthDip, y + HeaderHeightDip);
        dc->FillRectangle(&band, (ID2D1Brush*)_brush.Get());

        var texts = _headerFields.Where(f => f.Visible)
            .Select(f => HeaderFields.Text(f.Key, session, player, DateTime.Now))
            .Where(t => t is not null).Select(t => t!).ToList();
        if (texts.Count == 0) return;

        float left = x + ClassStripWidthDip + 4f;
        float cellWidth = (x + TableWidthDip - 6f - left) / texts.Count;
        for (int i = 0; i < texts.Count; i++)
        {
            float cellX = left + cellWidth * i;
            if (i > 0) PanelChrome.VerticalDivider(dc, _brush.Get(), cellX, y + 7f, y + HeaderHeightDip - 7f);
            PanelChrome.DrawText(dc, _brush.Get(), _statusFormat.Get(), texts[i], cellX, y, cellWidth, HeaderHeightDip, PaletteTokens.TextPrimary, TextAlignment.Center);
        }
        ThrowIfFailed(_statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, RelativeRow row)
    {
        if (row.IsPlayer)
        {
            // Mockups: the player's row is a cyan-tinted, cyan-outlined rounded highlight.
            var highlight = new RectF(x + ClassStripWidthDip + 1f, y + 1f, x + TableWidthDip - 3f, y + RowHeightDip - 1f);
            PanelChrome.FillPanel(dc, _brush.Get(), highlight, PaletteTokens.PlayerRowFill, 4f);
            PanelChrome.StrokePanel(dc, _brush.Get(), highlight, PaletteTokens.PlayerRowBorder, 1f, 4f);
        }
        var stripColor = PaletteTokens.ResolveClassColor(row.CarClassId, row.ClassShortName, row.ClassColorHex, row.ClassRank);
        SetBrushColor(stripColor);
        var stripRect = new RectF(x, y, x + ClassStripWidthDip, y + RowHeightDip);
        dc->FillRectangle(&stripRect, (ID2D1Brush*)_brush.Get());

        float rowLeft = x + ColumnsLeftMarginDip;
        var layout = WidgetLayoutEngine.LayoutTable(_effectiveColumns, rowCount: 1, RowHeightDip, 0, 0, 0, float.MaxValue, float.MaxValue);
        foreach (var placement in layout.Columns)
        {
            float cellX = rowLeft + placement.OffsetXPx;
            float cellWidth = placement.ResolvedWidthPx;
            switch (placement.Column.Key)
            {
                case "position":
                    // Mockups: the real running position (overall), not an offset. Falls back to the
                    // class position when the overall one isn't known.
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    int position = row.ClassPosition > 0 ? row.ClassPosition : row.OverallPosition;
                    DrawCell(dc, position > 0 ? position.ToString(CultureInfo.InvariantCulture) : "—", cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
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
                        var box = new RectF(cellX + 1f, y + 7f, cellX + cellWidth - 1f, y + RowHeightDip - 7f);
                        var destination = FlagBitmapCache.Contain(flag, box);
                        dc->DrawBitmap(flag, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
                    }
                    break;
                case "brand":
                    var brandBitmap = _flags.FindBrand(row.ManufacturerBadge);
                    if (brandBitmap != null)
                    {
                        var box = new RectF(cellX + 2f, y + 5f, cellX + cellWidth - 2f, y + RowHeightDip - 5f);
                        var destination = FlagBitmapCache.Contain(brandBitmap, box);
                        dc->DrawBitmap(brandBitmap, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
                    }
                    else if (!string.IsNullOrWhiteSpace(row.ManufacturerBadge)
                             && PanelChrome.MeasureWidth(_dwriteFactory, _statusFormat.Get(), row.ManufacturerBadge) <= cellWidth)
                    {
                        // A make without a bundled logo: its name, but only if it fits the cell --
                        // an overflowing word (e.g. the pace car's "SAFETY") would spill over the
                        // neighbouring columns.
                        SetBrushColor(PaletteTokens.TextSecondary);
                        DrawCell(dc, row.ManufacturerBadge, cellX, y, cellWidth, ColumnAlignment.Center);
                    }
                    break;
                case "name":
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, PanelChrome.Ellipsize(_dwriteFactory, _statusFormat.Get(), NameDisplay.Format(row.DriverCode, _numberFormatConfig.NameFormat), cellWidth, _nameFit), cellX, y, cellWidth, ColumnAlignment.Left);
                    break;
                case "license":
                    DrawLicenseBadge(dc, cellX, y, cellWidth, row.LicString, row.LicColorHex);
                    break;
                case "irating":
                {
                    // Item 13: the same Standings-style pill, without the ΔiRating half (spec §7:
                    // "Não inclua ΔiRating neste widget") and at this widget's narrower column width.
                    var pill = new RectF(cellX, y + (RowHeightDip - IRatingPillHeightDip) / 2, cellX + cellWidth, y + (RowHeightDip + IRatingPillHeightDip) / 2);
                    StandingsWidget.DrawIRatingPillOnly(dc, _brush.Get(), _statusFormat.Get(), pill, row.IRating, _numberFormatConfig);
                    break;
                }
                case "gap":
                    // Player's own row always shows a neutral 0.0, never a computed value. Kapps prints the gap
                    // WITHOUT a sign (ahead/behind is the row's place), 1 decimal by default.
                    SetBrushColor(row.IsPlayer ? PaletteTokens.TextSecondary : PaletteTokens.TextPrimary);
                    string gapText = row.IsPlayer ? "0" + DecimalSuffix(placement.Column.DecimalPlaces)
                        : row.GapSeconds is double gap ? Math.Abs(gap).ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: false), CultureInfo.InvariantCulture) : "—";
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
        // Mockups: a solid blue rounded pill with bold white "A 4.12".
        // Kapps colours the pill by the licence letter (LicenseStyle); the SDK's LicColor is a decimal
        // number, not "#RRGGBB", and rendered the AI's "R" blue.
        var color = ParseHexOrFallback(LicenseStyle.ColorHex(license) ?? colorHex, PaletteTokens.SrPillBlue);
        const float pillHeight = 24f;
        var pill = new RectF(x, y + (RowHeightDip - pillHeight) / 2, x + width, y + (RowHeightDip + pillHeight) / 2);
        PanelChrome.FillPanel(dc, _brush.Get(), pill, color, 5f);
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
        StandingsWidget.DrawTimeBarPill(dc, x, y, width, RowHeightDip, active, seconds, cooldown, _numericFormat.Get(), _brush.Get());
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
