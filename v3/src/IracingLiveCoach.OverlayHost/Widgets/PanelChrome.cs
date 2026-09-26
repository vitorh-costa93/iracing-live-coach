using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// Shared panel look from the mockups: a dark navy, slightly translucent rounded panel with a thin
/// blue-grey border, whose content (header band, player highlight, rows) is clipped to the rounded
/// outline so nothing pokes out of a corner. The clip is a D2D layer with a rounded-rectangle
/// geometry mask; <see cref="PushClip"/> returns a scope that pops the layer and releases the
/// geometry.
/// </summary>
internal static unsafe class PanelChrome
{
    public const float CornerRadius = 7f;

    public readonly ref struct ClipScope
    {
        private readonly ID2D1DeviceContext* _dc;
        private readonly ComPtr<ID2D1RoundedRectangleGeometry> _geometry;

        internal ClipScope(ID2D1DeviceContext* dc, ComPtr<ID2D1RoundedRectangleGeometry> geometry)
        {
            _dc = dc;
            _geometry = geometry;
        }

        public void Dispose()
        {
            _dc->PopLayer();
            _geometry.Dispose();
        }
    }

    public static ClipScope PushClip(ID2D1DeviceContext* dc, RectF rect, float radius = CornerRadius)
    {
        ID2D1Factory* factory = null;
        dc->GetFactory(&factory);

        var rounded = new RoundedRect { rect = rect, radiusX = radius, radiusY = radius };
        ComPtr<ID2D1RoundedRectangleGeometry> geometry = default;
        ThrowIfFailed(factory->CreateRoundedRectangleGeometry(&rounded, geometry.GetAddressOf()));
        factory->Release();

        var parameters = new LayerParameters1
        {
            contentBounds = new RectF(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity),
            geometricMask = (ID2D1Geometry*)geometry.Get(),
            maskAntialiasMode = AntialiasMode.PerPrimitive,
            maskTransform = System.Numerics.Matrix3x2.Identity,
            opacity = 1f,
            opacityBrush = null,
            layerOptions = LayerOptions1.None
        };
        dc->PushLayer(&parameters, null);
        return new ClipScope(dc, geometry);
    }

    /// <summary>Width in DIPs of <paramref name="text"/> in <paramref name="format"/> (DirectWrite
    /// layout metrics; a rough estimate if layout creation fails, so a frame never dies over it).</summary>
    public static float MeasureWidth(IDWriteFactory* factory, IDWriteTextFormat* format, string text)
    {
        if (text.Length == 0) return 0f;
        ComPtr<IDWriteTextLayout> layout = default;
        fixed (char* p = text)
        {
            if (factory->CreateTextLayout(p, (uint)text.Length, format, 2000f, 100f, layout.GetAddressOf()).Failure)
                return text.Length * 6.5f;
        }
        try
        {
            TextMetrics metrics;
            return layout.Get()->GetMetrics(&metrics).Success ? metrics.widthIncludingTrailingWhitespace : text.Length * 6.5f;
        }
        finally { layout.Dispose(); }
    }

    /// <summary>Shortens <paramref name="text"/> with a trailing ellipsis until it fits
    /// <paramref name="maxWidth"/> (a name in a narrow column, or with an enlarged font, must never run
    /// into its neighbour). Results are cached per (text, width, format) since widgets ask every frame.</summary>
    public static string Ellipsize(IDWriteFactory* factory, IDWriteTextFormat* format, string text, float maxWidth, Dictionary<(string, int), string> cache)
    {
        int key = (int)MathF.Round(maxWidth);
        if (cache.TryGetValue((text, key), out var cached)) return cached;
        string result = text;
        if (text.Length > 1 && MeasureWidth(factory, format, text) > maxWidth)
        {
            int length = text.Length - 1;
            while (length > 1 && MeasureWidth(factory, format, text[..length].TrimEnd() + "\u2026") > maxWidth) length--;
            result = text[..length].TrimEnd() + "\u2026";
        }
        if (cache.Count > 512) cache.Clear();
        cache[(text, key)] = result;
        return result;
    }

