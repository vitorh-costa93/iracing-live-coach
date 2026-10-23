using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public sealed class Digital2010ModeTests
{
    static GpxContext Context(GpxOptions? options=null, Dictionary<string,string>? settings=null, SessionKind kind=SessionKind.Race)
    {
        var model=LayoutPreview.ForWidget("lapcounter");
        var session=model.Session! with { Kind=kind };
        model=model with { Session=session, Now=100, Broadcast=BroadcastState.Empty with { SessionSeenT=0 } };
        return new GpxContext { Model=model, Session=session, Options=options??new(), Settings=new WidgetSettings { Id="gpxsat", Options=settings }, G=new(14), Sky=new(), Split=new() };
    }
    [Fact]
    public void Original_font_and_big_position_use_DIN_bold_and_original_multiplier()
    {
        var mode=new Digital2010Mode();
        Assert.True(mode.Implemented); Assert.Equal(14,mode.Spec.FH); Assert.Equal(700,mode.Spec.Normal.Weight);
        Assert.Contains("DIN",mode.Spec.Normal.Families); Assert.InRange(mode.Spec.Big.SizeFh,1.6666f,1.6668f);
    }
    [Fact]
    public void Hidden_mode_omits_every_slot_and_sky_only_omits_race_towers()
    {
        var mode=new Digital2010Mode();
        Assert.Empty(mode.Dispatch(Context(new GpxOptions { ShowMode="none" })));
        var screens=mode.Dispatch(Context(new GpxOptions { ShowMode="sky" },new() { ["bottomTower"]="standings",["raceProgression"]="true" }));
        Assert.All(screens,s=>Assert.Equal("top",s.Slot));
    }
    [Theory]
    [InlineData("ride")][InlineData("fl")][InlineData("out")][InlineData("pit")][InlineData("pitt")][InlineData("dt")][InlineData("win")]
    [InlineData("split")][InlineData("splitgreen")][InlineData("str")][InlineData("stp")][InlineData("sti")][InlineData("stq")][InlineData("bottom")][InlineData("progression")][InlineData("gapa")][InlineData("gapb")][InlineData("time")]
    public void Preview_screens_are_instant_and_geometry_stays_inside_the_reference_canvas(string preview)
    {
        var screen=Assert.Single(new Digital2010Mode().Dispatch(Context(new GpxOptions { PreviewScreen=preview })));
        Assert.Equal(GpxAnimKind.Instant,screen.Anim); Assert.NotEmpty(screen.Boxes);
        Assert.All(screen.Boxes,b=> { Assert.InRange(b.Rect.L,0,1024); Assert.InRange(b.Rect.R,0,1024); Assert.InRange(b.Rect.T,0,768); Assert.InRange(b.Rect.B,0,768); Assert.True(b.Rect.W>0); Assert.True(b.Rect.H>0); Assert.True(GpxTextures.Has(b.Texture)); });
    }
    [Fact]
    public void Eight_car_standings_uses_two_columns_and_no_title()
    {
        var screen=Assert.Single(new Digital2010Mode().Dispatch(Context(new GpxOptions { PreviewScreen="str" })));
        var backgrounds=screen.Boxes.Where(b=>b.Text is null && !b.Logo).ToList();
        Assert.InRange(backgrounds.Count,1,8);
        Assert.All(backgrounds,b=>Assert.Equal(300,b.Rect.W));
        Assert.All(backgrounds,b=>Assert.Contains(b.Rect.L,new float[]{212,512}));
        Assert.DoesNotContain(screen.Boxes,b=>b.Text?.Contains("Race control")==true);
    }
    [Fact]
    public void Race_standings_suppresses_bottom_tower_unless_alwaysTower_is_enabled()
    {
        var options=new GpxOptions { RaceStandings="standings", Rotation=false };
        var first=new Digital2010Mode().Dispatch(Context(options,new() { ["bottomTower"]="standings",["raceProgression"]="false" }));
        Assert.DoesNotContain(first,s=>s.Slot=="bottom"); Assert.Contains(first,s=>s.Key.Contains("standings"));
        var second=new Digital2010Mode().Dispatch(Context(options,new() { ["bottomTower"]="standings",["alwaysTower"]="true",["raceProgression"]="false" }));
        Assert.Contains(second,s=>s.Slot=="bottom");
    }
    [Fact]
    public void Pit_timer_has_priority_over_race_standings_and_does_not_show_unknown_stop()
    {
        var c=Context(new GpxOptions { RaceStandings="standings",DirectorsCut=true },new(){["raceProgression"]="false"});
        var car=c.Focus!; var session=c.Session with { Cars=c.Session.Cars.Select(x=>x.Index==car.Index?x with { PitState=PitState.DrivingIntoPits }:x).ToArray() };
        c=new GpxContext { Model=c.Model with { Session=session }, Session=session,Options=c.Options,Settings=c.Settings,G=c.G,Sky=c.Sky,Split=c.Split };
        var main=Assert.Single(new Digital2010Mode().Dispatch(c).Where(x=>x.Slot=="main"));
        Assert.StartsWith("d10:pit:",main.Key); Assert.Equal(90,main.Priority);
        Assert.DoesNotContain(main.Boxes,b=>b.Text=="Pit Stop");
    }
    [Fact]
    public void Missing_grid_keeps_progression_gain_blank_instead_of_claiming_zero()
    {
        var c=Context(new GpxOptions { PreviewScreen="progression" });
        c=new GpxContext { Model=c.Model with { Grid=null },Session=c.Session,Options=c.Options,Settings=c.Settings,G=c.G,Sky=c.Sky,Split=c.Split };
        var screen=Assert.Single(new Digital2010Mode().Dispatch(c));
        Assert.All(screen.Boxes.Where((_,i)=>i%5==4),b=>Assert.Equal("",b.Text));
    }
    [Fact]
    public void Unsupported_speed_trap_never_generates_a_fake_screen()
    {
        var screens=new Digital2010Mode().Dispatch(Context(new GpxOptions { SpeedSplitRace="trap", Rotation=false },new(){["raceProgression"]="false"}));
        Assert.DoesNotContain(screens,s=>s.Slot is "main" or "gap");
    }
}



