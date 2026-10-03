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

    private readonly TableFormatSet _fmt;
    private ComPtr<ID2D1SolidColorBrush> _brush; // color set per-draw via SetColor; one brush reused throughout.

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;
    private float RowHeightDip => (_appearance.RowHeightDip > 0 ? _appearance.RowHeightDip : BaseRowHeightDip) + _appearance.RowSpacingDip;

    private const float BaseRowHeightDip = 32f;
    private const float HeaderHeightDip = 30f;
    private const float PanelGapDip = 6f;
    private const float ClassStripWidthDip = 4f;
    private const float PositionColumnWidthDip = 30f;
    private const float CarNumberColumnWidthDip = 44f;
    private const float PositionChangeColumnWidthDip = 40f;
    private const float NameColumnWidthDip = 152f;
    private const float LicenseColumnWidthDip = 56f;
    private const float FlagColumnWidthDip = 30f;
    private const float BrandColumnWidthDip = 34f;
    private const float BadgeWidthDip = 96f;
    private const float GapColumnWidthDip = 66f;
    private const float IntervalColumnWidthDip = 66f;
    private const float LastLapColumnWidthDip = 80f;
    private const float LapDeltaColumnWidthDip = 68f;
    private const float AvgGapColumnWidthDip = 76f;
    public const int DefaultAvgGapWindow = 5;
    private const float OvertakeColumnWidthDip = 64f;
    private const float PitColumnWidthDip = 68f;
    private const float ColumnGapDip = 6f;
    private const float PanelRadiusDip = PanelChrome.CornerRadius;

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
        // Kapps: chevron + places gained/lost since the start, right after the position (race only; blank
        // when unchanged or outside a race).
        new("posChange", ColumnWidthMode.Fixed, PositionChangeColumnWidthDip, PositionChangeColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 1),
        new("carNumber", ColumnWidthMode.Fixed, CarNumberColumnWidthDip, CarNumberColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 2),
        new("brand", ColumnWidthMode.Fixed, BrandColumnWidthDip, BrandColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 3),
        new("flag", ColumnWidthMode.Fixed, FlagColumnWidthDip, FlagColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 4),
        new("name", ColumnWidthMode.Flexible, NameColumnWidthDip, 60f, ColumnAlignment.Left, 0, ColumnGapDip, true, 5),
        new("license", ColumnWidthMode.Fixed, LicenseColumnWidthDip, LicenseColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 6),
        new("iratingDelta", ColumnWidthMode.Fixed, BadgeWidthDip, BadgeWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 7),
        new("interval", ColumnWidthMode.Fixed, IntervalColumnWidthDip, IntervalColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 8, DecimalPlaces: 3),
        new("lastLap", ColumnWidthMode.Fixed, LastLapColumnWidthDip, LastLapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 9, DecimalPlaces: 3),
        new("lapDelta", ColumnWidthMode.Fixed, LapDeltaColumnWidthDip, LapDeltaColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, true, 10, DecimalPlaces: 3),
        // Optional (hidden by default): average of the N fastest clean laps, theirs minus mine.
        new("avgGap", ColumnWidthMode.Fixed, AvgGapColumnWidthDip, AvgGapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, false, 11, DecimalPlaces: 3, LapWindow: DefaultAvgGapWindow),
        new("pit", ColumnWidthMode.Fixed, PitColumnWidthDip, PitColumnWidthDip, ColumnAlignment.Center, 0, ColumnGapDip, true, 12),
        new("gap", ColumnWidthMode.Fixed, GapColumnWidthDip, GapColumnWidthDip, ColumnAlignment.Right, 0, ColumnGapDip, false, 13, DecimalPlaces: 3),
        new("overtake", ColumnWidthMode.Fixed, OvertakeColumnWidthDip, OvertakeColumnWidthDip, ColumnAlignment.Center, 0, 0, true, 14),
    ];

    /// <summary>Old saved profiles gain columns added later (posChange) -- see ColumnDefaults.</summary>
    public static List<ColumnDefinition> WithMissingDefaults(List<ColumnDefinition> columns) =>
        ColumnDefaults.MergeMissing(columns, BuildDefaultColumns());

    /// <summary>Spec §12: every column-config change (reorder/width/visibility/decimals/alignment)
    /// applies live, no restart. Called from the Control Center's IPC handler.</summary>
    public void SetColumns(List<ColumnDefinition> columns)
    {
        _columns = WithMissingDefaults(columns);
        RebuildEffectiveColumns();
    }

    /// <summary>The user's columns with the appearance's horizontal padding applied to every padded
    /// column ("Padding (H)"); what layout and width actually use.</summary>
    private List<ColumnDefinition> _effectiveColumns = BuildDefaultColumns();

    private void RebuildEffectiveColumns()
    {
        var cols = _columns.Select(c => _appearance.PaddingHDip >= 0 && c.PaddingRightPx > 0 ? c with { PaddingRightPx = _appearance.PaddingHDip } : c).ToList();
        // Contextual columns hide WITHOUT shrinking the widget (the name takes the space): Overtake only where the
        // session has push-to-pass, Pit only once a car has made a stop.
        if (!_hasP2P) cols = WidgetLayoutEngine.HideKeepingWidth(cols, "overtake");
        if (!_hasPit) cols = WidgetLayoutEngine.HideKeepingWidth(cols, "pit");
        _effectiveColumns = cols;
    }

    private bool _hasP2P;
    private bool _hasPit;
    private readonly Dictionary<(string, int), string> _nameFit = new();

    /// <summary>Item 9: same one-way latch as RelativeWidget's own SyncP2PColumn -- once any row in
    /// this frame's FULL field (this method always receives the whole selectable set, never a
    /// windowed subset) publishes push-to-pass data, the Overtake column stays reserved for the
    /// rest of the session; it is a per-session decision, never re-evaluated downward per frame.
    /// Pit keeps its own, separate (two-way) rule -- only the Overtake column has the "must never
    /// collapse mid-session" requirement.</summary>
    private void SyncP2PColumn(IEnumerable<StandingsRow> rows)
    {
        var list = rows as IReadOnlyCollection<StandingsRow> ?? rows.ToList();
        bool pit = list.Any(r => !string.IsNullOrEmpty(r.PitStatus));
        bool changed = pit != _hasPit;
        _hasPit = pit;
        if (!_hasP2P && list.Any(r => r.P2PActive is not null))
        {
            _hasP2P = true;
            changed = true;
        }
        if (changed) RebuildEffectiveColumns();
    }

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
    private float TableWidth => ColumnsLeftMarginDip + WidgetLayoutEngine.SumVisibleColumnFootprints(_effectiveColumns);

    public StandingsWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags, IDWriteFontCollection1* fontCollection = null, bool startTelemetry = true)
    {
        _flags = flags;
        _dwriteFactory = dwriteFactory;
        RebuildEffectiveColumns();
        _fontCollection = fontCollection;
        _fmt = new TableFormatSet(dwriteFactory, fontCollection);
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.StandingsUpdated += OnStandingsUpdated;
        _telemetry.SessionStatusUpdated += OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated += OnPlayerCarStatusUpdated;
        if (startTelemetry) _telemetry.Start();
    }

    /// <summary>(Re)builds every owned text format at its tuned base size times the current
    /// <see cref="_appearance"/> font scale. Called from the constructor and again from
    /// <see cref="SetAppearance"/> -- a DirectWrite text format's size is immutable once created,
    /// so a live font-scale change means throwing the old ones away and creating new ones, not
    /// mutating them.</summary>
    private void CreateTextFormats()
    {
        _nameFit.Clear();
        _fmt.Rebuild(_appearance);
    }

    /// <summary>Spec §12: font scale/row height/row spacing apply live, no restart, same pattern as
    /// <see cref="SetColumns"/>.</summary>
    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight || appearance.FontFamily != _appearance.FontFamily;
        _appearance = appearance;
        RebuildEffectiveColumns();
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
            const string simLabel = "SIMULATION";
            fixed (char* p = simLabel)
            {
                var rect = new RectF(x, y - 16, x + 200, y);
                dc->DrawText(p, (uint)simLabel.Length, _fmt.StatusFormat, &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
            DrawPanels(dc, x, y, simulated, _simulatedSession, _simulatedPlayer);
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
        DrawPanels(dc, x, y, rows, session, player);
    }

    /// <summary>Size of what the last <see cref="Draw"/> actually painted -- the overlay resizes an
    /// auto-sized window to this, so columns/row-count/Top-N changes never clip or leave dead space.</summary>
    public (float Width, float Height) LastDrawnSize { get; private set; } = (600f, 100f);

    private SessionStatus? _simulatedSession;
    private PlayerCarStatus? _simulatedPlayer;
    public void SetSimulatedSession(SessionStatus? session, PlayerCarStatus? player)
    {
        _simulatedSession = session;
        _simulatedPlayer = player;
    }

    private IReadOnlyList<double>? _playerCleanLaps;
    private List<HeaderFieldConfig> _headerFields = HeaderFields.DefaultStandings();
    public void SetHeaderFields(List<HeaderFieldConfig> fields) => _headerFields = HeaderFields.Complete(fields);

    /// <summary>One rounded panel per class (mockups: "GTP" and "GT3" are separate panels), each with
    /// its own header -- class label, lap, that class's own SOF, clock -- then its rows.</summary>
    private void DrawPanels(ID2D1DeviceContext* dc, float x, float y, IReadOnlyList<StandingsRow> rows, SessionStatus? session, PlayerCarStatus? player)
    {
        SyncP2PColumn(rows);
        _playerCleanLaps = rows.FirstOrDefault(r => r.IsPlayer)?.CleanLapTimes;
        var groups = StandingsSelection.GroupAndSelect(rows, _presentationOptions);
        float cursorY = y;
        foreach (var group in groups)
        {
            float panelHeight = HeaderHeightDip + group.Rows.Count * RowHeightDip;
            var panel = new RectF(x, cursorY, x + TableWidth, cursorY + panelHeight);

            // SOF is that class's own, computed from EVERY driver in the class (not just the rows
            // this widget selected for display).
            double? classSof = session?.ClassSof is { } frozenSof && frozenSof.TryGetValue(group.ClassId, out var fixedSof)
                ? fixedSof // race: all retained session entrants, including disconnected drivers
                : Sof.Compute(rows.Where(r => r.CarClassId == group.ClassId).Select(r => r.IRating));

            PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));
            using (PanelChrome.PushClip(dc, panel))
            {
                DrawHeaderBand(dc, x, cursorY, group, classSof, session, player);
                float rowY = cursorY + HeaderHeightDip;
                bool first = true;
                for (int i = 0; i < group.Rows.Count; i++)
                {
                    var row = group.Rows[i];
                    if (!first && !row.IsPlayer && !group.Rows[i - 1].IsPlayer)
                    {
                        SetBrushColor(PaletteTokens.PanelDivider);
                        var separator = new RectF(x + ClassStripWidthDip, rowY, x + TableWidth, rowY + 1f);
                        dc->FillRectangle(&separator, (ID2D1Brush*)_brush.Get());
                    }
                    DrawRow(dc, x, rowY, row);
                    rowY += RowHeightDip;
                    first = false;
                }
            }
            PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
            cursorY += panelHeight + PanelGapDip;
        }
        LastDrawnSize = (TableWidth, Math.Max(HeaderHeightDip, cursorY - y - PanelGapDip));
    }

    /// <summary>Header band: the user-configured fields (spec §12), separated by thin vertical
    /// dividers as in the mockups. The class label is coloured with the class colour and left-aligned
    /// in its own cell; the other fields share the remaining width evenly, centred.</summary>
    private void DrawHeaderBand(ID2D1DeviceContext* dc, float x, float y, StandingsClassGroup group, double? classSof, SessionStatus? session, PlayerCarStatus? player)
    {
        SetBrushColor(PaletteTokens.PanelHeaderBand);
        var band = new RectF(x, y, x + TableWidth, y + HeaderHeightDip);
        dc->FillRectangle(&band, (ID2D1Brush*)_brush.Get());

        var cells = new List<(string Text, Color4 Color, bool IsClass, (string?, int?)? Font)>();
        var classColor = PaletteTokens.ResolveClassColor(group.ClassId, group.ClassShortName, group.ClassColorHex, group.ClassRank);
        // The class colour also runs down the header so a panel is identifiable even when the sim
        // publishes no class name (never invented -- spec §15).
        SetBrushColor(classColor);
        var headerStrip = new RectF(x, y, x + ClassStripWidthDip, y + HeaderHeightDip);
        dc->FillRectangle(&headerStrip, (ID2D1Brush*)_brush.Get());
        foreach (var field in _headerFields)
        {
            if (!field.Visible) continue;
            if (field.Key == "class")
            {
                // Spec §15: never invent a class name -- a blank SDK class simply has no label.
                if (!string.IsNullOrWhiteSpace(group.ClassShortName))
                    cells.Add((group.ClassShortName.Replace(" CLASS", ""), classColor, true, FieldFont(field)));
                continue;
            }
            string? text = field.Key switch
            {
                "sof" => classSof is double sof ? "SOF " + NumberFormatConfig.GroupThousands((int)Math.Round(sof)) : null,
                // Each class panel shows ITS class's driver count (Kapps), never the whole field.
                "drivers" => session is null ? null : $"{session.DriverCountText(group.ClassId)} DRIVERS",
                // Each class panel shows ITS leader's lap and projection (Kapps "15/≈33.05").
                "lap" => HeaderFields.ClassLapText(session, group.ClassId, group.Rows.Any(r => r.Position == 1)),
                _ => HeaderFields.Text(field.Key, session, player, DateTime.Now),
            };
            if (text is not null) cells.Add((text, PaletteTokens.TextPrimary, false, FieldFont(field)));
        }
        if (cells.Count == 0) return;

        float left = x + ClassStripWidthDip + 6f;
        float right = x + TableWidth - 6f;
        bool hasClass = cells[0].IsClass;
        float classWidth = hasClass ? 74f : 0f;
        int others = cells.Count - (hasClass ? 1 : 0);
        float otherWidth = others > 0 ? (right - left - classWidth) / others : 0f;

        float cellX = left;
        for (int i = 0; i < cells.Count; i++)
        {
            var (text, color, isClass, font) = cells[i];
            float width = isClass ? classWidth : otherWidth;
            if (i > 0)
            {
                SetBrushColor(PaletteTokens.PanelDivider);
                var divider = new RectF(cellX, y + 7f, cellX + 1f, y + HeaderHeightDip - 7f);
                dc->FillRectangle(&divider, (ID2D1Brush*)_brush.Get());
            }
            SetBrushColor(color);
            _fmt.Override = font;
            ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(isClass ? TextAlignment.Leading : TextAlignment.Center));
            fixed (char* p = text)
            {
                var rect = new RectF(cellX + (isClass ? 2f : 0f), y, cellX + width, y + HeaderHeightDip);
                PanelChrome.DrawTabularText(dc, _dwriteFactory, (ID2D1Brush*)_brush.Get(), _fmt.StatusFormat, text, rect);
            }
            ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(TextAlignment.Center));
            _fmt.Override = null;
            cellX += width;
        }
    }

    private static (string?, int?)? FieldFont(HeaderFieldConfig field) =>
        field.FontFamily is null && field.FontWeight is null ? null : (field.FontFamily, field.FontWeight);

    private void DrawWaitingState(ID2D1DeviceContext* dc, float x, float y)
    {
        LastDrawnSize = (TableWidth, RowHeightDip);
        SetBrushColor(PaletteTokens.TextDisabled);
        string text = "Waiting for iRacing...";
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + NameColumnWidthDip + PositionColumnWidthDip + BadgeWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _fmt.NameFormat, &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, StandingsRow row)
    {
        // Class color strip -- keyed by the class, already resolved by TelemetryReader/LiveCoachEngine,
        // never recomputed here from manufacturer/licence/etc (spec §15).
        if (row.IsPlayer)
        {
            // Mockups: the player's row is a cyan-tinted, cyan-outlined rounded highlight.
            var highlight = new RectF(x + ClassStripWidthDip + 1f, y + 1f, x + TableWidth - 3f, y + RowHeightDip - 1f);
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
            _fmt.Override = placement.Column.FontFamily is null && placement.Column.FontWeight is null ? null : (placement.Column.FontFamily, placement.Column.FontWeight);
            switch (placement.Column.Key)
            {
                case "position":
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, (row.ClassPosition > 0 ? row.ClassPosition : row.Position).ToString(CultureInfo.InvariantCulture), cellX, y, cellWidth, ColumnAlignment.Center);
                    break;
                case "posChange":
                    DrawPositionChange(dc, cellX, y, cellWidth, row.PositionChange);
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
                             && PanelChrome.MeasureWidth(_dwriteFactory, _fmt.StatusFormat, row.ManufacturerBadge) <= cellWidth)
                    {
                        // A make without a bundled logo: its name, but only if it fits the cell --
                        // an overflowing word (e.g. the pace car's "SAFETY") would spill over the
                        // neighbouring columns.
                        SetBrushColor(PaletteTokens.TextSecondary);
                        DrawCell(dc, row.ManufacturerBadge, cellX, y, cellWidth, ColumnAlignment.Center);
                    }
                    break;
                case "name":
                    // Full name is expected to already be in DriverCode per spec §5 -- this widget
                    // does not truncate or abbreviate on its own.
                    SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
                    DrawCell(dc, PanelChrome.Ellipsize(_dwriteFactory, _fmt.StatusFormat, NameDisplay.Format(row.DriverCode, _numberFormatConfig.NameFormat), cellWidth, _nameFit), cellX, y, cellWidth, ColumnAlignment.Left);
                    break;
                case "license":
                    DrawLicenseBadge(dc, cellX, y, cellWidth, row.LicString, row.LicColorHex);
                    break;
                case "iratingDelta":
                    // iRating + Δ combined badge, same row, same rectangle (spec §6: never stacked).
                    float badgeHeight = PanelChrome.BadgeHeight(RowHeightDip);
                    var badgeRect = new Rect2D(cellX, y + (RowHeightDip - badgeHeight) / 2, cellWidth, badgeHeight);
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
                {
                    // Kapps format (StandingsCellText): "INT" on the class leader, "1L", "1.5" in a race,
                    // "0.161" by best lap; a missing value stays an explicit dash.
                    string text = StandingsCellText.Interval(row);
                    DrawTextCell(dc, cellX, y, cellWidth, text, text == "—" ? PaletteTokens.TextDisabled : PaletteTokens.TextPrimary, placement.Column.Alignment);
                    break;
                }
                case "lastLap":
                {
                    // Kapps (StandingsCellText.LapText): last lap in a race with 1 decimal, best lap with 3
                    // when ordered by best lap; truncated.
                    string lapText = StandingsCellText.LapText(row);
                    DrawTextCell(dc, cellX, y, cellWidth, lapText, lapText == "—" ? PaletteTokens.TextDisabled : PaletteTokens.TextPrimary, placement.Column.Alignment);
                    break;
                }
                case "lapDelta":
                {
                    // No +/- sign -- the magnitude is shown, colour carries the direction FROM THE PLAYER'S
                    // point of view (Kapps): green = the player was faster (that driver's lap was slower,
                    // delta = theirs - mine > 0), red = the player was slower (delta < 0); zero, unknown or
                    // the player's own row stays neutral.
                    double? deltaValue = row.IsPlayer ? 0.0 : row.LapDeltaVsPlayerSeconds;
                    var deltaColor = !row.IsPlayer && deltaValue is double d && d != 0.0
                        ? (d > 0 ? PaletteTokens.LapDeltaFaster : PaletteTokens.LapDeltaSlower)
                        : PaletteTokens.NeutralDeltaOrGap;
                    DrawNumericOrDash(dc, cellX, y, cellWidth, deltaValue,
                        v => Math.Abs(v).ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: false), CultureInfo.InvariantCulture),
                        deltaColor, placement.Column.Alignment);
                    break;
                }
                case "avgGap":
                {
                    // Average of the N fastest clean laps, theirs minus mine. Same colour rule as lapDelta:
                    // green = the player is faster on average, red = slower, neutral on the own row.
                    int window = placement.Column.LapWindow is > 0 ? placement.Column.LapWindow.Value : DefaultAvgGapWindow;
                    double? avg = row.IsPlayer ? 0.0 : LapHistory.AverageGap(row.CleanLapTimes, _playerCleanLaps, window);
                    var avgColor = !row.IsPlayer && avg is double a && a != 0.0
                        ? (a > 0 ? PaletteTokens.LapDeltaFaster : PaletteTokens.LapDeltaSlower)
                        : PaletteTokens.NeutralDeltaOrGap;
                    DrawNumericOrDash(dc, cellX, y, cellWidth, avg,
                        v => Math.Abs(v).ToString(DecimalFormat(placement.Column.DecimalPlaces, signed: false), CultureInfo.InvariantCulture),
                        avgColor, placement.Column.Alignment);
                    break;
                }
                case "pit":
                {
                    // Core's PitStatus is "L8 24s" (last stop: lap + seconds) or "--" when the driver
                    // hasn't stopped; the mockups show it as "L8/24s". Never fabricated when absent.
                    string pit = string.IsNullOrWhiteSpace(row.PitStatus) || row.PitStatus == "--" ? "" : row.PitStatus; // Kapps: blank without a stop // Kapps: "L1 58.8", "PIT 35", "TOW 28m"
                    DrawCell(dc, pit, cellX, y, cellWidth, placement.Column.Alignment);
                    break;
                }
                case "overtake":
                    DrawOvertakeCell(dc, cellX, y, cellWidth, row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown);
                    break;
            }
        }
        _fmt.Override = null;
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
    /// the status format (this widget's one general-purpose format) with its text
    /// alignment swapped per call -- cheaper than keeping one <c>IDWriteTextFormat</c> per
    /// alignment for a value that changes rarely (only on a column-config update, not per frame).</summary>
    private void DrawCell(ID2D1DeviceContext* dc, string text, float x, float y, float width, ColumnAlignment alignment)
    {
        ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            PanelChrome.DrawTabularText(dc, _dwriteFactory, (ID2D1Brush*)_brush.Get(), _fmt.StatusFormat, text, rect);
        }
        ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(TextAlignment.Center)); // restore this format's other callers' expectation
    }

    private void DrawNumericOrDash(ID2D1DeviceContext* dc, float x, float y, float widthDip, double? value, Func<double, string> format, Color4 color, ColumnAlignment alignment)
    {
        SetBrushColor(value is null ? PaletteTokens.TextDisabled : color);
        string text = value is double v ? format(v) : "—"; // spec §17/§7: missing data is an explicit dash, never a fabricated zero.
        ThrowIfFailed(_fmt.NumericFormat->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + widthDip, y + RowHeightDip);
            PanelChrome.DrawTabularText(dc, _dwriteFactory, (ID2D1Brush*)_brush.Get(), _fmt.NumericFormat, text, rect);
        }
        ThrowIfFailed(_fmt.NumericFormat->SetTextAlignment(TextAlignment.Trailing)); // restore this format's default for the next call
    }

    private void DrawTextCell(ID2D1DeviceContext* dc, float x, float y, float widthDip, string text, Color4 color, ColumnAlignment alignment)
    {
        SetBrushColor(color);
        ThrowIfFailed(_fmt.NumericFormat->SetTextAlignment(ToDWrite(alignment)));
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + widthDip, y + RowHeightDip);
            PanelChrome.DrawTabularText(dc, _dwriteFactory, (ID2D1Brush*)_brush.Get(), _fmt.NumericFormat, text, rect);
        }
        ThrowIfFailed(_fmt.NumericFormat->SetTextAlignment(TextAlignment.Trailing));
    }

    /// <summary>Kapps' positions gained/lost: a chevron (green up / red down) followed by the number in the
    /// same colour; nothing when unchanged or unknown. The chevron is drawn as two strokes (no glyph
    /// dependency on the bundled font).</summary>
    private void DrawPositionChange(ID2D1DeviceContext* dc, float x, float y, float width, int? change)
    {
        var (trend, number) = StandingsCellText.PositionChange(change);
        if (trend == PositionTrend.None) return;
        var color = trend == PositionTrend.Up ? PaletteTokens.PositiveDelta : PaletteTokens.NegativeDelta;
        SetBrushColor(color);
        float cx = x + 9f, cy = y + RowHeightDip / 2f, half = 5f, rise = trend == PositionTrend.Up ? -3f : 3f;
        dc->DrawLine(new System.Numerics.Vector2(cx - half, cy - rise), new System.Numerics.Vector2(cx, cy + rise), (ID2D1Brush*)_brush.Get(), 2.4f, null);
        dc->DrawLine(new System.Numerics.Vector2(cx, cy + rise), new System.Numerics.Vector2(cx + half, cy - rise), (ID2D1Brush*)_brush.Get(), 2.4f, null);
        DrawTextCell(dc, x + 16f, y, width - 16f, number, color, ColumnAlignment.Center);
    }

    private readonly record struct Rect2D(float X, float Y, float Width, float Height);

    private void DrawLicenseBadge(ID2D1DeviceContext* dc, float x, float y, float width, string license, string? colorHex)
    {
        // Mockups: a solid blue rounded pill with bold white "A 4.12".
        // Kapps colours the pill by the licence letter (LicenseStyle); the SDK's LicColor is a decimal
        // number, not "#RRGGBB", and rendered the AI's "R" blue.
        var color = ParseHexOrFallback(LicenseStyle.ColorHex(license) ?? colorHex, PaletteTokens.SrPillBlue);
        float badgeHeight = PanelChrome.BadgeHeight(RowHeightDip);
        var pill = new RectF(x, y + (RowHeightDip - badgeHeight) / 2, x + width, y + (RowHeightDip + badgeHeight) / 2);
        PanelChrome.DrawTranslucentBadge(dc, _brush.Get(), pill, color);
        SetBrushColor(PaletteTokens.TextPrimary);
        string text = string.IsNullOrWhiteSpace(license) ? "—" : _numberFormatConfig.FormatLicense(license);
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + RowHeightDip);
            PanelChrome.DrawTabularText(dc, _dwriteFactory, (ID2D1Brush*)_brush.Get(), _fmt.StatusFormat, text, rect);
        }
    }

    private void DrawOvertakeCell(ID2D1DeviceContext* dc, float x, float y, float width, bool? active, double? seconds, bool cooldown)
    {
        DrawTimeBarPill(dc, x, y, width, RowHeightDip, active, seconds, cooldown, _fmt.NumericFormat, _brush.Get());
    }

    /// <summary>Push-to-pass cell: the remaining bank in seconds ("124s") on top, in the same colour as
    /// the bar, with the thin bank bar right under it -- one compact column instead of a bar column
    /// plus a number column (the widget stays inside its width budget). Colours follow the mockup:
    /// green = active, yellow = recharging, light blue = available, grey = empty/unknown.</summary>
    internal static void DrawTimeBarPill(ID2D1DeviceContext* dc, float x, float y, float width, float rowHeight, bool? active, double? seconds, bool cooldown, IDWriteTextFormat* numeric, ID2D1SolidColorBrush* brush)
    {
        Color4 color = active is null ? PaletteTokens.OvertakeUnknown
            : active == true ? PaletteTokens.OvertakeActive
            : cooldown ? PaletteTokens.OvertakeCooldown
            : seconds is <= 0 ? PaletteTokens.OvertakeDepleted
            : PaletteTokens.OvertakeAvailable;

        const float barHeight = 5f, barInset = 8f;
        float textHeight = rowHeight * 0.53f;
        float barTop = y + rowHeight * 0.66f;
        var track = new RectF(x + barInset, barTop, x + width - barInset, barTop + barHeight);
        PanelChrome.FillPanel(dc, brush, track, PaletteTokens.BarTrackEmpty, 2.5f);
        if (seconds is double bank)
        {
            float fraction = Math.Clamp((float)(bank / TelemetryReader.P2PMaxSeconds), 0f, 1f);
            if (fraction > 0f)
            {
                var fill = new RectF(track.Left, track.Top, track.Left + (track.Right - track.Left) * fraction, track.Bottom);
                PanelChrome.FillPanel(dc, brush, fill, color, 2.5f);
            }
        }

        string text = seconds is double s ? $"{Math.Clamp((int)Math.Round(s), 0, TelemetryReader.P2PMaxSeconds)}s" : "—";
        var textColor = seconds is null ? PaletteTokens.TextDisabled : color;
        PanelChrome.DrawText(dc, brush, numeric, text, x, y + 2f, width, textHeight, textColor, TextAlignment.Center);
        numeric->SetTextAlignment(TextAlignment.Trailing);
    }

    /// <summary>The iRating pill's shared fill/border chrome (spec §6: dark, outlined, rounded) --
    /// item 13 reuses exactly this so the Relative widget's own iRating column never redraws its
    /// own copy of the pill background.</summary>
    internal static void DrawIRatingPillBackground(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, RectF pill)
    {
        float radius = Math.Min(5f, (pill.Bottom - pill.Top) / 4f);
        PanelChrome.FillPanel(dc, brush, pill, PaletteTokens.PillFill, radius);
        PanelChrome.StrokePanel(dc, brush, pill, PaletteTokens.PillBorder, 1f, radius);
    }

    /// <summary>Item 13: the Standings' own iRating pill, WITHOUT the projected-delta half -- used by
    /// the Relative widget's narrower iRating column (no ΔiRating there, spec §7). Centered text,
    /// "—" for an unknown rating (&lt;=1), same fill/border/rounding as <see cref="DrawIRatingBadge"/>.</summary>
    internal static void DrawIRatingPillOnly(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, IDWriteTextFormat* statusFormat, RectF pill, int iRating, NumberFormatConfig format)
    {
        DrawIRatingPillBackground(dc, brush, pill);
        // Kapps prints the AI's 0 as "0.0k" (no dash).
        Color4 textColor = PaletteTokens.TextPrimary;
        brush->SetColor(&textColor);
        string text = format.FormatIRating(Math.Max(0, iRating));
        ThrowIfFailed(statusFormat->SetTextAlignment(TextAlignment.Center));
        fixed (char* p = text)
        {
            var rect = pill;
            dc->DrawText(p, (uint)text.Length, statusFormat, &rect, (ID2D1Brush*)brush, DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawIRatingBadge(ID2D1DeviceContext* dc, Rect2D bounds, int iRating, double? estimatedDelta)
    {
        // Mockups: a dark, outlined pill -- "4.390" in white, the projected delta in green/red
        // inside the SAME pill (spec §6: never stacked).
        if (!_numberFormatConfig.ShowIRatingDelta) estimatedDelta = null;
        var pill = new RectF(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);
        DrawIRatingPillBackground(dc, _brush.Get(), pill);

        if (iRating <= 1) estimatedDelta = null; // unknown rating: never a delta on top of a dash
        SetBrushColor(PaletteTokens.TextPrimary);
        string iratingText = _numberFormatConfig.FormatIRating(Math.Max(0, iRating)); // Kapps: AI "0.0k"
        ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(estimatedDelta is null ? TextAlignment.Center : TextAlignment.Leading));
        fixed (char* p = iratingText)
        {
            var rect = new RectF(bounds.X + 7f, bounds.Y, bounds.X + bounds.Width * 0.62f, bounds.Y + bounds.Height);
            if (estimatedDelta is null) rect = new RectF(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);
            dc->DrawText(p, (uint)iratingText.Length, _fmt.StatusFormat, &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }

        if (estimatedDelta is double delta)
        {
            int rounded = (int)Math.Round(delta);
            SetBrushColor(rounded > 0 ? PaletteTokens.PositiveDelta : rounded < 0 ? PaletteTokens.NegativeDelta : PaletteTokens.TextSecondary);
            string deltaText = rounded == 0 ? "0" : rounded.ToString("+0;-0", CultureInfo.InvariantCulture);
            ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(TextAlignment.Trailing));
            fixed (char* p = deltaText)
            {
                var rect = new RectF(bounds.X + bounds.Width * 0.5f, bounds.Y, bounds.X + bounds.Width - 7f, bounds.Y + bounds.Height);
                dc->DrawText(p, (uint)deltaText.Length, _fmt.StatusFormat, &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
        ThrowIfFailed(_fmt.StatusFormat->SetTextAlignment(TextAlignment.Center));
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
        _fmt.Dispose();
    }
}
