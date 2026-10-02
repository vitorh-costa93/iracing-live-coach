using System.Numerics;
using Ams2.OverlayHost.Gfx;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;
using D2DGradientStop = Vortice.Win32.Graphics.Direct2D.Common.GradientStop;

namespace Ams2.OverlayHost.Theme;

public enum HAlign { Left, Center, Right }

/// <summary>
/// Superfície de desenho de um widget: aplica o tema (fontes, cores, sombra) e a escala.
/// Os widgets desenham em unidades de design; <c>scale</c> vira uma transformação do contexto D2D.
/// Mantém os formatos de texto e o pincel em cache e os refaz quando o device é recriado.
/// </summary>
public sealed unsafe class ThemeCanvas : IDisposable
{
    readonly DeviceResources _gfx;
    readonly Dictionary<(FontToken, HAlign), ComPtr<IDWriteTextFormat>> _formats = [];
    ComPtr<ID2D1SolidColorBrush> _brush;
    int _generation;

    public Theme Theme { get; set; }
    public float Scale { get; set; }
    /// <summary>Opacidade global do widget (0..1): multiplica o alfa de toda cor desenhada.</summary>
    public float Opacity { get; set; } = 1f;

    string? _fontOverride;
    /// <summary>Familia que substitui as fontes de texto do tema; a familia de numeros do tema nunca e trocada.</summary>
    public string? FontOverride
    {
        get => _fontOverride;
        set
        {
            if (_fontOverride == value) return;
            _fontOverride = value;
            foreach (var f in _formats.Values) f.Dispose();
            _formats.Clear();
        }
    }

    public ThemeCanvas(DeviceResources gfx, Theme theme, float scale = 1f)
    {
        _gfx = gfx; Theme = theme; Scale = scale;
        _generation = gfx.Generation;
    }

    ID2D1DeviceContext* Dc => _gfx.Context;

    void EnsureResources()
    {
        if (_generation != _gfx.Generation)
        {
            foreach (var f in _formats.Values) f.Dispose();
            _formats.Clear();
            _brush.Dispose();
            _brush = default;
            _generation = _gfx.Generation;
        }
        if (_brush.Get() == null)
        {
            var c = new Color4(1, 1, 1, 1);
            ComPtr<ID2D1SolidColorBrush> b = default;
            ThrowIfFailed(Dc->CreateSolidColorBrush(&c, null, b.GetAddressOf()));
            _brush = b;
        }
    }

    /// <summary>Inicia o quadro: aplica a escala. Chamar entre BeginFrame/EndFrame do <see cref="DeviceResources"/>.</summary>
    public void Begin()
    {
        EnsureResources();
        var m = Matrix3x2.CreateScale(Scale);
        Dc->SetTransform(&m);
    }

    public void End()
    {
        var id = Matrix3x2.Identity;
        Dc->SetTransform(&id);
    }

    IDWriteTextFormat* Format(FontToken font, HAlign align)
    {
        if (_formats.TryGetValue((font, align), out var cached)) return cached.Get();
        var requested = font;
        if (_fontOverride is not null && font.Family != Theme.Numbers.Family) font = font with { Family = _fontOverride };
        bool has = _gfx.Fonts.Has(font.Family);
        string family = has ? font.Family : "Segoe UI";
        var collection = has ? (IDWriteFontCollection*)_gfx.Fonts.Collection : null;
        var fmt = _gfx.DWriteFactory->CreateTextFormat(family, collection, font.Size,
            fontWeight: (FontWeight)font.Weight, localeName: "en-us");
        ThrowIfFailed(fmt.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(fmt.Get()->SetWordWrapping(WordWrapping.NoWrap));
        ThrowIfFailed(fmt.Get()->SetTextAlignment(align switch
        {
            HAlign.Center => TextAlignment.Center,
            HAlign.Right => TextAlignment.Trailing,
            _ => TextAlignment.Leading,
        }));
        _formats[(requested, align)] = fmt;
        return fmt.Get();
    }

    ID2D1Brush* Solid(Color4 c)
    {
        c = new Color4(c.R, c.G, c.B, c.A * Opacity);
        _brush.Get()->SetColor(&c);
        return (ID2D1Brush*)_brush.Get();
    }

    public void FillRect(float x, float y, float w, float h, Color4 color)
    {
        var r = new RectF(x, y, x + w, y + h);
        Dc->FillRectangle(&r, Solid(color));
    }

    public void FillEllipse(float cx, float cy, float rx, float ry, Color4 color)
    {
        var e = new Ellipse { point = new Vector2(cx, cy), radiusX = rx, radiusY = ry };
        Dc->FillEllipse(&e, Solid(color));
    }

    public void Line(float x1, float y1, float x2, float y2, Color4 color, float width = 1f)
        => Dc->DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Solid(color), width, null);

    public void StrokeRect(float x, float y, float w, float h, Color4 color, float width)
    {
        float i = width / 2;
        var r = new RectF(x + i, y + i, x + w - i, y + h - i);
        Dc->DrawRectangle(&r, Solid(color), width, null);
    }

