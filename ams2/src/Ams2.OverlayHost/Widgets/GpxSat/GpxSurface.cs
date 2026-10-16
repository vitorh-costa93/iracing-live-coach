using System.Numerics;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;
using TextAntialiasMode = Vortice.Win32.Graphics.Direct2D.TextAntialiasMode;
using FigureBegin = Vortice.Win32.Graphics.Direct2D.Common.FigureBegin;
using FigureEnd = Vortice.Win32.Graphics.Direct2D.Common.FigureEnd;
using D2DGradientStop = Vortice.Win32.Graphics.Direct2D.Common.GradientStop;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>
/// Superficie de desenho do GPxSAT: mapeia o espaco logico 1024x768 na area do widget (pillarbox 4:3 ou esticado), desenha as caixas
/// com as texturas procedurais e o texto direto em pixels do device. Nitidez do texto: sem transformacao (tamanho real em px),
/// baseline e bordas das caixas em pixel inteiro, antialias em tons de cinza com parametros proprios (alvo com alfa nao aceita ClearType).
/// Recursos (pincel, gradientes, formatos, layouts) em cache, refeitos quando o device muda.
/// </summary>
public sealed unsafe class GpxSurface : IDisposable
{
    public const string FallbackFamily = "Barlow Semi Condensed";
    const int LayoutCacheLimit = 384;

