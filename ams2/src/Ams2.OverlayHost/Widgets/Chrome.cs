using Ams2.OverlayHost.Theme;

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
}
