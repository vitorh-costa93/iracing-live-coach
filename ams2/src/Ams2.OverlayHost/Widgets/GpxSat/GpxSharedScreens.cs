using Ams2.Core.Calc.GpxSat;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>Microtime e lista de opções: independentes do ShowMode, como no laço compartilhado do GPxSAT.</summary>
public static class GpxSharedScreens
{
    static bool Flag(GpxContext c, string key) => c.Settings.Option(key) == "true";
    public static IEnumerable<GpxScreen> Dispatch(GpxContext c)
    {
        if (Flag(c, "microTime") && c.Model.QualiLap is { } lap && c.Focus is { } focus)
        {
            bool personal = c.Settings.Option("microCompare") == "personal";
            bool delta = c.Settings.Option("microAbsolute") != "true";
            var reference = personal ? lap.PersonalReference : lap.LeaderReference;
            const float x=10, y=10, cell=60, rh=12, label=12;
            var boxes = new List<GpxBox>();
            GpxBox Box(float xx, float yy, float w, string text, uint background, uint ink)
            {
                string texture = "micro:" + background;
                if (!GpxTextures.Has(texture)) GpxTextures.Define(texture, [new(0, background), new(1, background)]);
                return new(new(xx, yy, w, rh), texture, text, GpxAlign.Right, ink, Indent:2, Font:GpxFontRole.Mono) { Stroke=0xff808080 };
            }
            boxes.Add(Box(x,y,2*cell+label,"µtime",0xff010101,GpxInk.White));
            boxes.Add(Box(x+label,y+rh,cell,reference is null ? "" : c.Name(reference.Car)[..Math.Min(10,c.Name(reference.Car).Length)],0xff000099,GpxInk.White));
            boxes.Add(Box(x+label+cell,y+rh,cell,c.Name(focus)[..Math.Min(10,c.Name(focus).Length)],0xff339900,GpxInk.White));
            for(int i=0; i<3; i++)
            {
                double? value = i == 2 ? lap.LastResult is { Invalid:false } result ? result.LapTime : null
                    : lap.Sectors.Take(i+1).All(s => s is not null) ? lap.Sectors.Take(i+1).Sum(s=>s!.Time) : null;
                double? compare = reference?.Times.ElementAtOrDefault(i);
                string valueText = value is null || (delta && compare is null) ? "-:--.---" : delta ? GpxFormat.SignedGap(value.Value-compare!.Value) : GpxFormat.LapTime(value);
                float yy=y+(i+2)*rh;
                boxes.Add(Box(x,yy,label,i==2?"F":(i+1).ToString(),0xfffefefe,0xff101010));
                boxes.Add(Box(x+label,yy,cell,GpxFormat.LapTime(compare),0xffd4d0c8,0xff101010));
                boxes.Add(Box(x+label+cell,yy,cell,valueText,0xffd4d0c8,value is null?0xff808080:compare is { } other && value<other?0xfffe0000:0xff101010));
            }
            yield return new("microtime","microtime",boxes,GpxAnimKind.Instant);
        }
        if (Flag(c,"infoScreen"))
        {
            var lines = new List<(string Label,bool Active)>
            {
                ("Skymessages only",c.Options.ShowMode=="sky"),("Hide all",c.Options.ShowMode=="none"),
                (c.IsRace?"Lap counter":"Time left",c.Options.ShowTimeLeft),("Director's cut",c.Options.DirectorsCut),
                ("Standings",c.IsRace?c.Options.RaceStandings=="standings":c.Options.QualiStandings),
                ("Pit stops",c.Options.RaceStandings=="pitstops"),("Gap car ahead",c.Options.SpeedSplitRace is "ahead" or "nearest"),
                ("Gap car behind",c.Options.SpeedSplitRace is "behind" or "nearest"),("Speed traps",c.Options.SpeedSplitRace=="trap"),
            };
            yield return new("info","info",lines.Select((line,i)=>new GpxBox(new(700,i*14,300,14),"gpx_shared_clear",line.Label,GpxAlign.Right,line.Active?0xfffe0000:GpxInk.White,Font:GpxFontRole.Mono)).ToArray(),GpxAnimKind.Instant);
        }
    }
    static GpxSharedScreens() => GpxTextures.Define("gpx_shared_clear",[new(0,0),new(1,0)]);
}
