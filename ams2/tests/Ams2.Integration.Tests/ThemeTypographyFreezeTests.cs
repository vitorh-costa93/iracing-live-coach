using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;

namespace Ams2.Integration.Tests;

/// <summary>Tipografia: o 2012 usa a DIN-Bold do GPx Digital 2010; a do 1999 (Reddit Sans/F1 Broadcast e Mañana/Futura do GPx) nao muda.</summary>
public class ThemeTypographyFreezeTests
{
    [Fact]
    public void Theme_2012_uses_gpx_din_bold_matching_digital2010_spec()
    {
        foreach (var f in new[] { Themes.F1_2012.Title, Themes.F1_2012.Label, Themes.F1_2012.Text, Themes.F1_2012.Numbers })
        { Assert.Equal("DIN", f.Family); Assert.Equal(700, f.Weight); }
        Assert.Equal("DIN", new Digital2010Mode().Spec.Normal.Families[0]);
    }

    [Fact]
    public void Theme_1999_typography_is_frozen()
    {
        foreach (var t in new[] { Themes.F1_1998, Themes.F1_1999Digital })
        {
            Assert.Equal(new FontToken("Reddit Sans", 800, 30f), t.Title);
            Assert.Equal(new FontToken("Reddit Sans", 800, 25f), t.Label);
            Assert.Equal(new FontToken("Reddit Sans", 800, 30f), t.Text);
            Assert.Equal(new FontToken("F1 Broadcast 98 Values", 400, 32f, 3f), t.Numbers);
        }
        Assert.Equal("Mañana", new ClassicMode().Spec.Normal.Families[0]);
        Assert.Equal("Futura-Condensed-Bold", new DigitalMode().Spec.Normal.Families[0]);
    }
}
