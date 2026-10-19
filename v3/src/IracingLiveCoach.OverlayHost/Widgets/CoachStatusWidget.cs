using System.Diagnostics;
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
/// "Coach": the last spoken coach message (fades out some seconds after it arrives), the session
/// technique score, a microphone icon showing whether voice is on, and the Weekly Focus on a small
/// line. Reads an immutable <see cref="CoachSnapshot"/>; shows an empty panel when the coach is off.
/// </summary>
public sealed unsafe class CoachStatusWidget : IDisposable
{
    private readonly CoachHost? _coach;
    private CoachSnapshot? _simulated;

    /// <summary>Spec §12's simulation preview -- see FuelWidget.SetSimulatedStatus for the rationale.</summary>
    public void SetSimulated(CoachSnapshot? snapshot) => _simulated = snapshot;

    private ComPtr<IDWriteTextFormat> _messageFormat;
    private ComPtr<IDWriteTextFormat> _labelFormat;
    private ComPtr<IDWriteTextFormat> _bigFormat;
    private ComPtr<IDWriteTextFormat> _unitFormat;
    private ComPtr<ID2D1SolidColorBrush> _brush;

    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteFontCollection1* _fontCollection;
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CoachMessage? _shownMessage;
    private double _shownAt;

    private const float WidthDip = 560f;
    private const float HeightDip = 84f;
    private const double HoldSeconds = 10.0;
    private const double FadeSeconds = 5.0;

    private static readonly Color4 Amber = new(1.0f, 0.78f, 0.17f, 1f);

    public CoachStatusWidget(ID2D1DeviceContext* dc, IDWriteFactory* dwriteFactory, IDWriteFontCollection1* fontCollection = null, CoachHost? coach = null)
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

