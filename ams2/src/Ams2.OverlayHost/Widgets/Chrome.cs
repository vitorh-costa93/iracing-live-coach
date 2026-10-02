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
            float used = t.Style switch { ThemeStyle.Modern2010s => x + 16 + tw + 16, ThemeStyle.Broadcast2000s => x + tw, _ => x + tw + 10 };
            float k = t.Style == ThemeStyle.Broadcast2000s ? 0.5f : 1f;
            barWidth = Math.Clamp((maxRight - used) / k, 0, barWidth);
            if (barWidth < 12) underline = false;
        }
        switch (t.Style)
        {
            case ThemeStyle.Modern2010s:
                // Barra inclinada vermelha antes do título; sublinhado fino claro até barWidth.
                c.Line(x + 1, y + 24, x + 8, y + 6, t.AccentBar, 3.2f);
                c.Text(title, t.Title, x + 16, y, 260, 30, t.TitleColor);
                if (underline) c.GradientBar(x, y + 31, barWidth + c.Measure(title, t.Title) + 16, t.TitleBarHeight, t.TitleBar);
                break;
            case ThemeStyle.Broadcast2000s:
                Caption(c, x, y + 2, title);
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

    /// <summary>Célula retangular com texto (vocabulário 2004–2008): fundo sólido, texto sem sombra.</summary>
    public static void Cell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, Color4 fill, Color4 ink, HAlign align = HAlign.Left, float padX = 8)
    {
        c.FillRect(x, y, w, h, fill);
        c.Text(text, font, x + padX, y - 1, w - 2 * padX, h, ink, align);
    }

    /// <summary>Célula branca com texto escuro (nomes, rótulos).</summary>
    public static void WhiteCell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, HAlign align = HAlign.Left, Color4? ink = null)
        => Cell(c, x, y, w, h, text, font, c.Theme.NameCellFill, ink ?? c.Theme.NameCellInk, align);

    /// <summary>Célula preta com texto branco (valores, gaps).</summary>
    public static void BlackCell(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, HAlign align = HAlign.Right, Color4? ink = null)
        => Cell(c, x, y, w, h, text, font, c.Theme.ValueCellFill, ink ?? c.Theme.ValueColor, align);

    /// <summary>Legenda pequena em caixa branca ("30/56", títulos de widget). Devolve a largura.</summary>
    public static float Caption(ThemeCanvas c, float x, float y, string text, float h = 26, FontToken? font = null)
    {
        var f = font ?? c.Theme.Label;
        float w = c.Measure(text, f) + 18;
        Cell(c, x, y, w, h, text, f, c.Theme.NameCellFill, c.Theme.NameCellInk, HAlign.Center, 9);
        return w;
    }

    /// <summary>Mensagem de espera ("NO DATA"): legenda em caixa branca no 2004–2008, texto simples nos outros.</summary>
    public static void Notice(ThemeCanvas c, string text, float x, float y, float w = 360)
    {
        var t = c.Theme;
        if (t.Style == ThemeStyle.Broadcast2000s) Caption(c, x, y + 2, text);
        else c.Text(text, t.Label, x, y, w, 30, t.LabelColor, shadow: t.TextShadow);
    }

    /// <summary>Caixa de destaque do tema (posição, marcha): retângulo reto no 1998/2004, cantos suaves no 2010s.</summary>
    public static void AccentBox(ThemeCanvas c, float x, float y, float w, float h, string text, FontToken font, Color4? fill = null)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, t.BoxRadius, fill ?? t.AccentFill);
        c.Text(text, font, x, y - 1, w, h, t.AccentInk, HAlign.Center);
    }

    /// <summary>Célula clara atrás de um nome (2004–2008); sem efeito nos outros temas. Devolve a cor de tinta do nome.</summary>
    public static Color4 NameCell(ThemeCanvas c, float x, float y, float w, float h, Color4 normalInk)
    {
        var t = c.Theme;
        if (t.NameCellFill.A <= 0f) return normalInk;
        c.FillRect(x, y, w, h, t.NameCellFill);
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
