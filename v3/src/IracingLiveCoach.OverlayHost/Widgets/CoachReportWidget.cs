using System.Globalization;
using IracingLiveCoach.Core.Coach;
using IracingLiveCoach.OverlayHost.Coach;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// "AI Coach Report": the last valid lap's corner-by-corner table from the coach engine's LapReport
/// (turn, brake-point delta in metres, peak-brake delta, time lost, main issue), sorted by time lost,
/// with the lap delta vs the reference and the Technique Score + Braking/Trail/Throttle/Consistency
/// breakdown. Reads an immutable <see cref="CoachSnapshot"/>; with no report yet it shrinks to a
/// single discreet line.
/// </summary>
public sealed unsafe class CoachReportWidget : IDisposable
{
    private readonly CoachHost? _coach;
    private CoachSnapshot? _simulated;

    /// <summary>Spec §12's simulation preview -- see FuelWidget.SetSimulatedStatus for the rationale.</summary>
    public void SetSimulated(CoachSnapshot? snapshot) => _simulated = snapshot;

    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _valueFormat;
    private ComPtr<IDWriteTextFormat> _unitFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private const float WidthDip = 520f;
    private const float EmptyHeightDip = 30f;
    private const int MaxRows = 10;

    private static readonly Color4 Amber = new(1.0f, 0.78f, 0.17f, 1f);

