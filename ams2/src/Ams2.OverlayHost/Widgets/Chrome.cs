using Ams2.OverlayHost.Theme;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Peças de cabeçalho compartilhadas pelos widgets: título em caixa alta + barra dourada do tema.</summary>
public static class Chrome
{
    public static void Header(ThemeCanvas c, string title, float x, float y, float barWidth = 160, bool underline = true, float maxRight = float.MaxValue)
    {
        var t = c.Theme;
        // maxRight: borda direita util do painel; encurta a barra do tema para widgets que ficam estreitos ao ocultar colunas.
        if (maxRight < float.MaxValue)
        {
            float tw = c.Measure(title, t.Title);
            float used = t.Style switch { ThemeStyle.Modern2018 => x + tw, ThemeStyle.Broadcast2000s => x + tw, _ => x + tw + 10 };
            float k = t.Style == ThemeStyle.Broadcast2000s ? 0.5f : 1f;
            barWidth = Math.Clamp((maxRight - used) / k, 0, barWidth);
            if (barWidth < 12) underline = false;
        }
        switch (t.Style)
        {
            case ThemeStyle.Modern2018:
                // Título branco em caixa alta com tracking e filete vermelho F1 logo abaixo (como "PIT STOPS" / "GAINED/LOST" da torre).
                c.Text(title, t.Title, x, y, 300, 30, t.TitleColor);
                if (underline) c.FillRect(x, y + 31, barWidth + c.Measure(title, t.Title) + t.Title.Tracking * title.Length, t.TitleBarHeight, t.AccentBar);
                break;
            case ThemeStyle.Broadcast2000s:
                Caption(c, x, y + 2, title, kind: CellKind.Navy);
                break;
            default:
            {
                c.Text(title, t.Title, x, y, 260, 30, t.TitleColor, shadow: t.TextShadow);
                float w = c.Measure(title, t.Title);
                if (barWidth >= 12) c.GradientBar(x + w + 10, y + 3, barWidth, t.TitleBarHeight, t.TitleBar);
                break;
            }
        }
    }

    /// <summary>Tipos de célula do vocabulário 2004–2008 (cores amostradas das capturas de 2005).</summary>
    public enum CellKind { White, Black, Red, Navy, Green, Orange, Blue }

    static Color4 C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    static BarStop[] Stops(CellKind k) => k switch
    {
        // Branco até ~75% e depois lavanda (referência: 255 -> 190,188,210 na base).
        CellKind.White => [new(0f, C(255, 255, 255)), new(0.72f, C(250, 249, 255)), new(1f, C(190, 188, 212))],
        CellKind.Black => [new(0f, C(34, 34, 37)), new(0.22f, C(6, 6, 8)), new(1f, C(0, 0, 0))],
        CellKind.Red => [new(0f, C(222, 44, 28)), new(0.18f, C(200, 20, 4)), new(0.7f, C(196, 18, 0)), new(1f, C(140, 12, 2))],
        CellKind.Navy => [new(0f, C(78, 76, 102)), new(0.2f, C(66, 64, 88)), new(0.7f, C(60, 58, 82)), new(1f, C(40, 38, 63))],
        CellKind.Green => [new(0f, C(16, 140, 34)), new(0.2f, C(1, 118, 13)), new(1f, C(0, 96, 10))],
        CellKind.Blue => [new(0f, C(40, 110, 200)), new(0.2f, C(22, 88, 178)), new(1f, C(12, 56, 128))],
        _ => [new(0f, C(232, 146, 22)), new(0.2f, C(217, 130, 11)), new(1f, C(184, 104, 6))],
    };

    static Color4 DefaultInk(ThemeCanvas c, CellKind k) => k == CellKind.White ? c.Theme.NameCellInk : c.Theme.ValueColor;

    /// <summary>Célula com gradiente vertical sutil, canto reto e filete escuro embaixo (separa as linhas empilhadas).</summary>
    public static void Box(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, CellKind kind, HAlign align = HAlign.Left, float padX = 8, Color4? ink = null)
    {
        c.FillRect(x, y + h, w, 1.5f, new Color4(0.04f, 0.04f, 0.08f, 0.6f));
        c.VGradientRect(x, y, w, h, Stops(kind));
        c.Text(text, font, x + padX, y - 1, w - 2 * padX, h, ink ?? DefaultInk(c, kind), align);
    }

