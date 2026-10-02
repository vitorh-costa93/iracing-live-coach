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
            {
                c.Text(title, t.Title, x, y, 260, 30, t.TitleColor, shadow: t.TextShadow);
                float w2 = c.Measure(title, t.Title);
                if (underline) c.GradientBar(x, y + 30, w2 + barWidth * 0.5f, t.TitleBarHeight, t.TitleBar);
                break;
            }
            default:
            {
                c.Text(title, t.Title, x, y, 260, 30, t.TitleColor, shadow: t.TextShadow);
                float w = c.Measure(title, t.Title);
                if (barWidth >= 12) c.GradientBar(x + w + 10, y + 3, barWidth, t.TitleBarHeight, t.TitleBar);
                break;
            }
        }
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