    public CoachReportWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null, CoachHost? coach = null)
    {
        _dwriteFactory = dwriteFactory;
        _fontCollection = fontCollection;
        _coach = coach;
        CreateTextFormats();

        var white = PaletteTokens.TextPrimary;
        ComPtr<ID2D1SolidColorBrush> brush = default;
        ThrowIfFailed(dc->CreateSolidColorBrush(&white, null, brush.GetAddressOf()));
        _brush = brush;
    }

    private ComPtr<IDWriteTextFormat> MakeFormat(float size, FontWeight weight)
    {
        ComPtr<IDWriteTextFormat> format = FontCatalog.CreateFormat(_dwriteFactory, _fontCollection, _appearance.FontFamily, size * _appearance.FontScale, weight, _appearance.FontWeight);
        ThrowIfFailed(format.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(format.Get()->SetWordWrapping(WordWrapping.NoWrap));
        return format;
    }

    private void CreateTextFormats()
    {
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _unitFormat.Dispose();
        _labelFormat = MakeFormat(15f, FontWeight.SemiBold);
        _valueFormat = MakeFormat(22f, FontWeight.SemiBold);
        _unitFormat = MakeFormat(13f, FontWeight.Medium);
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight || appearance.FontFamily != _appearance.FontFamily;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    /// <summary>Size of the last frame (the overlay auto-fits the window to it).</summary>
    public (float Width, float Height) LastDrawnSize { get; private set; } = (WidthDip, EmptyHeightDip);

    private static string LapTime(double sec)
    {
        int m = (int)(sec / 60);
        return $"{m}:{sec - m * 60:00.000}";
    }

    private static string Signed(double value, string format) =>
        (value >= 0 ? "+" : "-") + Math.Abs(value).ToString(format, CultureInfo.InvariantCulture);

    private static string IssueText(string issue) => issue switch
    {
        nameof(CornerIssue.BrakeTooEarly) => "Braked too early",
        nameof(CornerIssue.BrakeTooLate) => "Braked too late",
        nameof(CornerIssue.BrakeTooSoft) => "Brake pressure low",
        nameof(CornerIssue.TrailBrakeShort) => "Trail brake too short",
        nameof(CornerIssue.MinSpeedLow) => "Low minimum speed",
        nameof(CornerIssue.ThrottleLate) => "Throttle too late",
        _ => "-",
    };

    private static Color4 ScoreColor(int score) =>
        score >= 80 ? PaletteTokens.PositiveDelta : score >= 60 ? Amber : PaletteTokens.NegativeDelta;

    private static Color4 LossColor(double lost) =>
        lost >= 0.10 ? PaletteTokens.NegativeDelta : lost >= 0.03 ? Amber : lost <= -0.02 ? PaletteTokens.PositiveDelta : PaletteTokens.TextSecondary;

    /// <summary>Draws at (x, y). Must be called between BeginFrame/EndFrame.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip)
    {
        width = Math.Max(width, 320f);
        var snapshot = _simulated ?? _coach?.Snapshot(includeTrace: false);
        var report = snapshot?.Report;

        if (snapshot is null || !snapshot.Settings.Enabled || report is null)
        {
            // Discreet: one dim line, no table.
            LastDrawnSize = (width, EmptyHeightDip);
            var small = new RectF(x, y, x + width, y + EmptyHeightDip);
            PanelChrome.FillPanel(dc, _brush.Get(), small, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));
            string text = snapshot is null ? "Coach unavailable" : !snapshot.Settings.Enabled ? "Coach off" : "No lap report yet";
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), text, x + 12f, y, width - 24f, EmptyHeightDip, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), small, PaletteTokens.PanelBorder);
            return;
        }

        var rows = report.Corners.OrderByDescending(c => c.TimeLostSec).Take(MaxRows).ToList();
        const float headerH = 30f, breakdownH = 24f, colHeadH = 18f, rowH = 21f;
        float height = 4f + headerH + breakdownH + colHeadH + rows.Count * rowH + 8f;
        LastDrawnSize = (width, height);

        var panel = new RectF(x, y, x + width, y + height);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));

        float top = y + 4f;
        const float pad = 10f;

        // Header: lap, lap time, delta vs reference | technique score.
        float lx = x + pad;
        lx += PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _labelFormat.Get(), "Lap ", report.Lap.ToString(CultureInfo.InvariantCulture),
            lx, top, headerH, PaletteTokens.TextSecondary, PaletteTokens.TextPrimary) + 14f;
        string lapTime = LapTime(report.LapTimeSec);
        float ltw = PanelChrome.MeasureWidth(_dwriteFactory, _valueFormat.Get(), lapTime);
        PanelChrome.DrawText(dc, _brush.Get(), _valueFormat.Get(), lapTime, lx, top, ltw + 4f, headerH, PaletteTokens.TextPrimary);
        lx += ltw + 14f;
        if (snapshot.HasReference)
        {
            var deltaColor = report.DeltaToRefSec <= 0 ? PaletteTokens.PositiveDelta : PaletteTokens.NegativeDelta;
            PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _labelFormat.Get(), "vs ref ", Signed(report.DeltaToRefSec, "0.000") + " s",
                lx, top, headerH, PaletteTokens.TextSecondary, deltaColor);
        }
        string score = report.TechniqueScore.ToString(CultureInfo.InvariantCulture);
        float sw = PanelChrome.MeasureWidth(_dwriteFactory, _valueFormat.Get(), score);
        PanelChrome.DrawText(dc, _brush.Get(), _valueFormat.Get(), score, x + width - pad - sw - 4f, top, sw + 4f, headerH, ScoreColor(report.TechniqueScore));
        float labelW = PanelChrome.MeasureWidth(_dwriteFactory, _labelFormat.Get(), "Technique Score ");
        PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "Technique Score ", x + width - pad - sw - 4f - labelW, top, labelW + 4f, headerH, PaletteTokens.TextSecondary);

        // Breakdown: four equal cells.
        float by = top + headerH;
        if (snapshot.Breakdown is { } b)
        {
            var cells = new (string Label, int Value)[] { ("Braking", b.Braking), ("Trail", b.TrailBraking), ("Throttle", b.Throttle), ("Consistency", b.Consistency) };
            float cw = (width - 2 * pad) / cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _unitFormat.Get(), cells[i].Label + " ", cells[i].Value.ToString(CultureInfo.InvariantCulture),
                    x + pad + cw * i, by, breakdownH, PaletteTokens.TextSecondary, ScoreColor(cells[i].Value));
            }
        }
        PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, by + breakdownH);

        // Table.
        float c0 = x + pad, c1 = x + pad + 40f, c2 = c1 + 82f, c3 = c2 + 76f, c4 = c3 + 64f, cEnd = x + width - pad;
        float ty = by + breakdownH + 1f;
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Turn", c0, ty, c1 - c0, colHeadH, PaletteTokens.TextSecondary);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Brake pt", c1, ty, c2 - c1, colHeadH, PaletteTokens.TextSecondary, TextAlignment.Trailing);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Peak brake", c2, ty, c3 - c2, colHeadH, PaletteTokens.TextSecondary, TextAlignment.Trailing);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Lost", c3, ty, c4 - c3, colHeadH, PaletteTokens.TextSecondary, TextAlignment.Trailing);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Main issue", c4 + 14f, ty, cEnd - c4 - 14f, colHeadH, PaletteTokens.TextSecondary);
        float ry = ty + colHeadH;
        foreach (var c in rows)
        {
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "T" + c.Index.ToString(CultureInfo.InvariantCulture), c0, ry, c1 - c0, rowH, PaletteTokens.TextPrimary);
            var brakeColor = Math.Abs(c.BrakePointDeltaM) >= 8 ? PaletteTokens.NegativeDelta : Math.Abs(c.BrakePointDeltaM) >= 3 ? Amber : PaletteTokens.TextPrimary;
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), Signed(c.BrakePointDeltaM, "0") + " m", c1, ry, c2 - c1, rowH, brakeColor, TextAlignment.Trailing);
            var peakColor = c.PeakBrakeDelta <= -0.15 ? PaletteTokens.NegativeDelta : PaletteTokens.TextPrimary;
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), Signed(c.PeakBrakeDelta * 100.0, "0") + "%", c2, ry, c3 - c2, rowH, peakColor, TextAlignment.Trailing);
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), Signed(c.TimeLostSec, "0.00") + " s", c3, ry, c4 - c3, rowH, LossColor(c.TimeLostSec), TextAlignment.Trailing);
            string issue = IssueText(c.MainIssue);
            PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), issue, c4 + 14f, ry, cEnd - c4 - 14f, rowH,
                issue == "-" ? PaletteTokens.TextDisabled : PaletteTokens.TextPrimary);
            ry += rowH;
        }

        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    public void Dispose()
    {
        _labelFormat.Dispose();
        _valueFormat.Dispose();
        _unitFormat.Dispose();
        _brush.Dispose();
    }
}
