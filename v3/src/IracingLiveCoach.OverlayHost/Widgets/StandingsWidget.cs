using System.Globalization;
using IracingLiveCoach.Core;
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
/// First real widget consuming live telemetry (Phase 3). Columns so far: class color strip,
/// position, driver name, iRating+Δ combined badge (spec §6), gap-to-leader, last lap, and
/// lap-delta-vs-player. NOT yet done: interval (distinct from gap-to-leader), the SF23 Overtake
/// column, multiclass grouping/headers, Top N + player window, and flags/brand icons/badges (spec
/// §18's asset pipeline isn't wired in). No decorative title is drawn, per spec §5/§15.
///
/// Reuses <see cref="TelemetryReader"/>'s existing StandingsUpdated event and its already-resolved
/// per-row ClassColorHex/EstimatedDeltaIRating — no recalculation logic duplicated here (spec §3).
///
/// Font: Barlow Semi Condensed is registered privately by the host from bundled open-font assets;
/// it is never silently replaced with an installed system font by this code.
/// </summary>
public sealed unsafe class StandingsWidget : IDisposable
{
    private readonly TelemetryReader _telemetry;
    private readonly FlagBitmapCache _flags;
    private readonly object _lock = new();
    private List<StandingsRow> _rows = new();
    private SessionStatus? _sessionStatus;
    private PlayerCarStatus? _playerStatus;

    /// <summary>Non-null while showing fictitious data for layout verification, per spec §12's
    /// explicit requirement: "preview com dados fictícios claramente identificado como simulação,
    /// disponível sem iRacing aberto". Never used as a fallback for missing real data.</summary>
    private List<StandingsRow>? _simulatedRows;

    private ComPtr<IDWriteTextFormat> _nameFormat;
    private ComPtr<IDWriteTextFormat> _statusFormat;
    private ComPtr<IDWriteTextFormat> _numericFormat; // right-aligned, for gap/interval/lap-time/delta columns
    private ComPtr<ID2D1SolidColorBrush> _brush; // color set per-draw via SetColor; one brush reused throughout.

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;
    private float RowHeightDip => (_appearance.RowHeightDip > 0 ? _appearance.RowHeightDip : BaseRowHeightDip) + _appearance.RowSpacingDip;

    private const float BaseRowHeightDip = 24f;
    private const float ClassHeaderHeightDip = 18f;
    private const float SessionHeaderHeightDip = 18f;
    private const float ClassStripWidthDip = 3f;
    private const float PositionColumnWidthDip = 28f;
    private const float CarNumberColumnWidthDip = 38f;
    private const float NameColumnWidthDip = 150f;
    private const float LicenseColumnWidthDip = 40f;
    private const float FlagColumnWidthDip = 22f;
    private const float BrandColumnWidthDip = 64f;
    private const float BadgeWidthDip = 70f;
    private const float BadgeHeightDip = 18f;
    private const float GapColumnWidthDip = 60f;
    private const float IntervalColumnWidthDip = 60f;
    private const float LastLapColumnWidthDip = 68f;
    private const float LapDeltaColumnWidthDip = 60f;
    private const float OvertakeColumnWidthDip = 52f;
    private const float PitColumnWidthDip = 60f;
    private const float ColumnGapDip = 6f;

    /// <summary>Left margin before the first configurable column -- the class-color strip and its
    /// own small gap are never part of the reorderable/configurable column set (spec §15: "mantenha
    /// a faixa vinculada à célula de posição").</summary>
    private const float ColumnsLeftMarginDip = ClassStripWidthDip + 4f;

    /// <summary>Live, user-configurable column set (spec §12: "ativar/ocultar, reordenar... largura
    /// individual... casas decimais por campo numérico... alinhamento"). Defaults reproduce the
    /// widths/order this widget always used; <see cref="SetColumns"/> is how the Control Center's
    /// Colunas tab (once it sends updates) or a loaded profile replaces them.</summary>
    private List<ColumnDefinition> _columns = BuildDefaultColumns();

