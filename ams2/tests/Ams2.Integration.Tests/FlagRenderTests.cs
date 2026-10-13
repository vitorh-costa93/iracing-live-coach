using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class FlagRenderTests
{
    static byte[] Render(IWidget w, OverlayModel m, Theme theme)
    {
        var (dw, dh) = w.DesignSize;
        using var gfx = DeviceResources.CreateOffscreen((int)Math.Ceiling(dw), (int)Math.Ceiling(dh));
        using var canvas = new ThemeCanvas(gfx, theme);
        gfx.BeginFrame(); canvas.Begin(); w.Draw(canvas, m); canvas.End(); gfx.EndFrame();
        return gfx.ReadPixelsBgra();
    }

    static OverlayModel WithCountry(OverlayModel m, string country) => m with
    {
        Session = m.Session! with { Cars = m.Session.Cars.Select(c => c with { Country = country }).ToList() },
        Standings = m.Standings.Select(r => r with { Car = r.Car with { Country = country } }).ToList(),
        Relative = m.Relative.Select(r => r with { Car = r.Car with { Country = country } }).ToList(),
    };

    [Theory]
    [InlineData("standings")]
    [InlineData("relative")]
    [InlineData("board")]
    public void Flag_never_appears_in_tables(string id)
    {
        var theme = Themes.F1_2004;
        var model = LayoutPreview.ForWidget(id);
        var w = WidgetRegistry.Create(id); w.UseTheme(theme); w.Configure(new WidgetSettings { Id = id });
        Assert.Equal(Render(w, WithCountry(model, ""), theme), Render(w, WithCountry(model, "ITA"), theme));
    }

    [Fact]
    public void Flag_appears_in_the_2018_position_caption_with_the_preview_and_the_live_fake_model()
    {
        var theme = Themes.F1_2018;
        var w = WidgetRegistry.Create("poscaption"); w.UseTheme(theme); w.Configure(LayoutPreview.Settings(new WidgetSettings { Id = "poscaption" }));
        var preview = LayoutPreview.ForWidget("poscaption");
        Assert.NotEqual(Render(w, WithCountry(preview, ""), theme), Render(w, preview, theme));

        // --fake com o jogador "pilotando" ignora a previa de layout (WidgetWindow.Render): o HostController aplica os paises de exemplo ao modelo vivo.
        double now = 0;
        using var provider = new OverlayDataProvider(new FakeRawSource(() => now), () => now);
        for (int i = 0; i < 1200; i++) { now += 1.0 / 60; provider.Tick(); }
        var live = provider.Current;
        Assert.True(live.PlayerDriving);
        Assert.All(live.Session!.Cars, c => Assert.Empty(c.Country));
        var withCountries = LayoutPreview.WithCountries(live);
        Assert.All(withCountries.Session!.Cars, c => Assert.NotEmpty(c.Country));
        Assert.NotEqual(Render(w, live, theme), Render(w, withCountries, theme));
    }

    [Fact]
    public void Flag_appears_in_the_2004_driver_caption_only_when_the_country_is_known()
    {
        var theme = Themes.F1_2004;
        var model = LayoutPreview.ForWidget("drivercaption");
        var w = WidgetRegistry.Create("drivercaption"); w.UseTheme(theme); w.Configure(new WidgetSettings { Id = "drivercaption", Options = new() { ["layoutPreview"] = "true" } });
        var none = Render(w, WithCountry(model, ""), theme);
        Assert.NotEqual(none, Render(w, WithCountry(model, "ITA"), theme));
        Assert.Equal(none, Render(w, WithCountry(model, "XXX"), theme)); // pais sem PNG = sem bandeira
    }
}