    DeviceResources? _gfx;
    GpxInstrumentBitmaps? _artwork;
    int _generation = -1;
    ComPtr<ID2D1SolidColorBrush> _solid;
    readonly Dictionary<string, ComPtr<ID2D1LinearGradientBrush>> _gradients = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Family, int Weight, int Size4, GpxAlign Align), ComPtr<IDWriteTextFormat>> _formats = [];
    readonly Dictionary<(string Text, IntPtr Format, int W, int H), ComPtr<IDWriteTextLayout>> _layouts = [];
    readonly Queue<(string, IntPtr, int, int)> _layoutOrder = [];
    ComPtr<IDWriteRenderingParams> _textParams;

    ID2D1DeviceContext* _dc;
    float _sx = 1, _sy = 1, _ox, _oy, _opacity = 1;
    GpxModeSpec _spec = null!;
    bool _originalFonts = true;

    public GpxGeometry Geometry { get; private set; } = new(17);
    /// <summary>Altura de linha em px do device.</summary>
    public float FhPx => Geometry.FH * _sy;
    /// <summary>Familia efetivamente usada por papel no ultimo quadro (diagnostico/preview).</summary>
    public string? LastFamily { get; private set; }

    void EnsureDevice(DeviceResources gfx)
    {
        if (ReferenceEquals(_gfx, gfx) && _generation == gfx.Generation) return;
        ReleaseResources();
        _gfx = gfx; _generation = gfx.Generation;
        _artwork = new GpxInstrumentBitmaps(gfx, Path.Combine(AppContext.BaseDirectory, "Assets", "GpxPatch"));
        var c = new Color4(1, 1, 1, 1);
        ComPtr<ID2D1SolidColorBrush> b = default;
        ThrowIfFailed(gfx.Context->CreateSolidColorBrush(&c, null, b.GetAddressOf()));
        _solid = b;
        ComPtr<IDWriteRenderingParams> p = default;
        // Gamma 1.8 e contraste moderado: hastes finas legiveis no fundo transparente; ClearType 0 (cinza); simetrico natural
        // (sem o serrilhado do modo aliased/GDI classico nas diagonais).
        if (gfx.DWriteFactory->CreateCustomRenderingParams(1.8f, 0.5f, 0f, PixelGeometry.Flat, RenderingMode.NaturalSymmetric, p.GetAddressOf()).Success)
            _textParams = p;
    }

    void ReleaseResources()
    {
        _artwork?.Dispose(); _artwork = null;
        foreach (var l in _layouts.Values) l.Dispose();
        _layouts.Clear(); _layoutOrder.Clear();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear(); _cellToEm.Clear();
        foreach (var g in _gradients.Values) g.Dispose();
        _gradients.Clear();
        _solid.Dispose(); _solid = default;
        _textParams.Dispose(); _textParams = default;
    }

    ComPtr<IDWriteRenderingParams> _savedParams;
    TextAntialiasMode _savedAa;

    /// <summary>Inicia o desenho: area do widget em px do device (tamanho de projeto x escala do canvas), mapeamento do espaco logico.</summary>
    public void Begin(ThemeCanvas canvas, float designW, float designH, GpxModeSpec spec, bool stretch, bool originalFonts, float textScale = 1f)
    {
        _textScale = float.IsFinite(textScale) && textScale > 0 ? textScale : 1f;
        var gfx = canvas.Resources;
        EnsureDevice(gfx);
        _dc = canvas.DeviceContext;
        _spec = spec; _originalFonts = originalFonts; _opacity = canvas.Opacity;
        Geometry = new GpxGeometry(spec.FH);
        float devW = designW * canvas.Scale * canvas.WidthScale, devH = designH * canvas.Scale * canvas.HeightScale;
        (_sx, _sy, _ox, _oy) = GpxGeometry.Fit(devW, devH, stretch);
        var id = Matrix3x2.Identity;
        _dc->SetTransform(&id);
        _savedAa = _dc->GetTextAntialiasMode();
        ComPtr<IDWriteRenderingParams> saved = default;
        _dc->GetTextRenderingParams(saved.GetAddressOf());
        _savedParams = saved;
        _dc->SetTextAntialiasMode(TextAntialiasMode.Grayscale);
        if (_textParams.Get() != null) _dc->SetTextRenderingParams(_textParams.Get());
    }

    /// <summary>Restaura transformacao do canvas e parametros de texto do contexto.</summary>
    public void End(ThemeCanvas canvas)
    {
        if (_dc == null) return;
        _dc->SetTextAntialiasMode(_savedAa);
        _dc->SetTextRenderingParams(_savedParams.Get());
        _savedParams.Dispose(); _savedParams = default;
        var m = Matrix3x2.CreateScale(canvas.Scale * canvas.WidthScale, canvas.Scale * canvas.HeightScale);
        _dc->SetTransform(&m);
        _dc = null;
    }

    /// <summary>Retangulo logico em px do device, com as bordas em pixel inteiro (caixas vizinhas compartilham a borda exata).</summary>
    public RectF ToDevice(GpxRect r)
    {
        float l = MathF.Round(_ox + r.L * _sx), t = MathF.Round(_oy + r.T * _sy);
        float rr = MathF.Round(_ox + r.R * _sx), b = MathF.Round(_oy + r.B * _sy);
        return new RectF(l, t, Math.Max(l, rr), Math.Max(t, b));
    }

    static Color4 Argb(uint c, float alpha = 1f)
        => new(((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f, ((c >> 24) & 0xff) / 255f * alpha);

    ID2D1Brush* Solid(uint argb, float alpha = 1f)
    {
        var c = Argb(argb, alpha * _opacity);
        _solid.Get()->SetColor(&c);
        return (ID2D1Brush*)_solid.Get();
    }

    ID2D1LinearGradientBrush* Gradient(string texture)
    {
        if (_gradients.TryGetValue(texture, out var cached)) return cached.Get();
        var stops = GpxTextures.Get(texture);
        var d2d = new D2DGradientStop[stops.Length];
        for (int i = 0; i < stops.Length; i++) d2d[i] = new D2DGradientStop { position = stops[i].Position, color = Argb(stops[i].Argb) };
        ComPtr<ID2D1GradientStopCollection> coll = default;
        fixed (D2DGradientStop* p = d2d)
            ThrowIfFailed(_dc->CreateGradientStopCollection(p, (uint)d2d.Length, Gamma.Gamma_2_2, ExtendMode.Clamp, coll.GetAddressOf()));
        using var _c = coll;
        var props = new LinearGradientBrushProperties { startPoint = new Vector2(0, 0), endPoint = new Vector2(0, 1) };
        ComPtr<ID2D1LinearGradientBrush> brush = default;
        ThrowIfFailed(_dc->CreateLinearGradientBrush(&props, null, coll.Get(), brush.GetAddressOf()));
        _gradients[texture] = brush;
        return brush.Get();
    }

    /// <summary>Preenche o retangulo (px do device) com a textura esticada na vertical (gradiente de cima para baixo da caixa).</summary>
    public void FillTexture(RectF r, string texture, float alpha = 1f, bool horizontal = false, float slant = 0)
    {
        if (r.Right <= r.Left || r.Bottom <= r.Top || alpha <= 0) return;
        var g = Gradient(texture);
        g->SetStartPoint(horizontal ? new Vector2(r.Left, 0) : new Vector2(0, r.Top));
        g->SetEndPoint(horizontal ? new Vector2(r.Right, 0) : new Vector2(0, r.Bottom));
        g->SetOpacity(Math.Clamp(alpha, 0, 1) * _opacity);
        if (slant == 0) _dc->FillRectangle(&r, (ID2D1Brush*)g);
        else
        {
            using ComPtr<ID2D1Factory> factory = default;
            _dc->GetFactory(factory.GetAddressOf());
            using ComPtr<ID2D1PathGeometry> path = default;
            ThrowIfFailed(factory.Get()->CreatePathGeometry(path.GetAddressOf()));
            using ComPtr<ID2D1GeometrySink> sink = default;
            ThrowIfFailed(path.Get()->Open(sink.GetAddressOf()));
            float dx = slant * _sx;
            sink.Get()->BeginFigure(new Vector2(r.Left + dx, r.Top), FigureBegin.Filled);
            sink.Get()->AddLine(new Vector2(r.Right, r.Top));
            sink.Get()->AddLine(new Vector2(r.Right - dx, r.Bottom));
            sink.Get()->AddLine(new Vector2(r.Left, r.Bottom));
            sink.Get()->EndFigure(FigureEnd.Closed);
            ThrowIfFailed(sink.Get()->Close());
            _dc->FillGeometry((ID2D1Geometry*)path.Get(), (ID2D1Brush*)g, null);
        }
    }

    public void Stroke(RectF r, uint argb) => _dc->DrawRectangle(&r, Solid(argb), Math.Max(1, _sy), null);

    public void FillSolid(RectF r, uint argb, float alpha = 1f)
    {
        if (r.Right <= r.Left || r.Bottom <= r.Top) return;
        _dc->FillRectangle(&r, Solid(argb, alpha));
    }

    /// <summary>
    /// Fonte da funcao: primeiro a original do GPxPatch (fonts.ini do modo, carregada de fonts\gpxpatch ou da instalacao do GP3), depois a do
    /// sistema com o mesmo nome; Barlow so quando a original nao existe. <c>Original</c> = tamanho em altura de celula GDI (Height do fonts.ini).
    /// </summary>
    (string Family, bool Custom, bool Original, int Weight, float SizeFh) Resolve(GpxFontRole role)
    {
        var spec = _spec.For(role);
        var gfx = _gfx!;
        if (_originalFonts)
            foreach (var f in spec.Families)
            {
                if (gfx.Fonts.Has(f)) return (f, true, true, spec.Weight, spec.SizeFh);
                if (gfx.Fonts.SystemHas((IDWriteFactory*)gfx.DWriteFactory, f)) return (f, false, true, spec.Weight, spec.SizeFh);
            }
        foreach (string fallback in new[] { FallbackFamily, "Barlow" })
            if (gfx.Fonts.Has(fallback)) return (fallback, true, false, spec.FallbackWeight, spec.FallbackSize);
        return ("Segoe UI", false, false, spec.FallbackWeight, spec.FallbackSize);
    }

    readonly Dictionary<(string, int), float> _cellToEm = [];
    float _textScale = 1f;

    /// <summary>Em por altura de celula (ascent + descent) da familia: o GDI do GPx cria a fonte com lfHeight = Height = celula, nao em.</summary>
    float CellToEm(string family, bool custom, int weight)
    {
        if (_cellToEm.TryGetValue((family, weight), out var k)) return k;
        k = 1f;
        var gfx = _gfx!;
        using var fmt = gfx.DWriteFactory->CreateTextFormat(family, custom ? (IDWriteFontCollection*)gfx.Fonts.Collection : null, 100f,
            fontWeight: (FontWeight)weight, fontStyle: FontStyle.Normal, localeName: "en-us");
        using ComPtr<IDWriteTextLayout> layout = default;
        const string probe = "Hg";
        fixed (char* p = probe)
            if (gfx.DWriteFactory->CreateTextLayout(p, (uint)probe.Length, fmt.Get(), 1000, 1000, layout.GetAddressOf()).Success)
            {
                LineMetrics line; uint lines;
                if (layout.Get()->GetLineMetrics(&line, 1, &lines).Success && line.height > 1) k = 100f / line.height;
            }
        _cellToEm[(family, weight)] = k;
        return k;
    }

    IDWriteTextFormat* Format(GpxFontRole role, GpxAlign align, out float sizePx)
    {
        var (family, custom, original, weight, sizeFh) = Resolve(role);
        LastFamily = family;
        // Tamanho em pixel inteiro do device (hinting vertical estavel, sem bordas meio-pixel): nitidez em qualquer escala do 1024x768.
        sizePx = MathF.Max(4, MathF.Round(sizeFh * FhPx * _textScale * (original ? CellToEm(family, custom, weight) : 1f)));
        int size4 = (int)(sizePx * 4);
        var key = (family, weight, size4, align);
        if (_formats.TryGetValue(key, out var cached)) return cached.Get();
        var gfx = _gfx!;
        var fmt = gfx.DWriteFactory->CreateTextFormat(family, custom ? (IDWriteFontCollection*)gfx.Fonts.Collection : null, sizePx,
            fontWeight: (FontWeight)weight, fontStyle: FontStyle.Normal, localeName: "en-us");
        ThrowIfFailed(fmt.Get()->SetParagraphAlignment(ParagraphAlignment.Near));
        ThrowIfFailed(fmt.Get()->SetWordWrapping(WordWrapping.NoWrap));
        ThrowIfFailed(fmt.Get()->SetTextAlignment(align switch { GpxAlign.Center => TextAlignment.Center, GpxAlign.Right => TextAlignment.Trailing, _ => TextAlignment.Leading }));
        _formats[key] = fmt;
        return fmt.Get();
    }

    IDWriteTextLayout* Layout(string text, IDWriteTextFormat* fmt, float w, float h)
    {
        var key = (text, (IntPtr)fmt, (int)MathF.Round(w * 4), (int)MathF.Round(h * 4));
        if (_layouts.TryGetValue(key, out var cached)) return cached.Get();
        ComPtr<IDWriteTextLayout> layout = default;
        fixed (char* p = text)
            ThrowIfFailed(_gfx!.DWriteFactory->CreateTextLayout(p, (uint)text.Length, fmt, Math.Max(1, w), Math.Max(1, h), layout.GetAddressOf()));
        if (_layouts.Count >= LayoutCacheLimit)
        {
            var oldest = _layoutOrder.Dequeue();
            _layouts[oldest].Dispose(); _layouts.Remove(oldest);
        }
        _layouts[key] = layout; _layoutOrder.Enqueue(key);
        return layout.Get();
    }

    /// <summary>
    /// Texto na caixa (px do device): centrado na vertical com a baseline em pixel inteiro; recuo <paramref name="indentLogical"/>
    /// (unidades logicas) somado a esquerda ou subtraido a direita; sem recorte (como o DrawText do GPx).
    /// </summary>
    public void Text(RectF box, string text, GpxAlign align, uint argb, float indentLogical, GpxFontRole role = GpxFontRole.Normal, float alpha = 1f, bool shadow = false, bool topAligned = false)
    {
        if (string.IsNullOrEmpty(text) || box.Right <= box.Left) return;
        var fmt = Format(role, align, out _);
        float indent = MathF.Round(indentLogical * _sx);
        float l = box.Left + (align == GpxAlign.Left ? indent : 0), r = box.Right - (align == GpxAlign.Right ? indent : 0);
        float w = Math.Max(1, r - l), h = box.Bottom - box.Top;
        var layout = Layout(text, fmt, w, h);
        LineMetrics line; uint lines;
        if (layout->GetLineMetrics(&line, 1, &lines).Failure || lines == 0) return;
        float top = topAligned ? box.Top : box.Top + (h - line.height) / 2;
        float baseline = MathF.Round(top + line.baseline);
        if (shadow) _dc->DrawTextLayout(new Vector2(l + Math.Max(1, _sx), baseline - line.baseline + Math.Max(1, _sy)), layout, Solid(0xff000000, alpha * .8f), DrawTextOptions.None);
        _dc->DrawTextLayout(new Vector2(l, baseline - line.baseline), layout, Solid(argb, alpha), DrawTextOptions.None);
    }

    /// <summary>Bandeira quadriculada procedural (no lugar do digital_flag.tga): 4x4 casas preto/branco num quadro escuro.</summary>
    public void CheckeredFlag(RectF r, float alpha = 1f)
    {
        FillTexture(r, GpxTextures.White, alpha);
        float pad = MathF.Max(1, MathF.Round((r.Bottom - r.Top) * 0.12f));
        float l = r.Left + pad, t = r.Top + pad, s = (r.Right - r.Left - 2 * pad) / 4, sv = (r.Bottom - r.Top - 2 * pad) / 4;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if ((x + y) % 2 == 0)
                    FillSolid(new RectF(MathF.Round(l + x * s), MathF.Round(t + y * sv), MathF.Round(l + (x + 1) * s), MathF.Round(t + (y + 1) * sv)), 0xff101010, alpha);
    }

    /// <summary>Selo do fornecedor de pneus (no lugar do digital_2004_m/b.tga): letra preta centrada na caixa branca.</summary>
    public void TyreBadge(RectF r, char letter, float alpha = 1f)
    {
        FillTexture(r, GpxTextures.White, alpha);
        Text(r, letter.ToString(), GpxAlign.Center, GpxInk.Black, 0, GpxFontRole.Normal, alpha);
    }


    public bool Artwork(RectF destination, string? asset, GpxRect? source, float alpha)
    {
        if (asset is null || _artwork is null) return false;
        var bitmap = _artwork.Find(asset);
        if (bitmap == null) return false;
        RectF src = source is { } r ? new RectF(r.L, r.T, r.R, r.B) : default;
        _dc->DrawBitmap(bitmap, &destination, alpha * _opacity, InterpolationMode.HighQualityCubic, source is null ? null : &src, null);
        return true;
    }

    public void TrackMap(GpxTrackMap map, Ams2.Core.SessionSnapshot session)
    {
        const float size=256;
        if (!map.Ready) return;
        foreach(var segment in map.Segments)
        {
            Vector2 Device(Vector2 p) => new(_ox+p.X*_sx, _oy+p.Y*_sy);
            var a=Device(map.Project(segment.A,0,0,size)); var b=Device(map.Project(segment.B,0,0,size));
            _dc->DrawLine(a,b,Solid(0xff202020),6*_sy,null);
            _dc->DrawLine(a,b,Solid(0xffc0c0c0),3*_sy,null);
        }
        foreach(var car in session.Cars.Where(c=>c.HasPose && !c.InGarage).OrderBy(c=>c.IsPlayer))
        {
            var p=map.Project(new((float)car.PosX,(float)car.PosZ),0,0,size);
            var rect=ToDevice(new(p.X-4,p.Y-4,8,8));
            var e=new Ellipse { point=new((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2),radiusX=4*_sx,radiusY=4*_sy };
            _dc->FillEllipse(&e,Solid(car.IsPlayer?0xff00cc00:0xffff0000));
        }
    }

    public void Badge(RectF r, GpxImage image, string? text, uint ink, float alpha)
    {
        bool oval = image == GpxImage.CarNumber;
        if (oval)
        {
            var e = new Ellipse { point = new Vector2((r.Left + r.Right)/2, (r.Top + r.Bottom)/2), radiusX = (r.Right-r.Left)/2, radiusY = (r.Bottom-r.Top)/2 };
            _dc->FillEllipse(&e, Solid(0xff63d9d2, alpha));
        }
        else FillTexture(r, GpxTextures.YellowFlag, alpha);
        Text(r, text ?? (image == GpxImage.Winner ? "WINNER" : ""), GpxAlign.Center, ink, 0,
            image == GpxImage.BigOne ? GpxFontRole.Big : GpxFontRole.Normal, alpha);
    }

    public void Dispose() => ReleaseResources();
}
