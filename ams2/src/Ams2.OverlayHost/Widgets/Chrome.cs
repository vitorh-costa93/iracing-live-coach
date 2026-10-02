using Ams2.OverlayHost.Theme;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Peças de cabeçalho compartilhadas pelos widgets: título em caixa alta + barra dourada do tema.</summary>
public static class Chrome
{
    public static void Header(ThemeCanvas c, string title, float x, float y, float barWidth = 160)
    {
        var t = c.Theme;
        c.Text(title, t.Title, x, y, 260, 30, t.TitleColor, shadow: t.TextShadow);
        float w = c.Measure(title, t.Title);
        c.GradientBar(x + w + 10, y + 3, barWidth, 15, t.TitleBar);
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
