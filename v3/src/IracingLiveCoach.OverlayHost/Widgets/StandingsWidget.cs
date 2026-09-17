using System.Globalization;
using IracingLiveCoach.Core;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Assets;
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

    /// <summary>Non-null while showing fictitious data for layout verification, per spec §12's
    /// explicit requirement: "preview com dados fictícios claramente identificado como simulação,
    /// disponível sem iRacing aberto". Never used as a fallback for missing real data.</summary>
    private List<StandingsRow>? _simulatedRows;

    private ComPtr<IDWriteTextFormat> _nameFormat;
    private ComPtr<IDWriteTextFormat> _statusFormat;
    private ComPtr<IDWriteTextFormat> _numericFormat; // right-aligned, for gap/interval/lap-time/delta columns
    private ComPtr<ID2D1SolidColorBrush> _brush; // color set per-draw via SetColor; one brush reused throughout.

    private const float RowHeightDip = 24f;
    private const float ClassHeaderHeightDip = 18f;
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
    private const float ColumnGapDip = 6f;

    public StandingsWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags)
    {
        _flags = flags;
        ComPtr<IDWriteTextFormat> nameFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 15f, fontWeight: FontWeight.Medium, localeName: "en-us");
        ThrowIfFailed(nameFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(nameFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _nameFormat = nameFormat;

        ComPtr<IDWriteTextFormat> statusFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 14f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        ThrowIfFailed(statusFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetTextAlignment(TextAlignment.Center));
        ThrowIfFailed(statusFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _statusFormat = statusFormat;

        ComPtr<IDWriteTextFormat> numericFormat = dwriteFactory->CreateTextFormat("Barlow Semi Condensed", 14f, fontWeight: FontWeight.Medium, localeName: "en-us");
        ThrowIfFailed(numericFormat.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(numericFormat.Get()->SetTextAlignment(TextAlignment.Trailing));
        ThrowIfFailed(numericFormat.Get()->SetWordWrapping(WordWrapping.NoWrap));
        _numericFormat = numericFormat;

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;

        _telemetry = new TelemetryReader();
        _telemetry.StandingsUpdated += OnStandingsUpdated;
        _telemetry.Start();
    }

    private void OnStandingsUpdated(List<StandingsRow> rows)
    {
        lock (_lock) { _rows = rows; }
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
            DrawRows(dc, x, y, simulated);
            return;
        }

        List<StandingsRow> rows;
        lock (_lock) { rows = _rows; }

        if (!_telemetry.HasRecentTelemetry || rows.Count == 0)
        {
            DrawWaitingState(dc, x, y);
            return;
        }

        DrawRows(dc, x, y, rows);
    }

    private void DrawRows(ID2D1DeviceContext* dc, float x, float y, IReadOnlyList<StandingsRow> rows)
    {
        var groups = StandingsSelection.GroupAndSelect(rows);
        bool multiClass = groups.Count > 1;
        float rowY = y;
        foreach (var group in groups)
        {
            if (multiClass)
            {
                DrawClassHeader(dc, x, rowY, group.ClassShortName, group.ClassId, group.ClassColorHex);
                rowY += ClassHeaderHeightDip;
            }
            foreach (var row in group.Rows)
            {
                DrawRow(dc, x, rowY, row);
                rowY += RowHeightDip;
            }
        }
    }

    private void DrawClassHeader(ID2D1DeviceContext* dc, float x, float y, string name, int classId, string? classColorHex)
    {
        var color = PaletteTokens.ResolveClassColor(classId, name, classColorHex);
        SetBrushColor(PaletteTokens.SessionHeaderBand);
        var background = new RectF(x, y, x + 600f, y + ClassHeaderHeightDip);
        dc->FillRectangle(&background, (ID2D1Brush*)_brush.Get());
        SetBrushColor(color);
        var strip = new RectF(x, y, x + ClassStripWidthDip, y + ClassHeaderHeightDip);
        dc->FillRectangle(&strip, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.TextPrimary);
        string text = string.IsNullOrWhiteSpace(name) ? "CLASS" : name;
        fixed (char* p = text)
        {
            var rect = new RectF(x + 8f, y, x + 180f, y + ClassHeaderHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
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

        float cursorX = x + ClassStripWidthDip + 4f;

        // Position.
        SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
        string posText = row.Position.ToString();
        fixed (char* p = posText)
        {
            var rect = new RectF(cursorX, y, cursorX + PositionColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)posText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += PositionColumnWidthDip;

        // Car number is the real SDK DriverInfo.CarNumber, kept separate from CarIdx and from
        // the rendered row index. Preserve source leading zeroes and use an explicit # prefix.
        SetBrushColor(PaletteTokens.TextSecondary);
        string carNumber = string.IsNullOrWhiteSpace(row.CarNumber) ? "—" : $"#{row.CarNumber}";
        fixed (char* p = carNumber)
        {
            var rect = new RectF(cursorX, y, cursorX + CarNumberColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)carNumber.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += CarNumberColumnWidthDip;

        // Real local PNG flag asset, decoded once through WIC and reused as a device bitmap.
        var flag = _flags.Find(row.FlagEmoji);
        if (flag != null)
        {
            var destination = new RectF(cursorX + 1f, y + 4f, cursorX + FlagColumnWidthDip - 1f, y + RowHeightDip - 4f);
            dc->DrawBitmap(flag, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
        }
        cursorX += FlagColumnWidthDip;

        // Brand image follows the same cache/recovery path as flags. Text is an explicit fallback
        // only for a make for which no local V2 asset exists.
        var brandBitmap = _flags.FindBrand(row.ManufacturerBadge);
        if (brandBitmap != null)
        {
            var destination = new RectF(cursorX + 2f, y + 3f, cursorX + BrandColumnWidthDip - 2f, y + RowHeightDip - 3f);
            dc->DrawBitmap(brandBitmap, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
        }
        else if (!string.IsNullOrWhiteSpace(row.ManufacturerBadge))
        {
            SetBrushColor(PaletteTokens.TextSecondary);
            string brand = row.ManufacturerBadge;
            fixed (char* p = brand)
            {
                var rect = new RectF(cursorX, y, cursorX + BrandColumnWidthDip, y + RowHeightDip);
                dc->DrawText(p, (uint)brand.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
            }
        }
        cursorX += BrandColumnWidthDip;

        // Driver name (full name is expected to already be in DriverCode per spec §5 -- this widget
        // does not truncate or abbreviate on its own).
        SetBrushColor(row.IsPlayer ? PaletteTokens.PlayerHighlight : PaletteTokens.TextPrimary);
        string name = row.DriverCode;
        fixed (char* p = name)
        {
            var rect = new RectF(cursorX, y, cursorX + NameColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)name.Length, _nameFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += NameColumnWidthDip + ColumnGapDip;

        DrawLicenseBadge(dc, cursorX, y, row.LicString, row.LicColorHex);
        cursorX += LicenseColumnWidthDip + ColumnGapDip;

        // iRating + Δ combined badge, same row, same rectangle (spec §6: never stacked, never duplicated).
        var badgeRect = new Rect2D(cursorX, y + (RowHeightDip - BadgeHeightDip) / 2, BadgeWidthDip, BadgeHeightDip);
        DrawIRatingBadge(dc, badgeRect, row.IRating, row.EstimatedDeltaIRating);
        cursorX += BadgeWidthDip + ColumnGapDip;

        // Gap to leader -- distinct field from lap-delta-vs-player (spec §6: "não confunda esse
        // valor com gap de corrida"). Leader's own row has no gap (shown "—" via DrawNumericOrDash's
        // null-handling below, matching what the real V2 view model already does for the leader).
        cursorX = DrawNumericOrDash(dc, cursorX, y, GapColumnWidthDip, row.GapToLeaderSeconds,
            v => v.ToString("0.000", CultureInfo.InvariantCulture), PaletteTokens.TextSecondary);

        // Interval is intentionally rendered as a distinct live race metric; it is never derived
        // from gap-to-leader and a missing SDK value remains an explicit dash.
        cursorX = DrawNumericOrDash(dc, cursorX, y, IntervalColumnWidthDip, row.IntervalSeconds,
            v => v.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture), PaletteTokens.TextSecondary);

        // Last lap time (m:ss.sss via the shared LapTimeFormatting helper -- not reimplemented here).
        cursorX = DrawNumericOrDash(dc, cursorX, y, LastLapColumnWidthDip, row.LastLapTime,
            LapTimeFormatting.Format, PaletteTokens.TextPrimary);

        // Lap-delta-vs-player: negative = this driver faster than the player (spec §6's sign
        // convention), colored green/red; player's own row always shows a neutral 0.000.
        var deltaColor = row.IsPlayer ? PaletteTokens.NeutralDeltaOrGap
            : row.LapDeltaVsPlayerSeconds switch
            {
                < 0 => PaletteTokens.LapDeltaFaster,
                > 0 => PaletteTokens.LapDeltaSlower,
                _ => PaletteTokens.NeutralDeltaOrGap
            };
        double? deltaValue = row.IsPlayer ? 0.0 : row.LapDeltaVsPlayerSeconds;
        cursorX = DrawNumericOrDash(dc, cursorX, y, LapDeltaColumnWidthDip, deltaValue, v => v.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture), deltaColor);

        DrawOvertakeCell(dc, cursorX, y, row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown);
    }

    /// <returns>The cursor X position after this column (its right edge + the standard column gap).</returns>
    private float DrawNumericOrDash(ID2D1DeviceContext* dc, float x, float y, float widthDip, double? value, Func<double, string> format, Color4 color)
    {
        SetBrushColor(value is null ? PaletteTokens.TextDisabled : color);
        string text = value is double v ? format(v) : "—"; // spec §17/§7: missing data is an explicit dash, never a fabricated zero.
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + widthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _numericFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        return x + widthDip + ColumnGapDip;
    }

    private readonly record struct Rect2D(float X, float Y, float Width, float Height);

    private void DrawLicenseBadge(ID2D1DeviceContext* dc, float x, float y, string license, string? colorHex)
    {
        var color = ParseHexOrFallback(colorHex, PaletteTokens.LicenseUnknown);
        SetBrushColor(color);
        var rr = new RoundedRect
        {
            rect = new RectF(x, y + 3f, x + LicenseColumnWidthDip, y + RowHeightDip - 3f),
            radiusX = PaletteTokens.BadgeCornerRadiusPx,
            radiusY = PaletteTokens.BadgeCornerRadiusPx
        };
        dc->FillRoundedRectangle(&rr, (ID2D1Brush*)_brush.Get());
        SetBrushColor(PaletteTokens.TextPrimary);
        string text = string.IsNullOrWhiteSpace(license) ? "—" : license;
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + LicenseColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawOvertakeCell(ID2D1DeviceContext* dc, float x, float y, bool? active, double? seconds, bool cooldown)
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
            var textRect = new RectF(x, y, x + OvertakeColumnWidthDip, y + 13f);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &textRect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        var track = new RectF(x + 2f, y + 17f, x + OvertakeColumnWidthDip - 2f, y + 20f);
        SetBrushColor(PaletteTokens.BarTrackEmpty);
        dc->FillRectangle(&track, (ID2D1Brush*)_brush.Get());
        if (seconds is not double bank) return;
        float fraction = Math.Clamp((float)(bank / TelemetryReader.P2PMaxSeconds), 0f, 1f);
        if (fraction <= 0f) return;
        var fill = new RectF(x + 2f, y + 17f, x + 2f + (OvertakeColumnWidthDip - 4f) * fraction, y + 20f);
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
        string iratingText = iRating.ToString("N0", CultureInfo.InvariantCulture);
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
        _telemetry.Dispose();
        _brush.Dispose();
        _numericFormat.Dispose();
        _statusFormat.Dispose();
        _nameFormat.Dispose();
    }
}