    /// <summary>Draws text with OpenType tabular figures (<c>tnum</c>) so digits keep one advance and
    /// columns of numbers line up. Fonts that are tabular by default (IBM Plex Sans) or lack the
    /// feature (Chakra Petch) are unaffected; falls back to a plain DrawText if the layout fails.</summary>
    public static void DrawTabularText(ID2D1DeviceContext* dc, IDWriteFactory* factory, ID2D1Brush* brush, IDWriteTextFormat* format, string text, RectF rect)
    {
        if (text.Length == 0) return;
        ComPtr<IDWriteTextLayout> layout = default;
        ComPtr<IDWriteTypography> typography = default;
        fixed (char* p = text)
        {
            float width = Math.Max(1f, rect.Right - rect.Left), height = Math.Max(1f, rect.Bottom - rect.Top);
            if (factory->CreateTextLayout(p, (uint)text.Length, format, width, height, layout.GetAddressOf()).Failure
                || factory->CreateTypography(typography.GetAddressOf()).Failure)
            {
                layout.Dispose(); typography.Dispose();
                var fallback = rect;
                dc->DrawText(p, (uint)text.Length, format, &fallback, brush, DrawTextOptions.None, MeasuringMode.Natural);
                return;
            }
            var feature = new FontFeature { nameTag = FontFeatureTag.TabularFigures, parameter = 1 };
            typography.Get()->AddFontFeature(feature);
            var range = new TextRange { startPosition = 0, length = (uint)text.Length };
            layout.Get()->SetTypography(typography.Get(), range);
            dc->DrawTextLayout(new System.Numerics.Vector2(rect.Left, rect.Top), layout.Get(), brush, DrawTextOptions.None);
        }
        layout.Dispose();
        typography.Dispose();
    }

    /// <summary>Draws one text run inside a box, vertically centred.</summary>
    public static void DrawText(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, IDWriteTextFormat* format, string text, float x, float y, float width, float height, Color4 color, TextAlignment alignment = TextAlignment.Leading)
    {
        if (text.Length == 0) return;
        var c = color;
        brush->SetColor(&c);
        format->SetTextAlignment(alignment);
        fixed (char* p = text)
        {
            var rect = new RectF(x, y, x + width, y + height);
            dc->DrawText(p, (uint)text.Length, format, &rect, (ID2D1Brush*)brush, DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    /// <summary>Draws label + value on one line: the label in <paramref name="labelColor"/>, the
    /// value immediately after it (at the label's measured width) in <paramref name="valueColor"/>.</summary>
    public static float DrawLabelValue(ID2D1DeviceContext* dc, IDWriteFactory* factory, ID2D1SolidColorBrush* brush, IDWriteTextFormat* format,
        string label, string value, float x, float y, float height, Color4 labelColor, Color4 valueColor)
    {
        float labelWidth = MeasureWidth(factory, format, label);
        DrawText(dc, brush, format, label, x, y, labelWidth + 4f, height, labelColor);
        float valueWidth = MeasureWidth(factory, format, value);
        DrawText(dc, brush, format, value, x + labelWidth, y, valueWidth + 4f, height, valueColor);
        return labelWidth + valueWidth;
    }

    /// <summary>Thin vertical divider used between header/footer fields.</summary>
    public static void VerticalDivider(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, float x, float top, float bottom)
    {
        var c = PaletteTokens.PanelDivider;
        brush->SetColor(&c);
        var rect = new RectF(x, top, x + 1f, bottom);
        dc->FillRectangle(&rect, (ID2D1Brush*)brush);
    }

    /// <summary>Thin horizontal divider.</summary>
    public static void HorizontalDivider(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, float left, float right, float y)
    {
        var c = PaletteTokens.PanelDivider;
        brush->SetColor(&c);
        var rect = new RectF(left, y, right, y + 1f);
        dc->FillRectangle(&rect, (ID2D1Brush*)brush);
    }

    /// <summary>Height of a pill/badge inside a row: 24 dip at the default row height, shrinking with the row
    /// so a compact row never gets a badge that fills it edge to edge.</summary>
    public static float BadgeHeight(float rowHeightDip) => Math.Clamp(rowHeightDip * 0.75f, 8f, 24f);

    /// <summary>Licence badge chrome: the class colour as border and text-independent translucent fill.</summary>
    public static void DrawTranslucentBadge(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, RectF pill, Color4 color)
    {
        float radius = Math.Min(5f, (pill.Bottom - pill.Top) / 4f);
        FillPanel(dc, brush, pill, new Color4(color.R, color.G, color.B, 0.22f), radius);
        StrokePanel(dc, brush, pill, color, 1f, radius);
    }

    /// <summary>Panel fill + border (call the border AFTER the clipped content so it sits on top).</summary>
    public static void FillPanel(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, RectF rect, Color4 fill, float radius = CornerRadius)
    {
        var color = fill;
        brush->SetColor(&color);
        var rounded = new RoundedRect { rect = rect, radiusX = radius, radiusY = radius };
        dc->FillRoundedRectangle(&rounded, (ID2D1Brush*)brush);
    }

    public static void StrokePanel(ID2D1DeviceContext* dc, ID2D1SolidColorBrush* brush, RectF rect, Color4 border, float thickness = 1f, float radius = CornerRadius)
    {
        var color = border;
        brush->SetColor(&color);
        // Half-pixel inset so a 1px stroke lands on whole pixels instead of blurring across two.
        var inset = new RectF(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f);
        var rounded = new RoundedRect { rect = inset, radiusX = radius, radiusY = radius };
        dc->DrawRoundedRectangle(&rounded, (ID2D1Brush*)brush, thickness, null);
    }
}