    /// <summary>Célula retangular com texto (vocabulário 2004–2008): fundo sólido, texto sem sombra.</summary>
    public static void Cell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, Color4 fill, Color4 ink, HAlign align = HAlign.Left, float padX = 8)
    {
        c.FillRect(x, y, w, h, fill);
        c.Text(text, font, x + padX, y - 1, w - 2 * padX, h, ink, align);
    }

    /// <summary>Célula branca com texto escuro (nomes, rótulos).</summary>
    public static void WhiteCell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, HAlign align = HAlign.Left, Color4? ink = null)
        => Box(c, x, y, w, h, text, font, CellKind.White, align, 8, ink);

    /// <summary>Célula preta com texto branco (valores, gaps); <paramref name="kind"/> troca para verde/laranja nos deltas.</summary>
    public static void BlackCell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, HAlign align = HAlign.Right, Color4? ink = null, CellKind kind = CellKind.Black)
        => Box(c, x, y, w, h, text, font, kind, align, align == HAlign.Right ? 5 : 8, ink);

    /// <summary>Caixa de posição: vermelha só para o líder, azul-ardósia para os demais.</summary>
    public static void PositionBox(ThemeCanvas c, float x, float y, float w, float h, int position, FontToken font)
        => Box(c, x, y, w, h, position.ToString(System.Globalization.CultureInfo.InvariantCulture), font, position == 1 ? CellKind.Red : CellKind.Navy, HAlign.Center, 0);

    /// <summary>Caixinha do fornecedor de pneus como na transmissao: azul "M" (Michelin) ou vermelha "B" (Bridgestone). Desconhecido: nao desenha. Devolve se desenhou.</summary>
    public static bool TyreBox(ThemeCanvas c, float x, float y, float w, float h, string supplier, FontToken font)
    {
        if (supplier is not ("M" or "B")) return false;
        Box(c, x, y, w, h, supplier, font, supplier == "M" ? CellKind.Blue : CellKind.Red, HAlign.Center, 0);
        return true;
    }

    /// <summary>Bandeira quadriculada (2 linhas de quadrados, comecando no preto no canto de cima-esquerdo).</summary>
    public static void Checkered(ThemeCanvas c, float x, float y, float w, float h)
    {
        float sq = h / 2;
        int n = (int)Math.Ceiling(w / sq);
        c.FillRect(x, y, w, h, C(250, 250, 252));
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < n; i++)
            {
                if ((i + row) % 2 != 0) continue;
                float sw = Math.Min(sq, w - i * sq);
                c.FillRect(x + i * sq, y + row * sq, sw, sq, C(12, 12, 14));
            }
    }

    /// <summary>Cabecalho branco com o nome do widget em teal (no lugar do logotipo do patrocinador do video).</summary>
    public static void HeaderCell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font)
        => Box(c, x, y, w, h, text, font, CellKind.White, HAlign.Center, 4, HeaderTeal);

    /// <summary>Teal do nome do widget no cabeçalho branco (no lugar do logotipo do patrocinador do vídeo).</summary>
    public static Color4 HeaderTeal => C(0, 154, 166);

    /// <summary>Tinta do nome do jogador (vermelho escuro sobre a célula branca).</summary>
    public static Color4 PlayerInk => C(176, 12, 24);

    /// <summary>Legenda pequena em caixa ("30/56", títulos de widget). Branca por padrão, azul-ardósia com texto branco para cabeçalhos. Devolve a largura.</summary>
    public static float Caption(ThemeCanvas c, float x, float y, string text, float h = 26, FontToken? font = null, CellKind kind = CellKind.White, Color4? ink = null)
    {
        var f = font ?? c.Theme.Label;
        float w = c.Measure(text, f) + 18;
        Box(c, x, y, w, h, text, f, kind, HAlign.Center, 9, ink);
        return w;
    }

    /// <summary>Mensagem de espera ("NO DATA"): legenda em caixa branca no 2004–2008, texto simples nos outros.</summary>
    public static void Notice(ThemeCanvas c, string text, float x, float y, float w = 360)
    {
        var t = c.Theme;
        if (t.Style == ThemeStyle.Broadcast2000s) Caption(c, x, y + 2, text, kind: CellKind.Navy);
        else c.Text(text, t.Label, x, y, w, 30, t.LabelColor, shadow: t.TextShadow);
    }

    // ---- Vocabulário 1998–2001 (GP do Brasil 2003): bolha ciana, emblema circular do fornecedor de pneus, selo preto, barra de tempo dividido ----

    /// <summary>Bolha ciana arredondada (pílula) com o número do carro; tinta escura.</summary>
    public static void Bubble(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, h / 2, t.BadgeFill);
        c.Text(text, font, x, y - 1, w, h, t.BadgeInk, HAlign.Center);
    }

    /// <summary>Emblema circular do fornecedor de pneus: disco azul "M" ou vermelho "B" num círculo branco. Desconhecido: não desenha. Devolve se desenhou.</summary>
    public static bool TyreEmblem(ThemeCanvas c, float cx, float cy, float r, string supplier, FontToken font)
    {
        if (supplier is not ("M" or "B")) return false;
        c.FillEllipse(cx, cy, r, r, C(250, 250, 252));
        c.FillEllipse(cx, cy, r - 2.5f, r - 2.5f, supplier == "M" ? C(24, 74, 184) : C(204, 24, 32));
        c.Text(supplier, font, cx - r, cy - r - 1, 2 * r, 2 * r, C(255, 255, 255), HAlign.Center);
        return true;
    }

    /// <summary>Selo preto de canto ("TIMING"), sem marcas.</summary>
    public static void BlackTag(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font)
    {
        c.FillRect(x, y, w, h, new Color4(0.02f, 0.02f, 0.02f, 0.96f));
        c.Text(text, font, x, y - 1, w, h, C(255, 255, 255), HAlign.Center);
    }

    /// <summary>Barra fina de degradê preto/amarelo/branco do tempo dividido; <paramref name="mirror"/> inverte (branco à esquerda).</summary>
    public static void SplitBar(ThemeCanvas c, float x, float y, float w, float h, bool mirror)
    {
        var stops = c.Theme.TitleBar;
        if (mirror) stops = stops.Reverse().Select(s => new BarStop(1f - s.Position, s.Color)).ToArray();
        c.GradientBar(x, y, w, h, stops);
    }

    /// <summary>Caixa de destaque do tema (posição, marcha): retângulo reto no 1998/2004, cantos suaves no 2010s.</summary>
    public static void AccentBox(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, Color4? fill = null)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, t.BoxRadius, fill ?? t.AccentFill);
        // 2018: número preto em negrito na caixa branca.
        if (t.Style == ThemeStyle.Modern2018) font = font with { Weight = Math.Max(font.Weight, 700) };
        c.Text(text, font, x, y - 1, w, h, t.AccentInk, HAlign.Center);
    }

    // ---- Vocabulário 2018–2021 (ref. f1-2018-analysis.md) ----

    /// <summary>Caixa de posição 2018: quadrado branco arredondado com número preto em negrito; <paramref name="fill"/> troca a cor (verde/vermelho de ganho/perda).</summary>
    public static void PosBox(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, Color4? fill = null)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, t.BoxRadius, fill ?? t.AccentFill);
        c.Text(text, font with { Weight = Math.Max(font.Weight, 700) }, x, y, w, h, fill is null ? t.AccentInk : new Color4(1, 1, 1, 1), HAlign.Center);
    }

    /// <summary>Tique vertical (cor da equipe na TV; aqui cor neutra/da classe, o AMS2 não informa a cor da equipe).</summary>
    public static void Tick(ThemeCanvas c, float x, float y, float h, Color4 color, float w = 4) => c.FillRect(x, y, w, h, color);

    /// <summary>Cor neutra do tique da classe: tons fixos por índice de classe (sem cores de equipe, que o AMS2 não expõe).</summary>
    public static Color4 ClassTick(int classIndex) => (classIndex % 4) switch
    {
        0 => C(225, 6, 0),
        1 => C(40, 120, 220),
        2 => C(30, 170, 90),
        _ => C(230, 150, 20),
    };

    /// <summary>Marcador de melhor volta: quadrado roxo com um cronômetro branco desenhado (sem glifo).</summary>
    public static void FastestMarker(ThemeCanvas c, float x, float y, float s)
    {
        var t = c.Theme;
        c.FillRect(x, y, s, s, t.FastestFill);
        float cx = x + s / 2, cy = y + s / 2 + s * 0.06f, r = s * 0.28f;
        var w = new Color4(1, 1, 1, 1);
        c.StrokeEllipse(cx, cy, r, r, w, Math.Max(1.6f, s * 0.07f));
        c.FillRect(cx - s * 0.08f, y + s * 0.12f, s * 0.16f, s * 0.08f, w);           // botão
        c.Line(cx, cy, cx + r * 0.55f, cy - r * 0.55f, w, Math.Max(1.4f, s * 0.06f)); // ponteiro
    }

    /// <summary>Colchetes de canto ciano em volta do tempo de parada (gráfico PIT LANE).</summary>
    public static void CornerBrackets(ThemeCanvas c, float x, float y, float w, float h, Color4 color, float len = 10, float th = 2.5f)
    {
        c.FillRect(x, y, len, th, color); c.FillRect(x, y, th, len, color);
        c.FillRect(x + w - len, y, len, th, color); c.FillRect(x + w - th, y, th, len, color);
        c.FillRect(x, y + h - th, len, th, color); c.FillRect(x, y + h - len, th, len, color);
        c.FillRect(x + w - len, y + h - th, len, th, color); c.FillRect(x + w - th, y + h - len, th, len, color);
    }

    /// <summary>"Nome SOBRENOME" em dois pesos (nome regular, sobrenome negrito em caixa alta), como na legenda 2018. Devolve a largura usada.</summary>
    public static float TwoWeightName(ThemeCanvas c, string fullName, FontToken bold, float x, float y, float h, float maxW, Color4 color)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string first = parts.Length > 1 ? string.Join(' ', parts[..^1]) + " " : "";
        string last = (parts.Length > 0 ? parts[^1] : fullName).ToUpperInvariant();
        var reg = bold with { Weight = 400 };
        // Reduz os dois juntos até caber.
        while (bold.Size > 14 && c.Measure(first, reg) + c.Measure(last, bold) > maxW) { bold = bold with { Size = bold.Size - 1 }; reg = reg with { Size = bold.Size }; }
        float fw = c.Measure(first, reg);
        if (first.Length > 0) c.Text(first, reg, x, y, fw + 6, h, color);
        float lw = c.Measure(last, bold);
        c.Text(last, bold, x + fw, y, lw + 6, h, color);
        return fw + lw;
    }

    /// <summary>Célula clara atrás de um nome (2004–2008); sem efeito nos outros temas. Devolve a cor de tinta do nome.</summary>
    public static Color4 NameCell(ThemeCanvas c, float x, float y, float w, float h, Color4 normalInk)
    {
        var t = c.Theme;
        if (t.NameCellFill.A <= 0f) return normalInk;
        c.VGradientRect(x, y, w, h, Stops(CellKind.White));
        return t.NameCellInk;
    }

    /// <summary>
    /// Valor numérico (família de números do tema) seguido de unidade (família de texto, menor), alinhados juntos.
    /// A fonte de números do tema não tem letras nem símbolos como "°", por isso a unidade vai em outra fonte.
    /// </summary>
    public static void ValueUnit(ThemeCanvas c, string value, string unit, float x, float y, float h, Color4 color, bool alignRight, FontToken? unitFont = null)
    {
        var t = c.Theme;
        var uf = unitFont ?? t.Label;
        float vw = c.Measure(value, t.Numbers) + t.Numbers.Tracking * value.Length, uw = unit.Length > 0 ? c.Measure(unit, uf) + 6 : 0;
        float left = alignRight ? x - vw - uw : x;
        c.Text(value, t.Numbers, left, y, vw + 4, h, color, shadow: t.ValueShadow);
        if (unit.Length > 0) c.Text(unit, uf, left + vw + 6, y, uw + 4, h, color, shadow: t.TextShadow);
    }
}
