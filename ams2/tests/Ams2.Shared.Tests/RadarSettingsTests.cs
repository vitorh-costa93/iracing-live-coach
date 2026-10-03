using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public class RadarSettingsTests
{
    [Theory]
    [InlineData("f1-1998")] [InlineData("f1-2004")] [InlineData("f1-2018")]
    public void Radar_is_in_every_default_profile_visible_in_v3_panel_style_at_the_lower_centre(string theme)
    {
        var r = ProfileFactory.CreateDefault("x", theme).Get("radar")!;
        Assert.True(r.Visible);
        Assert.Equal((900, 585, 1f), (r.X, r.Y, r.Scale));
        Assert.Equal((15, 3), (r.EffectiveRadarRange, r.EffectiveRadarSensitivity));
        Assert.Equal([], r.Columns);                                   // "Sempre visivel" desligado: so com carro proximo
        Assert.Null(ProfileFactory.CreateDefault("x", theme).Get("fuel")!.RadarRange);
    }

    [Fact]
    public void Radar_range_and_sensitivity_are_clamped_and_only_kept_on_the_radar()
    {
        var r = new WidgetSettings { Id = "radar", RadarRange = 999, RadarSensitivity = -4 }.Normalized();
        Assert.Equal((WidgetCatalog.MaxRadarRange, WidgetCatalog.MinRadarSensitivity), (r.RadarRange, r.RadarSensitivity));
        Assert.Equal(WidgetCatalog.MinRadarRange, new WidgetSettings { Id = "radar", RadarRange = 2 }.Normalized().RadarRange);
        Assert.Null(new WidgetSettings { Id = "fuel", RadarRange = 20, RadarSensitivity = 4 }.Normalized().RadarRange);
        Assert.Equal(0.5, WidgetCatalog.RadarSensitivityFactor(1)); Assert.Equal(1.0, WidgetCatalog.RadarSensitivityFactor(3)); Assert.Equal(1.5, WidgetCatalog.RadarSensitivityFactor(5));
    }

    [Fact]
    public void Patch_changes_only_the_radar_fields_present_and_old_profiles_load_with_defaults()
    {
        var s = ProfileFactory.CreateDefault("x", "f1-1998").Get("radar")!;
        var p = new WidgetPatch { RadarRange = 25 }.ApplyTo(s);
        Assert.Equal((25, 3), (p.RadarRange, p.RadarSensitivity));
        p = new WidgetPatch { RadarSensitivity = 5 }.ApplyTo(p);
        Assert.Equal((25, 5), (p.RadarRange, p.RadarSensitivity));

        // perfil salvo antes do radar: o widget que falta entra com o padrao
        var old = ProfileFactory.CreateDefault("x", "f1-2004") with { Widgets = ProfileFactory.CreateDefault("x", "f1-2004").Widgets.Where(w => w.Id != "radar").ToList() };
        Assert.Null(old.Get("radar"));
        var n = old.Normalized();
        Assert.Equal((900, 585, 15), (n.Get("radar")!.X, n.Get("radar")!.Y, n.Get("radar")!.EffectiveRadarRange));
    }
}