    public static List<ColumnDefinition> BuildDefaultColumns() =>
    [
        new("position", ColumnWidthMode.Fixed, PositionColumnWidthDip, PositionColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 0),
        new("carNumber", ColumnWidthMode.Fixed, CarNumberColumnWidthDip, CarNumberColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 1),
        new("flag", ColumnWidthMode.Fixed, FlagColumnWidthDip, FlagColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 2),
        new("brand", ColumnWidthMode.Fixed, BrandColumnWidthDip, BrandColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 3),
        new("name", ColumnWidthMode.Flexible, NameColumnWidthDip, 60f, ColumnAlignment.Left, 0, ColumnGapDip, true, 4),
        new("license", ColumnWidthMode.Fixed, LicenseColumnWidthDip, LicenseColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 5),
        new("iratingDelta", ColumnWidthMode.Fixed, BadgeWidthDip, BadgeWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 6),
        new("gap", ColumnWidthMode.Fixed, GapColumnWidthDip, GapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 7, DecimalPlaces: 3),
        new("interval", ColumnWidthMode.Fixed, IntervalColumnWidthDip, IntervalColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 8, DecimalPlaces: 3),
        new("lastLap", ColumnWidthMode.Fixed, LastLapColumnWidthDip, LastLapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 9, DecimalPlaces: 3),
        new("lapDelta", ColumnWidthMode.Fixed, LapDeltaColumnWidthDip, LapDeltaColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 10, DecimalPlaces: 3),
        new("pit", ColumnWidthMode.Fixed, PitColumnWidthDip, PitColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, false, 11),
        new("overtake", ColumnWidthMode.Fixed, OvertakeColumnWidthDip, OvertakeColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 12),
    ];

    /// <summary>Spec §12: every column-config change (reorder/width/visibility/decimals/alignment)
    /// applies live, no restart. Called from the Control Center's IPC handler.</summary>
    public void SetColumns(List<ColumnDefinition> columns) => _columns = columns;

    public IReadOnlyList<ColumnDefinition> Columns => _columns;

    /// <summary>Spec §6/§12: "Top N fixo configurável por classe, combinado com janela em torno do
    /// jogador, sem duplicações" -- <see cref="StandingsSelection.GroupAndSelect"/> already
    /// implements the dedup/window logic; this just lets the Control Center's Regras tab change the
    /// numbers it runs with, live.</summary>
    private StandingsPresentationOptions _presentationOptions = StandingsPresentationOptions.Default;
    public void SetPresentationOptions(StandingsPresentationOptions options) => _presentationOptions = options;

    /// <summary>Spec §12: "formato de número para iRating e Safety Rating" -- applied to the
    /// iRating badge and the license/SR badge, live.</summary>
    private NumberFormatConfig _numberFormatConfig = NumberFormatConfig.Default;
    public void SetNumberFormat(NumberFormatConfig config) => _numberFormatConfig = config;

    /// <summary>Sum of every visible column's footprint plus the left margin -- replaces the
    /// previous hardcoded constant so header band/border/grid always agree with whatever the live
    /// column configuration actually is (spec §12's auto-width formula).</summary>
    private float TableWidth => ColumnsLeftMarginDip + WidgetLayoutEngine.SumVisibleColumnFootprints(_columns);

