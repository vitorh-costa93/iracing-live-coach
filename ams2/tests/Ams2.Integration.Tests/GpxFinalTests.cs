using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class GpxFinalTests
{
    static GpxContext Context(string theme, WidgetSettings? settings=null, GpxOptions? options=null)
    {
        var model=LayoutPreview.ForWidget("lapcounter");
        return new() { Model=model,Session=model.Session!,G=new(GpxModeRegistry.Create(theme).Spec.FH),
            Settings=settings??new() { Id="gpxsat" },Options=options??new(),Sky=new(),Split=new() };
    }
    [Fact]
    public void All_registered_modes_are_implemented_and_use_distinct_original_families()
    {
        var modes=GpxModeRegistry.ThemeModes.Keys.Select(GpxModeRegistry.Create).ToArray();
        Assert.Equal(4,modes.Length); Assert.All(modes,m=>Assert.True(m.Implemented));
        Assert.Equal(4,modes.Select(m=>m.Spec.Normal.Families[0]).Distinct().Count());
        Assert.DoesNotContain('\f',FontLibrary.DefaultGpxFontDirectory);
    }
    [Fact]
    public void Normalized_remaining_option_keeps_its_meaning_in_2010()
    {
        var cfg=new WidgetSettings { Id="gpxsat",Options=new() { ["lapsLeftMode"]="remaining" } }.Normalized("f1-2012");
        var c=Context("f1-2012",cfg,new() { PreviewScreen="time" });
        var main=Assert.Single(new Digital2010Mode().Dispatch(c));
        int lap=c.Session.Cars.First(x=>x.Position==1).CurrentLap;
        Assert.Contains(main.Boxes,b=>b.Text==Math.Max(0,c.Session.LapsInEvent-Math.Max(1,lap)+1).ToString());
    }
    [Fact]
    public void Classic_default_clock_does_not_suppress_the_race_cards()
    {
        var cfg=new WidgetSettings { Id="gpxsat" }.Normalized("f1-1998");
        Assert.False(GpxOptions.From(cfg,classicClock:true).ShowTimeLeft);
        Assert.Equal("false",WidgetCatalog.OptionsFor("f1-1998","gpxsat").Single(o=>o.Id=="showTimeLeft").Default);
    }
    [Fact]
    public void Frame_signature_is_stable_for_same_content_and_changes_with_shown_text()
    {
        var w=new GpxSatWidget(); w.UseTheme(Ams2.OverlayHost.Theme.Themes.F1_2012);
        w.Configure(new WidgetSettings { Id="gpxsat",Show=ShowCatalog.PureGpx,Options=new() { ["lapsLeftMode"]="remaining",["previewScreen"]="time" } }.Normalized("f1-2012"));
        var m=LayoutPreview.ForWidget("lapcounter") with { Inputs=null };
        long? a=w.FrameSignature(m);
        Assert.NotNull(a);
        Assert.Equal(a,w.FrameSignature(m));
        Assert.Equal(a,w.FrameSignature(m with { }));                        // novo registro, mesmo conteudo e relogio
        var changed=m with { Session=m.Session! with { LapsInEvent=m.Session!.LapsInEvent+1 } };
        Assert.NotEqual(a,w.FrameSignature(changed));                        // texto exibido mudou
    }
    [Fact]
    public void Gauge_angles_match_the_original_240kph_and_9000rpm_marks()
    {
        Assert.Equal(0,GpxGaugesWidget.Angle(240,360));
        Assert.Equal(45,GpxGaugesWidget.Angle(9000,18000));
        Assert.Equal(-90,GpxGaugesWidget.Angle(999,360));
    }
}
