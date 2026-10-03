using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

/// <summary>Composicao padrao de cada tema: nenhum widget pode se sobrepor nem sair da tela 1920x1080.</summary>
public class LayoutTests
{
    public static IEnumerable<object[]> Themes => ThemeCatalog.All.Select(t => new object[] { t.Id });

    static List<(string Id, double X, double Y, double W, double H)> Rects(string theme)
    {
        var p = ProfileFactory.CreateDefault("Padrão", theme, WidgetLayout.RefWidth, WidgetLayout.RefHeight);
        var list = new List<(string, double, double, double, double)>();
        foreach (var w in p.Widgets.Where(w => w.Visible))
        {
            Assert.True(WidgetLayout.DesignSizes[theme].TryGetValue(w.Id, out var sz), $"{theme}/{w.Id}: sem tamanho de projeto");
            list.Add((w.Id, w.X, w.Y, Math.Ceiling(sz.W * w.Scale), Math.Ceiling(sz.H * w.Scale)));
        }
        return list;
    }

    static bool Exclusive(string a, string b) => WidgetLayout.ExclusiveGroups.Any(g => g.Contains(a) && g.Contains(b));

    [Theory, MemberData(nameof(Themes))]
    public void Every_widget_has_a_design_size(string theme)
        => Assert.Equal(WidgetCatalog.All.Select(w => w.Id).Order(), WidgetLayout.DesignSizes[theme].Keys.Order());

    [Theory, MemberData(nameof(Themes))]
    public void Default_layout_stays_inside_the_screen(string theme)
    {
        foreach (var (id, x, y, w, h) in Rects(theme))
        {
            Assert.True(x >= 0 && y >= 0, $"{theme}/{id} comeca fora da tela ({x},{y})");
            Assert.True(x + w <= WidgetLayout.RefWidth && y + h <= WidgetLayout.RefHeight, $"{theme}/{id} passa da tela 1920x1080: ({x},{y}) {w}x{h}");
        }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Default_layout_has_no_overlapping_widgets(string theme)
    {
        var r = Rects(theme);
        for (int i = 0; i < r.Count; i++)
            for (int j = i + 1; j < r.Count; j++)
            {
                var (ai, ax, ay, aw, ah) = r[i]; var (bi, bx, by, bw, bh) = r[j];
                if (Exclusive(ai, bi)) continue;
                bool overlap = ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
                Assert.False(overlap, $"{theme}: {ai} ({ax},{ay} {aw}x{ah}) sobrepoe {bi} ({bx},{by} {bw}x{bh})");
            }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Layout_scales_with_the_screen(string theme)
    {
        var a = ProfileFactory.CreateDefault("A", theme, 1920, 1080);
        var b = ProfileFactory.CreateDefault("A", theme, 3840, 2160);
        foreach (var w in a.Widgets)
        {
            Assert.Equal(w.X * 2, b.Get(w.Id)!.X);
            Assert.Equal(w.Y * 2, b.Get(w.Id)!.Y);
            Assert.InRange(b.Get(w.Id)!.Scale, w.Scale * 2 - 0.01f, w.Scale * 2 + 0.01f);
        }
    }
}
