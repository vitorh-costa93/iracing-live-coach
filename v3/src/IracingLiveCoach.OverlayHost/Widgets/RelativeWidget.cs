using System.Globalization;
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

    private const float RowHeightDip = 22f;
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

    public RelativeWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, FlagBitmapCache flags)
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
        _telemetry.FullRelativeUpdated += OnFullRelativeUpdated;
        _telemetry.SessionStatusUpdated += OnSessionStatusUpdated;
        _telemetry.PlayerCarStatusUpdated += OnPlayerCarStatusUpdated;
        _telemetry.Start();
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
        foreach (var row in rows)
        {
            DrawRow(dc, x, rowY, row);
            rowY += RowHeightDip;
        }
    }

    private void DrawHeader(ID2D1DeviceContext* dc, float x, float y, SessionStatus? session, PlayerCarStatus? player)
    {
        SetBrushColor(PaletteTokens.SessionHeaderBand);
        var band = new RectF(x, y, x + 600f, y + HeaderHeightDip);
        dc->FillRectangle(&band, (ID2D1Brush*)_brush.Get());
        string sessionText = session is null
            ? "RELATIVE"
            : $"{session.CarClassShortName}  {session.SessionTypeText}  LAP {session.CurrentLap?.ToString(CultureInfo.InvariantCulture) ?? "—"}/{session.TotalLaps?.ToString(CultureInfo.InvariantCulture) ?? "—"}";
        if (player is not null)
        {
            var track = player.TrackTempC is double temperature ? $"  TRACK {temperature:0.#}°C" : "";
            var brake = player.BrakeBiasPct is double bias ? $"  BB {bias:0.0}%" : "";
            sessionText += track + brake;
        }
        string local = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        SetBrushColor(PaletteTokens.TextPrimary);
        fixed (char* p = sessionText)
        {
            var rect = new RectF(x + 8f, y, x + 450f, y + HeaderHeightDip);
            dc->DrawText(p, (uint)sessionText.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        SetBrushColor(PaletteTokens.TextSecondary);
        fixed (char* p = local)
        {
            var rect = new RectF(x + 540f, y, x + 596f, y + HeaderHeightDip);
            dc->DrawText(p, (uint)local.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    private void DrawRow(ID2D1DeviceContext* dc, float x, float y, RelativeRow row)
    {
        var stripColor = PaletteTokens.ResolveClassColor(row.CarClassId, row.ClassShortName, row.ClassColorHex);
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

        SetBrushColor(PaletteTokens.TextSecondary);
        string carNumber = string.IsNullOrWhiteSpace(row.CarNumber) ? "—" : $"#{row.CarNumber}";
        fixed (char* p = carNumber)
        {
            var rect = new RectF(cursorX, y, cursorX + CarNumberColumnWidthDip, y + RowHeightDip);
            dc->DrawText(p, (uint)carNumber.Length, _statusFormat.Get(), &rect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        cursorX += CarNumberColumnWidthDip;

        // Flag and brand use the same real transparent assets as Standings.
        var flag = _flags.Find(row.FlagEmoji);
        if (flag != null)
        {
            var destination = new RectF(cursorX + 1f, y + 4f, cursorX + FlagColumnWidthDip - 1f, y + RowHeightDip - 4f);
            dc->DrawBitmap(flag, &destination, 1f, InterpolationMode.HighQualityCubic, null, null);
        }
        cursorX += FlagColumnWidthDip;

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
        cursorX += GapColumnWidthDip + ColumnGapDip;
        DrawOvertakeCell(dc, cursorX, y, row.P2PActive, row.P2PSecondsRemaining, row.P2PInCooldown);
    }

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
            var textRect = new RectF(x, y, x + OvertakeColumnWidthDip, y + 12f);
            dc->DrawText(p, (uint)text.Length, _statusFormat.Get(), &textRect, (ID2D1Brush*)_brush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }
        var track = new RectF(x + 2f, y + 16f, x + OvertakeColumnWidthDip - 2f, y + 19f);
        SetBrushColor(PaletteTokens.BarTrackEmpty);
        dc->FillRectangle(&track, (ID2D1Brush*)_brush.Get());
        if (seconds is not double bank) return;
        float fraction = Math.Clamp((float)(bank / TelemetryReader.P2PMaxSeconds), 0f, 1f);
        if (fraction <= 0f) return;
        var fill = new RectF(x + 2f, y + 16f, x + 2f + (OvertakeColumnWidthDip - 4f) * fraction, y + 19f);
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