    private ComPtr<IDWriteTextFormat> MakeFormat(float size, FontWeight weight, WordWrapping wrap = WordWrapping.NoWrap)
    {
        ComPtr<IDWriteTextFormat> format = FontCatalog.CreateFormat(_dwriteFactory, _fontCollection, _appearance.FontFamily, size * _appearance.FontScale, weight, _appearance.FontWeight);
        ThrowIfFailed(format.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(format.Get()->SetWordWrapping(wrap));
        return format;
    }

    private void CreateTextFormats()
    {
        _messageFormat.Dispose();
        _labelFormat.Dispose();
        _bigFormat.Dispose();
        _unitFormat.Dispose();
        _messageFormat = MakeFormat(16f, FontWeight.Medium, WordWrapping.Wrap);
        _labelFormat = MakeFormat(15f, FontWeight.SemiBold);
        _bigFormat = MakeFormat(30f, FontWeight.SemiBold);
        _unitFormat = MakeFormat(13f, FontWeight.Medium);
    }

    public void SetAppearance(WidgetAppearance appearance)
    {
        bool fontChanged = appearance.FontScale != _appearance.FontScale || appearance.FontWeight != _appearance.FontWeight || appearance.FontFamily != _appearance.FontFamily;
        _appearance = appearance;
        if (fontChanged) CreateTextFormats();
    }

    private void SetBrushColor(Color4 color)
    {
        var c = color;
        _brush.Get()->SetColor(&c);
    }

    private static Color4 WithAlpha(Color4 c, float a) => new(c.R, c.G, c.B, c.A * a);

    private static string CategoryText(string category) => category switch
    {
        "TrailBraking" => "Trail braking",
        _ => category,
    };

    /// <summary>Size of the last frame (the overlay auto-fits the window to it when auto-size is on).</summary>
    public (float Width, float Height) LastDrawnSize { get; private set; } = (WidthDip, HeightDip);

    /// <summary>Draws at (x, y) inside a width x height panel. Must be called between BeginFrame/EndFrame.</summary>
    public void Draw(ID2D1DeviceContext* dc, float x, float y, float width = WidthDip, float height = HeightDip)
    {
        width = Math.Max(width, 260f);
        height = Math.Max(height, 60f);
        LastDrawnSize = (width, height);
        var snapshot = _simulated ?? _coach?.Snapshot(includeTrace: false);

        var panel = new RectF(x, y, x + width, y + height);
        PanelChrome.FillPanel(dc, _brush.Get(), panel, PaletteTokens.ResolveBackground(_appearance, PaletteTokens.PanelBackground));

        if (snapshot is null)
        {
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "Coach unavailable", x + 12f, y, width - 24f, height, PaletteTokens.TextDisabled);
            PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
            return;
        }

        bool enabled = snapshot.Settings.Enabled;
        bool voiceOn = enabled && snapshot.Settings.VoiceEnabled;
        const float pad = 10f;
        bool hasFocusLine = enabled;
        float focusH = hasFocusLine ? 20f : 0f;
        float mainH = height - focusH - 6f;
        float scoreW = 84f;

        DrawMicrophone(dc, x + pad + 4f, y + 3f + (mainH - 26f) / 2f, voiceOn, enabled);

        float textX = x + pad + 36f;
        float textW = width - scoreW - (textX - x) - pad;
        if (!enabled)
        {
            PanelChrome.DrawText(dc, _brush.Get(), _labelFormat.Get(), "Coach off", textX, y + 3f, textW, mainH, PaletteTokens.TextDisabled);
        }
        else
        {
            // Fade clock: restarts when a new message arrives (preview: a repeating 18 s loop).
            double now = _clock.Elapsed.TotalSeconds;
            if (_simulated is not null)
            {
                _shownMessage = snapshot.LastMessage;
                _shownAt = now - now % 18.0;
            }
            else if (!ReferenceEquals(snapshot.LastMessage, _shownMessage))
            {
                _shownMessage = snapshot.LastMessage;
                _shownAt = now;
            }
            double age = now - _shownAt;
            float alpha = age <= HoldSeconds ? 1f : (float)Math.Clamp(1.0 - (age - HoldSeconds) / FadeSeconds, 0, 1);
            string text = snapshot.LastMessage?.Text ?? "";
            if (text.Length > 0 && alpha > 0.01f)
                PanelChrome.DrawText(dc, _brush.Get(), _messageFormat.Get(), text, textX, y + 3f, textW, mainH, WithAlpha(PaletteTokens.TextPrimary, alpha));
            else
                PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), snapshot.HasReference ? "Coach ready" : "Coach ready - no reference lap yet",
                    textX, y + 3f, textW, mainH, PaletteTokens.TextDisabled);
        }

        // Session score, right-aligned block.
        float scoreX = x + width - scoreW - pad;
        PanelChrome.VerticalDivider(dc, _brush.Get(), scoreX - 6f, y + 8f, y + 3f + mainH - 6f);
        string score = enabled && snapshot.SessionScore > 0 ? snapshot.SessionScore.ToString(CultureInfo.InvariantCulture) : "--";
        var scoreColor = snapshot.SessionScore >= 80 ? PaletteTokens.PositiveDelta
            : snapshot.SessionScore >= 60 ? Amber
            : snapshot.SessionScore > 0 ? PaletteTokens.NegativeDelta : PaletteTokens.TextDisabled;
        PanelChrome.DrawText(dc, _brush.Get(), _bigFormat.Get(), score, scoreX, y + 3f, scoreW, mainH - 16f, scoreColor, TextAlignment.Center);
        PanelChrome.DrawText(dc, _brush.Get(), _unitFormat.Get(), "Session score", scoreX - 4f, y + 3f + mainH - 18f, scoreW + 8f, 16f, PaletteTokens.TextSecondary, TextAlignment.Center);

        if (hasFocusLine)
        {
            PanelChrome.HorizontalDivider(dc, _brush.Get(), x + 8f, x + width - 8f, y + height - focusH - 3f);
            string focus = string.IsNullOrEmpty(snapshot.WeeklyFocus) ? "Collecting laps..." : CategoryText(snapshot.WeeklyFocus!);
            PanelChrome.DrawLabelValue(dc, _dwriteFactory, _brush.Get(), _unitFormat.Get(), "Weekly focus: ", focus,
                x + pad, y + height - focusH - 2f, focusH, PaletteTokens.TextSecondary, string.IsNullOrEmpty(snapshot.WeeklyFocus) ? PaletteTokens.TextDisabled : PaletteTokens.TextPrimary);
        }

        PanelChrome.StrokePanel(dc, _brush.Get(), panel, PaletteTokens.PanelBorder);
    }

    /// <summary>Vector microphone (capsule, cradle, stand); cyan when voice is on, dim with a red slash when off.</summary>
    private void DrawMicrophone(ID2D1DeviceContext* dc, float x, float y, bool voiceOn, bool coachOn)
    {
        var color = voiceOn ? PaletteTokens.FocusOutline : PaletteTokens.TextDisabled;
        SetBrushColor(color);
        var capsule = new RoundedRect { rect = new RectF(x + 7f, y, x + 17f, y + 15f), radiusX = 5f, radiusY = 5f };
        dc->FillRoundedRectangle(&capsule, (ID2D1Brush*)_brush.Get());
        var b = (ID2D1Brush*)_brush.Get();
        dc->DrawLine(new System.Numerics.Vector2(x + 4f, y + 11f), new System.Numerics.Vector2(x + 4f, y + 15f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 4f, y + 15f), new System.Numerics.Vector2(x + 8f, y + 20f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 8f, y + 20f), new System.Numerics.Vector2(x + 16f, y + 20f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 16f, y + 20f), new System.Numerics.Vector2(x + 20f, y + 15f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 20f, y + 15f), new System.Numerics.Vector2(x + 20f, y + 11f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 12f, y + 20f), new System.Numerics.Vector2(x + 12f, y + 25f), b, 1.8f, null);
        dc->DrawLine(new System.Numerics.Vector2(x + 7f, y + 25f), new System.Numerics.Vector2(x + 17f, y + 25f), b, 1.8f, null);
        if (!voiceOn)
        {
            SetBrushColor(coachOn ? PaletteTokens.NegativeDelta : PaletteTokens.TextDisabled);
            dc->DrawLine(new System.Numerics.Vector2(x + 1f, y + 2f), new System.Numerics.Vector2(x + 23f, y + 26f), b, 2.2f, null);
        }
    }

    public void Dispose()
    {
        _messageFormat.Dispose();
        _labelFormat.Dispose();
        _bigFormat.Dispose();
        _unitFormat.Dispose();
        _brush.Dispose();
    }
}