    /// <summary>Painel do tema: preenchimento + borda, com canto arredondado se o token pedir.</summary>
    public void Panel(float x, float y, float w, float h)
    {
        var t = Theme;
        if (t.CornerRadius <= 0.5f)
        {
            FillRect(x, y, w, h, t.PanelFill);
            if (t.BorderWidth > 0) StrokeRect(x, y, w, h, t.PanelBorder, t.BorderWidth);
            return;
        }
        var rr = new RoundedRect { rect = new RectF(x, y, x + w, y + h), radiusX = t.CornerRadius, radiusY = t.CornerRadius };
        Dc->FillRoundedRectangle(&rr, Solid(t.PanelFill));
        if (t.BorderWidth > 0)
        {
            float i = t.BorderWidth / 2;
            rr.rect = new RectF(x + i, y + i, x + w - i, y + h - i);
            Dc->DrawRoundedRectangle(&rr, Solid(t.PanelBorder), t.BorderWidth, null);
        }
    }

    /// <summary>Barra horizontal com gradiente (a "barra dourada" ao lado do título).</summary>
    public void GradientBar(float x, float y, float w, float h, BarStop[] stops)
    {
        var d2d = new D2DGradientStop[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            var sc0 = stops[i].Color; var sc = new Color4(sc0.R, sc0.G, sc0.B, sc0.A * Opacity);
            d2d[i] = new D2DGradientStop { position = stops[i].Position, color = sc };
        }
        ComPtr<ID2D1GradientStopCollection> collection = default;
        fixed (D2DGradientStop* p = d2d)
            ThrowIfFailed(Dc->CreateGradientStopCollection(p, (uint)d2d.Length, Gamma.Gamma_2_2, ExtendMode.Clamp, collection.GetAddressOf()));
        using var _c = collection;
        var props = new LinearGradientBrushProperties { startPoint = new Vector2(x, 0), endPoint = new Vector2(x + w, 0) };
        ComPtr<ID2D1LinearGradientBrush> brush = default;
        ThrowIfFailed(Dc->CreateLinearGradientBrush(&props, null, collection.Get(), brush.GetAddressOf()));
        using var _b = brush;
        var r = new RectF(x, y, x + w, y + h);
        Dc->FillRectangle(&r, (ID2D1Brush*)brush.Get());
    }

    /// <summary>Largura natural do texto em unidades de design.</summary>
    public float Measure(string text, FontToken font)
    {
        if (text.Length == 0) return 0;
        ComPtr<IDWriteTextLayout> layout = default;
        fixed (char* p = text)
            ThrowIfFailed(_gfx.DWriteFactory->CreateTextLayout(p, (uint)text.Length, Format(font, HAlign.Left), 4000f, 200f, layout.GetAddressOf()));
        using var _l = layout;
        TextMetrics m;
        ThrowIfFailed(layout.Get()->GetMetrics(&m));
        return m.widthIncludingTrailingWhitespace;
    }

    /// <summary>Texto centralizado verticalmente na caixa, com sombra opcional (desenhada antes, deslocada).</summary>
    public void Text(string text, FontToken font, float x, float y, float w, float h, Color4 color, HAlign align = HAlign.Left, ShadowToken? shadow = null)
    {
        if (text.Length == 0) return;
        var fmt = Format(font, align);
        fixed (char* p = text)
        {
            if (font.Tracking != 0f)
            {
                // Espaçamento entre caracteres exige IDWriteTextLayout1.
                ComPtr<IDWriteTextLayout> layout = default;
                ThrowIfFailed(_gfx.DWriteFactory->CreateTextLayout(p, (uint)text.Length, fmt, w, h, layout.GetAddressOf()));
                using var _l = layout;
                ComPtr<IDWriteTextLayout1> layout1 = default;
                ThrowIfFailed(layout.As(ref layout1));
                using var _l1 = layout1;
                ThrowIfFailed(layout1.Get()->SetCharacterSpacing(0, font.Tracking, 0, new TextRange { startPosition = 0, length = (uint)text.Length }));
                if (shadow is not null)
                    Dc->DrawTextLayout(new Vector2(x + shadow.OffsetX, y + shadow.OffsetY), (IDWriteTextLayout*)layout.Get(), Solid(shadow.Color), DrawTextOptions.None);
                Dc->DrawTextLayout(new Vector2(x, y), (IDWriteTextLayout*)layout.Get(), Solid(color), DrawTextOptions.None);
                return;
            }
            if (shadow is not null)
            {
                var s = new RectF(x + shadow.OffsetX, y + shadow.OffsetY, x + w + shadow.OffsetX, y + h + shadow.OffsetY);
                Dc->DrawText(p, (uint)text.Length, fmt, &s, Solid(shadow.Color), DrawTextOptions.None, MeasuringMode.Natural);
            }
            var r = new RectF(x, y, x + w, y + h);
            Dc->DrawText(p, (uint)text.Length, fmt, &r, Solid(color), DrawTextOptions.None, MeasuringMode.Natural);
        }
    }

    public void Dispose()
    {
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
        _brush.Dispose();
    }
}