    public StandingsWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags, IDWriteFontCollection1* fontCollection = null)
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
        _telemetry.StandingsUpdated += OnStandingsUpdated;
        _telemetry.SessionStatusUpdated += OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated += OnPlayerCarStatusUpdated;
        _telemetry.Start();
    }

    /// <summary>(Re)builds every owned text format at its tuned base size times the current
    /// <see cref="_appearance"/> font scale. Called from the constructor and again from
    /// <see cref="SetAppearance"/> -- a DirectWrite text format's size is immutable once created,
    /// so a live font-scale change means throwing the old ones away and creating new ones, not
    /// mutating them.</summary>
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

    /// <summary>Spec §12: font scale/row height/row spacing apply live, no restart, same pattern as
    /// <see cref="SetColumns"/>.</summary>
    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    private void OnStandingsUpdated(List<StandingsRow> rows)
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

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    /// <summary>Toggles the spec §12 simulation preview on/off -- fictitious rows for verifying
    /// layout without a live iRacing session. Pass null to return to real telemetry.</summary>
    public void SetSimulatedRows(List<StandingsRow>? rows) => _simulatedRows = rows;

    /// <summary>Draws the widget's current state at (x, y) in the device context's own coordinate
    /// space. Must be called between <see cref="DeviceResources.BeginFrame"/> and
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
            DrawSessionHeader(dc, x, y, null, null);
            DrawRows(dc, x, y + SessionHeaderHeightDip, simulated);
            return;
        }

        List<StandingsRow> rows;
        lock (_lock) { rows = _rows; }

        if (!_telemetry.HasRecentTelemetry || rows.Count == 0)
        {
            DrawWaitingState(dc, x, y);
            return;
        }

        SessionStatus? session;
        PlayerCarStatus? player;
        lock (_lock) { session = _sessionStatus; player = _playerStatus; }
        DrawSessionHeader(dc, x, y, session, player);
        DrawRows(dc, x, y + SessionHeaderHeightDip, rows);
    }

    private List<HeaderFieldConfig> _headerFields = HeaderFields.DefaultStandings();
    public void SetHeaderFields(List<HeaderFieldConfig> fields) => _headerFields = HeaderFields.Complete(fields);

    private void DrawSessionHeader(ID2D1DeviceContext* dc, float x, float y, SessionStatus? session, PlayerCarStatus? player)
    {
        SetBrushColor(PaletteTokens.SessionHeaderBand);
        var band = new RectF(x, y, x + TableWidth, y + SessionHeaderHeightDip);
        dc->FillRectangle(&band, (ID2D1Brush*)_brush.Get());
        // Spec §5/§15: no decorative widget title -- only the user-configured real header fields
        // (spec §12), in the user's chosen order; fields without data yet are skipped.
        string text = HeaderFields.Compose(_headerFields, session, player, DateTime.Now);
        if (text.Length == 0) return;
        SetBrushColor(PaletteTokens.TextPrimary);
        fixed (char* p = text)
        {
            var rect = new RectF(x + 8f, y, x + TableWidth - 6f, y + SessionHeaderHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawRows(ID2D1DeviceContext* dc, float x, float y, IReadOnlyList<StandingsRow> rows)
    {
        var groups = StandingsSelection.GroupAndSelect(rows, _presentationOptions);
        bool multiClass = groups.Count > 1;
        float headerTopY = y - SessionHeaderHeightDip;
        float rowY = y;
        bool drewAnyRow = false;

        // Graphite translucent body background (spec §5/§16's OverlayBackground) behind every row --
        // previously this table had no fill at all, just text floating over the desktop/game.
        float bodyHeight = groups.Sum(g => g.Rows.Count * RowHeightDip + (multiClass ? ClassHeaderHeightDip : 0f));
        SetBrushColor(PaletteTokens.OverlayBackground);
        var bodyBackground = new RectF(x, y, x + TableWidth, y + bodyHeight);
        dc->FillRectangle(&bodyBackground, (ID2D1Brush*)_brush.Get());
        foreach (var group in groups)
        {
            if (multiClass)
            {
                DrawClassHeader(dc, x, rowY, group.ClassShortName, group.ClassId, group.ClassColorHex);
                rowY += ClassHeaderHeightDip;
            }
            foreach (var row in group.Rows)
            {
                // Spec §7: rows share the same grid as every other column -- a thin horizontal
                // separator ABOVE each row (except the very first one drawn), never a per-cell
                // outline or card.
                if (drewAnyRow)
                {
                    SetBrushColor(PaletteTokens.Grid);
                    var separator = new RectF(x, rowY - PaletteTokens.BorderAndGridThicknessPx, x + TableWidth, rowY);
                    dc->FillRectangle(&separator, (ID2D1Brush*)_brush.Get());
                }
                DrawRow(dc, x, rowY, row);
                rowY += RowHeightDip;
                drewAnyRow = true;
            }
        }

        // Outer widget border (spec §16's WidgetOuterBorder), wrapping the session header through
        // the last row -- drawn last so it sits cleanly over the fills without being occluded.
        SetBrushColor(PaletteTokens.WidgetOuterBorder);
        var outer = new RectF(x, headerTopY, x + TableWidth, rowY);
        dc->DrawRectangle(&outer, (ID2D1Brush*)_brush.Get(), PaletteTokens.BorderAndGridThicknessPx, null);
    }

    private void DrawClassHeader(ID2D1DeviceContext* dc, float x, float y, string name, int classId, string? classColorHex)
    {
        var color = PaletteTokens.ResolveClassColor(classId, name, classColorHex);
        SetBrushColor(PaletteTokens.SessionHeaderBand);
        var background = new RectF(x, y, x + TableWidth, y + ClassHeaderHeightDip);
        dc->FillRectangle(&background, (ID2D1Brush*)_brush.Get());
        SetBrushColor(color);
        var strip = new RectF(x, y, x + ClassStripWidthDip, y + ClassHeaderHeightDip);
        dc->FillRectangle(&strip, (ID2D1Brush*)_brush.Get());
        // Spec §15: never fabricate a class name -- some real sessions genuinely don't populate
        // DriverInfo.CarClassShortName (confirmed live: a real multiclass session showed this blank
        // for several classes). A literal "CLASS" placeholder here would look like real data; leaving
        // the band/strip color but no text is the honest choice when the SDK gives us nothing.
        if (!string.IsNullOrWhiteSpace(name))
        {
            SetBrushColor(PaletteTokens.TextPrimary);
            fixed (char* p = name)
            {
                var rect = new RectF(x + 8f, y, x + 180f, y + ClassHeaderHeightDip);
                dc->DrawText(p, (uint)name.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
    }

    private void DrawWaitingState(ID2D1DeviceContext* dc, float x, float y)
    {
        SetBrushColor(PaletteTokens.TextDisabled);
        string text = "Aguardando iRacing...";
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + NameColumnWidthDip + PositionColumnWidthDip + BadgeWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _nameFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, StandingsRow row)
    {
        // Class color strip -- keyed by the class, already resolved by TelemetryReader/LiveCoachEngine,
        // never recomputed here from manufacturer/licence/etc (spec §15).
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
                case "position":
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, row.Position.ToString(CultureInfo.InvariantCulture), cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
                case "carNumber":
                    // Car number is the real SDK DriverInfo.CarNumber, kept separate from CarIdx and
                    // from the rendered row index. Preserve source leading zeroes; explicit # prefix.
                    SetBrushColor(PaletteTokens.TextSecondary);
                    DrawCell(dc, string.IsNullOrWhiteSpace(row.CarNumber) ? "—" : $"#{row.CarNumber}", cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
                case "flag":
                    var flag = _flags.Find(row.FlagEmoji);
                    if (flag != null)
                    {
                        // Contain-fit, never stretched (spec §18).
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
                    // Full name is expected to already be in DriverCode per spec §5 -- this widget
                    // does not truncate or abbreviate on its own.
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, NameDisplay.Format(row.DriverCode, _numberFormatConfig.NameFormat), cellX, y, cellWidth, ColumnAlignment.Left);
                    break;
                case "license":
                    DrawLicenseBadge(dc, cellX, y, cellWidth, row.LicString, row.LicColorHex);
                    break;
                case "iratingDelta":
                    // iRating + Δ combined badge, same row, same rectangle (spec §6: never stacked).
                    var badgeRect = new Rect2D(cellX, y + (RowHeightDip - BadgeHeightDip) / 2, cellWidth, BadgeHeightDip);
                    DrawIRatingBadge(dc, badgeRect, row.IRating, row.EstimatedDeltaIRating);
                    break;
                case "gap":
                    // Gap to leader -- distinct field from lap-delta-vs-player (spec §6: "não
                    // confunda esse valor com gap de corrida"). Leader's own row has no gap.
                    DrawNumericOrDash(dc, cellX, y, cellWidth, row.GapToLeaderSeconds,
                        v => v.ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: false), CultureInfo.InvariantCulture),
                        PaletteTokens.TextSecondary, placement.Column.Alignment);
                    break;
                case "interval":
                    // Never derived from gap-to-leader; a missing SDK value stays an explicit dash.
                    DrawNumericOrDash(dc, cellX, y, cellWidth, row.IntervalSeconds,
                        v => v.ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: true), CultureInfo.InvariantCulture),
                        PaletteTokens.TextSecondary, placement.Column.Alignment);
                    break;
                case "lastLap":
                    DrawNumericOrDash(dc, cellX, y, cellWidth, row.LastLapTime,
                        v => LapTimeFormatting.Format(v, placement.Column.DecimalPlaces ?? 3),
                        PaletteTokens.TextPrimary, placement.Column.Alignment);
                    break;
                case "lapDelta":
                {
                    // Negative = this driver faster than the player (spec §6's sign convention),
                    // colored green/red; player's own row always shows a neutral 0.000.
                    var deltaColor = row.IsPlayer ? PaletteTokens.NeutralDeltaOrGap
                        : row.LapDeltaVsPlayerSeconds switch
                        {
                            < 0 => PaletteTokens.LapDeltaFaster,
                            > 0 => PaletteTokens.LapDeltaSlower,
                            _ => PaletteTokens.NeutralDeltaOrGap
                        };
                    double? deltaValue = row.IsPlayer ? 0.0 : row.LapDeltaVsPlayerSeconds;
                    DrawNumericOrDash(dc, cellX, y, cellWidth, deltaValue,
                        v => v.ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: true), CultureInfo.InvariantCulture),
                        deltaColor, placement.Column.Alignment);
                    break;
                }
                case "pit":
                {
                    // Core's PitStatus is "L8 24s" (last stop: lap + seconds) or "--" when the driver
                    // hasn't stopped; the mockups show it as "L8/24s". Never fabricated when absent.
                    string pit = string.IsNullOrWhiteSpace(row.PitStatus) || row.PitStatus == "--" ? "—" : row.PitStatus.Replace(' ', '/');
                    DrawCell(dc, pit, cellX, y, cellWidth, placement.Column.Alignment);
                    break;
                }
                case "overtake":
                    DrawOvertakeCell(dc, cellX, y, cellWidth, row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown);
                    break;
            }
        }
    }

    /// <summary>Builds a numeric format string honoring the column's configured decimal places
    /// (spec §12: "casas decimais por campo numérico") -- signed columns always show an explicit
    /// +/- (spec §12: "sinal explícito em deltas"), unsigned ones never fabricate a sign.</summary>
    private static string DecimalFormat(int? decimalPlaces, bool signed)
    {
        int decimals = Math.Clamp(decimalPlaces ?? 3, 0, 6);
        string digits = decimals > 0 ? "." + new string('0', decimals) : "";
        return signed ? $"+0{digits};-0{digits};0{digits}" : $"0{digits}";
    }

    private static TextAlignment ToDWrite(ColumnAlignment alignment) => alignment switch
    {
        ColumnAlignment.Left => TextAlignment.Leading,
        ColumnAlignment.Right => TextAlignment.Trailing,
        _ => TextAlignment.Center
    };

    /// <summary>Draws a single line of text respecting a per-column alignment, reusing
    /// <see cref="_statusFormat"/> (this widget's one general-purpose format) with its text
    /// alignment swapped per call -- cheaper than keeping one <c>IDWriteTextFormat</c> per
    /// alignment for a value that changes rarely (only on a column-config update, not per frame).</summary>
    private void DrawCell(ID2D1DeviceContext* dc, string text, float x, float y, float width, ColumnAlignment alignment)
    {
        ThrowIfFailed(_statusFormat.Get()->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        ThrowIfFailed(_statusFormat.Get()->SetTextAlignment(TextAlignment.Center)); // restore this format's other callers' expectation
    }

    private void DrawNumericOrDash(ID2D1DeviceContext* dc, float x, float y, float widthDip, double? value, Func<double, string> format, Color4 color, ColumnAlignment alignment)
    {
        SetBrushColor(value is null ? PaletteTokens.TextDisabled : color);
        string text = value is double v ? format(v) : "—"; // spec §17/§7: missing data is an explicit dash, never a fabricated zero.
        ThrowIfFailed(_numericFormat.Get()->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + widthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _numericFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        ThrowIfFailed(_numericFormat.Get()->SetTextAlignment(TextAlignment.Trailing)); // restore this format's default for the next call
    }

    private readonly record struct Rect2D(float X, float Y, float Width, float Height);

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
            var textRect = new RectF(x, y, x + width, y + 13f);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &textRect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        var track = new RectF(x + 2f, y + 17f, x + width - 2f, y + 20f);
        SetBrushColor(PaletteTokens.BarTrackEmpty);
        dc->FillRectangle(&track, (ID2D1Brush*)_brush.Get());
        if (seconds is not double bank) return;
        float fraction = Math.Clamp((float)(bank / TelemetryReader.P2PMaxSeconds), 0f, 1f);
        if (fraction <= 0f) return;
        var fill = new RectF(x + 2f, y + 17f, x + 2f + (width - 4f) * fraction, y + 20f);
        SetBrushColor(color);
        dc->FillRectangle(&fill, (ID2D1Brush*)_brush.Get());
    }

    private void DrawIRatingBadge(ID2D1DeviceContext* dc, Rect2D bounds, int iRating, double? estimatedDelta)
    {
        SetBrushColor(PaletteTokens.IRatingBadgeBackground);
        var rr = new RoundedRect
        {
            rect = new RectF(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height),
            radiusX = PaletteTokens.BadgeCornerRadiusPx,
            radiusY = PaletteTokens.BadgeCornerRadiusPx
        };
        dc->FillRoundedRectangle(&rr, (ID2D1Brush*)_brush.Get());

        SetBrushColor(PaletteTokens.TextPrimary);
        string iratingText = _numberFormatConfig.FormatIRating(iRating);
        fixed (char* p = iratingText)
        {
            var rect = new RectF(bounds.X + 4, bounds.Y, bounds.X + bounds.Width * 0.55f, bounds.Y + bounds.Height);
            dc->DrawText(p, (uint)iratingText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }

        if (estimatedDelta is double delta)
        {
            SetBrushColor(delta >= 0 ? PaletteTokens.PositiveDelta : PaletteTokens.NegativeDelta);
            string deltaText = delta.ToString("+0;-0", CultureInfo.InvariantCulture);
            fixed (char* p = deltaText)
            {
                var rect = new RectF(bounds.X + bounds.Width * 0.55f, bounds.Y, bounds.X + bounds.Width - 4, bounds.Y + bounds.Height);
                dc->DrawText(p, (uint)deltaText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
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
        _telemetry.StandingsUpdated -= OnStandingsUpdated;
        _telemetry.SessionStatusUpdated -= OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated -= OnPlayerCarStatusUpdated;
        _telemetry.Dispose();
        _brush.Dispose();
        _numericFormat.Dispose();
        _statusFormat.Dispose();
        _nameFormat.Dispose();
    }
}
