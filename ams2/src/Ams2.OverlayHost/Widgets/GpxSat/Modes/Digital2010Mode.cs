using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;

namespace Ams2.OverlayHost.Widgets.GpxSat.Modes;

/// <summary>Digital 2010. Geometry follows the 1024x768 specification; SDK-absent penalties and portraits are never fabricated.</summary>
public sealed class Digital2010Mode : IGpxMode
{
    public string Id => "digital2010";
    public bool Implemented => true;
    public int CarsPerPage => 8;
    static readonly string[] Din = ["DIN", "DIN Bold", "din-bold"];
    public GpxModeSpec Spec { get; } = new(14, new(Din, 700, 1), new(Din, 700, 1.6667f));
    const float Rh = 25, Pad = 18, SmallPad = 12, Top = 37.5f, Bottom = 730.5f, Base = Bottom - 4 * Rh / 3;
    const float Left = 1024f / 18, Right = 1024 - Left, X1 = 921.6f;
    const uint Light = 0xfffefefe, Dark = 0xff010101, Pit = 0xfffe0000, Faster = 0xff339900, Retired = 0xa0fefefe;
    const string Neutral = "d10_data_neutral", Caption = "d10_data_caption", Title = "d10_data_title", First = "d10_first", Slow = "d10_slower", Fast = "d10_faster", Fastest = "d10_fastest";
    string? _session;
    double _lastNow = double.NegativeInfinity, _leaderCross = double.NegativeInfinity;
    int _leaderLap;
    bool _directorEnabled, _directorLatched, _directorAhead;
    double _directorStart;
    int _directorPass=int.MinValue;
    readonly Dictionary<int, (int Lap, int Pos, int Sector)> _last = [];
    readonly Dictionary<int, int> _crossPosition = [];
    readonly Dictionary<int, double> _bestAt = [];
    readonly Dictionary<int, double> _best = [];

    static Digital2010Mode()
    {
        static GpxStop[] Grad(uint a, uint b, uint c) => [new(0,a), new(.12f,b), new(.84f,c), new(1,0xff08090b)];
        GpxTextures.Define(Neutral, Grad(0xff41444b,0xff17191e,0xff090b0e));
        GpxTextures.Define(Caption, Grad(0xff73767d,0xff4a4d54,0xff292c32));
        GpxTextures.Define(Title, Grad(0xffeeeeef,0xffd7d8db,0xffb8bbc1));
        GpxTextures.Define(First, Grad(0xffda171d,0xffbc050c,0xff860209));
        GpxTextures.Define(Slow, Grad(0xfffff398,0xfff5d842,0xffc7aa15));
        GpxTextures.Define(Fast, Grad(0xff5bce83,0xff228842,0xff14592a));
        GpxTextures.Define(Fastest, Grad(0xffb861d7,0xff763494,0xff462357));
    }
    static float W(float source, float height = Rh, float sourceHeight = 32) => source * height / sourceHeight * 1.2f;
    static GpxRect Rect(float x, float y, float width, float height = Rh) => new(x,y,width,height);
    static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
    static GpxScreen Screen(string slot, string key, IReadOnlyList<GpxBox> boxes, int page = 0, int priority = 0, bool logo=true)
    {
        if(logo && slot=="main" && boxes.Count>0)
        {
            var list=boxes.ToList(); float bottom=boxes.Max(b=>b.Rect.B), right=boxes.Max(b=>b.Rect.R);
            float x=key.StartsWith("standings:",StringComparison.Ordinal)?512-W(320):right+3.84f;
            float y=key.StartsWith("standings:",StringComparison.Ordinal)?boxes.Min(b=>b.Rect.T)-3.84f-4*Rh/3:bottom-4*Rh/3;
            list.Add(new GpxBox(Rect(x,y,4*4*Rh/3,4*Rh/3),Neutral,Logo:true) { Asset="digital2010/LG" });
            boxes=list;
        }
        return new(slot,"d10:" + key,boxes,GpxAnimKind.Instant,page,priority);
    }
    static GpxBox Box(GpxRect r, string tex, string? text = null, GpxAlign align = GpxAlign.Left, uint ink = Light, GpxFontRole font = GpxFontRole.Normal)
                => new(r,tex,text,align,ink,Indent: align == GpxAlign.Center ? Pad / 2 : Pad,Font:font)
        {
            Slant=7,
            Asset="digital2010/"+(tex switch { Neutral=>"data_neutral",Caption=>"data_caption",Title=>"data_title",First=>"gap_first",Slow=>"gap_slower",Fast=>"gap_faster",Fastest=>"data_fastest",_=>"data_neutral" }),
            SourceRect=new GpxRect(0,0,222,32),
        };
    static GpxBox Text(GpxRect r, string text, GpxAlign align = GpxAlign.Center, uint ink = Light, GpxFontRole font = GpxFontRole.Normal)
        => Box(r,Neutral,text,align,ink,font) with { Alpha = 0, Asset=null };
    static string Option(GpxContext c, string key, string fallback) => c.Settings.Option(key)?.Trim().ToLowerInvariant() ?? fallback;
    static bool Flag(GpxContext c, string key, bool fallback) => bool.TryParse(c.Settings.Option(key),out var value) ? value : fallback;
    static int Count(GpxContext c,string key,int fallback,int max = 64) => int.TryParse(c.Settings.Option(key),out var n) ? Math.Clamp(n,1,max) : fallback;
    static bool Out(CarSnapshot car) => car.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;
    static IReadOnlyList<StandingRow> Rows(GpxContext c) => c.Model.Standings.Count > 0 ? c.Model.Standings : c.Session.Cars.Where(x=>x.Position>0).OrderBy(x=>x.Position).Select(x=>new StandingRow(x,null,0,x.IsPlayer)).ToList();
    static string Tiny(GpxContext c,CarSnapshot car) { var n=c.Name(car).Split(' ',StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ""; return n[..Math.Min(3,n.Length)].ToUpperInvariant(); }

    void Observe(GpxContext c)
    {
        string session = c.Session.Track + "|" + c.Session.Kind + "|" + c.Broadcast.SessionSeenT.ToString(CultureInfo.InvariantCulture);
        if (_session != session || c.Now < _lastNow) { _session=session; _last.Clear(); _crossPosition.Clear(); _best.Clear(); _bestAt.Clear(); _leaderLap=0; _leaderCross=double.NegativeInfinity; _directorEnabled=_directorLatched=false; _directorPass=int.MinValue; }
        if(c.Options.DirectorsCut && !_directorEnabled) { _directorStart=c.Now; _directorLatched=false; _directorPass=int.MinValue; }
        _directorEnabled=c.Options.DirectorsCut;
        _lastNow=c.Now;
        foreach(var car in c.Session.Cars)
        {
            if (_last.TryGetValue(car.Index,out var p))
            {
                if(car.LapsCompleted!=p.Lap || car.Sector!=p.Sector) _crossPosition[car.Index]=p.Pos;
                if(car.Position==1 && car.LapsCompleted>p.Lap) _leaderCross=c.Now;
                if(car.BestLapTime>0 && (_best.GetValueOrDefault(car.Index)<=0 || _best.GetValueOrDefault(car.Index)>car.BestLapTime)) _bestAt[car.Index]=c.Now;
            }
            _last[car.Index]=(car.LapsCompleted,car.Position,car.Sector); _best[car.Index]=car.BestLapTime;
            if(car.Position==1) _leaderLap=car.CurrentLap;
        }
    }
    public IReadOnlyList<GpxScreen> Dispatch(GpxContext c)
    {
        Observe(c);
        if(c.Previewing) return Preview(c) is { } preview ? [preview] : [];
        var list=new List<GpxScreen>();
        if(c.Options.ShowMode=="none") return list;
        if(c.Options.ShowTimeLeft && Time(c) is { } timer) list.Add(timer);
        if(c.Options.ShowLogo) list.Add(Screen("f1","f1",[new GpxBox(Rect(Left+W(32)/2,Base-2*Rh,100,2*Rh),Neutral) { Asset="digital2010/F1" }]));
        GpxScreen? sky=null;
        var active=c.Sky.Active(c.Now);
        if(c.ShowSky)
        {
            if(c.IsRace && c.Broadcast.Winner is { } win && c.Now-win.FinishedT is >=0 and var age && age<c.Options.SkySeconds) sky=Winner(c,win);
            else if(active is { } e) sky=e.Kind switch { GpxSkyKind.FastestLap=>FastLap(c,e.Car,e.LapTime,e.Key,e.At), GpxSkyKind.OutOfRace=>Control(c,e.Car,false), GpxSkyKind.InPits=>PitInfo(c,e.Car,e.Car.Index==c.Focus?.Index), _=>Card(c,e.Car,e.Key) };
            if(sky is not null) list.Add(sky with { Priority=100 });
            if(active is { Kind:GpxSkyKind.InPits } pit && c.Focus is { InPitLane:true } focus && pit.Car.Index!=focus.Index)
                list.Add(PitInfo(c,focus,true) with { Slot="pitfocus",Priority=90 });
        }
        if(!c.ShowAll) return list;
        if(c.IsRace)
        {
            bool dc=c.Options.DirectorsCut;
            // Retrato da classificacao/torres no instante em que o lider cruza a linha (mesmo com outra tela no slot): valores congelados.
            if(!dc && (c.Options.Rotation || c.Options.RaceStandings is "standings" or "pitstops" or "interval")) FrozenStandings(c,c.Options.Rotation?LapMode(c):c.Options.RaceStandings);
            if(sky is null)
            {
                if(c.Focus is { InPitLane:true } focus) list.Add(PitInfo(c,focus,true) with { Priority=90 });
                else if(dc) AddDirector(c,list);
                else if(c.Options.Rotation) { if(GapNow(c,c.Options.SpeedSplitRace) is { } window) list.Add(window); else if(LapStandings(c) is { } page) list.Add(page); }
                else if(c.Options.RaceStandings is "standings" or "pitstops" or "interval" && FrozenStandings(c,c.Options.RaceStandings) is { Count:>0 } all) list.Add(all[c.PageOf(all.Count*StandingsPerPage(c),StandingsPerPage(c))]);
                else if(GapNow(c,c.Options.SpeedSplitRace) is { } gap) list.Add(gap);
            }
            else if(dc) AddDirector(c,list,active?.Kind is GpxSkyKind.OutOfRace or GpxSkyKind.InPits);
            else if(active?.Kind is not (GpxSkyKind.OutOfRace or GpxSkyKind.InPits) && GapNow(c,c.Options.SpeedSplitRace) is { } gap) list.Add(gap);
            if(!dc)
            {
                if((c.Options.RaceStandings=="off" || Flag(c,"alwaysTower",false)) && Option(c,"bottomTower","off") is var mode && mode!="off") list.Add(BottomTower(c,mode));
                if(Flag(c,"raceProgression",true)) list.Add(Progression(c));
            }
        }
        else if(sky is null)
        {
            if(c.Options.QualiStandings && QualiTower(c) is { } tower) list.Add(tower);
            if(c.Options.SpeedSplitQuali=="split" && QualiSplit(c) is { } split) list.Add(split);
        }
        // Legenda recorrente: menor prioridade, so quando nem o main nem o gap tem tela.
        if(c.CaptionOpen && !list.Any(s => s.Slot is "main" or "gap") && c.Focus is { InPitLane:false } me) list.Add(DriverCaption(c,me));
        return list;
    }
    /// <summary>Legenda recorrente do piloto entre eventos: nome, equipe, posicao grande, bandeira do pais e fornecedor de pneus (prints 2012). Prioridade 0.</summary>
    GpxScreen DriverCaption(GpxContext c,CarSnapshot car)
    {
        var boxes=new List<GpxBox>(); var name=Step(1); NameTeam(c,car,boxes,name);
        int position=c.PositionOf(car); BigPosition(boxes,Down(name),position);
        float x=name.R-2*Pad/3+(position>0?W(191,2*Rh,114)*105/117:0)+4, size=Rh;
        if(!string.IsNullOrWhiteSpace(car.Country)) boxes.Add(Box(Rect(x,name.T,W(48)),Neutral,car.Country) with { Image=GpxImage.CountryFlag,Asset=null,SourceRect=null });
        if(car.TyreSupplier is "M" or "B") boxes.Add(Box(Rect(x,name.T+size,size,size),Neutral) with { Image=car.TyreSupplier=="M"?GpxImage.TyreM:GpxImage.TyreB,Asset=null,SourceRect=null });
        return Screen("main","cap:"+car.Index,boxes);
    }
    GpxScreen? Time(GpxContext c, bool force=false)
    {
        var boxes=new List<GpxBox>();
        bool yellow=c.Session.FlagColour is 6 or 7;
        if(c.IsRace && c.Session.LapsInEvent>0)
        {
            int total=c.Session.LapsInEvent, current=Math.Max(0,_leaderLap), left=current==0?total:total-current+1;
            float width=186*Rh/32, x=512-width/2;
            boxes.Add(Box(Rect(x,Top,width),yellow?Slow:Neutral) with { Asset="digital2010/timer/"+(yellow?"lap_timer_yellow":"lap_timer"),SourceRect=new(0,0,186,32) });
            uint ink=yellow?Dark:Light;
            boxes.Add(Text(Rect(x,Top,width/3),"Lap",ink:ink));
            boxes.Add(Text(Rect(x+width/3,Top,width/3),Number(Option(c,"lapsLeftMode","remaining")=="remaining"?Math.Max(0,left):Math.Min(current,total)),ink:ink));
            boxes.Add(Text(Rect(x+width*2/3-9,Top,18),"/",ink:ink));
            boxes.Add(Text(Rect(x+width*2/3,Top,width/3),Number(total),ink:ink));
            if(left is >=1 and <=5 && current>=1 && c.Now-_leaderCross is >=0 and <18) boxes.Add(Box(Rect(512-287*Rh/64,Top+Rh+Rh/10,287*Rh/32),Caption,left==1?"Final lap":$"{left} Laps remaining",GpxAlign.Center) with { Asset="digital2010/data_caption_big",SourceRect=new(0,0,287,32) });
        }
        else
        {
            if(c.Session.TimeRemainingSeconds is not { } remain && !force) return null;
            double rem=c.Session.TimeRemainingSeconds ?? 5025;
            string texture=rem<=0?Caption:c.Now-c.Broadcast.SessionSeenT is >=0 and <5?Fast:yellow?Slow:Neutral;
            float width=W(133), x=512-width/2;
            boxes.Add(Box(Rect(x,Top,width),texture) with { Asset="digital2010/timer/"+(rem<=0?"timer_finished":texture==Fast?"timer_green":yellow?"timer_yellow":"timer_bg"),SourceRect=new(0,0,133,32) });
            boxes.Add(Text(Rect(x,Top,45),GpxFormat.SessionShort(c.Session.Kind)));
            boxes.Add(Text(Rect(x+45-Pad/2,Top,width-45+Pad/2),GpxFormat.Clock(rem),ink:yellow && texture==Slow?Dark:Light));
        }
        return Screen("top","time",boxes);
    }
    static GpxRect Step(int row, bool right=false)
    {
        float b=Base-row*Rh;
        // F1R_dx2 without the optional F1 image: LEFT + half timing cap + W/36.
        float l=right? X1-Pad*(2-row)-1024f/20-W(222):Left+W(32)/2+100+1024f/36+Pad*row;
        return Rect(l,b-Rh,W(222));
    }
    static GpxRect Down(GpxRect r) => Rect(r.L-Pad,r.T+Rh,r.W,r.H);
    static GpxRect Next(GpxRect r,float source=176) => Rect(r.R-2*Pad/3,r.T,W(source));
    void NameTeam(GpxContext c,CarSnapshot car,List<GpxBox> boxes,GpxRect name)
    {
        boxes.Add(Box(name,Neutral,c.Name(car)));
        boxes.Add(Box(Down(name),Caption,GpxContext.Team(car)));
    }
    static void BigPosition(List<GpxBox> boxes,GpxRect row,int position,bool right=true)
    {
        if(position<=0) return;
        float width=W(191,2*Rh,114)*105/117;
        float x=right?row.R-2*Pad/3:row.L+2*Pad/3+Pad-width;
        boxes.Add(Box(Rect(x,row.B-2*Rh,width,2*Rh),position==1?First:Neutral,Number(position),GpxAlign.Center,font:GpxFontRole.Big) with { Asset="digital2010/big_position_"+(position==1?"first":"neutral"),SourceRect=new(0,0,191,114) });
    }
    GpxScreen Card(GpxContext c,CarSnapshot car,string key)
    {
        if(car.InPitLane) return PitInfo(c,car,car.Index==c.Focus?.Index);
        var boxes=new List<GpxBox>(); var name=Step(1); NameTeam(c,car,boxes,name); BigPosition(boxes,Down(name),c.PositionOf(car));
        return Screen("main",key,boxes);
    }
    GpxScreen FastLap(GpxContext c,CarSnapshot car,double lap,string key,double at)
    {
        var boxes=new List<GpxBox>(); var name=Step(1); NameTeam(c,car,boxes,name);
        var col=Next(name); boxes.Add(Box(col,Caption,"Fastest Lap",GpxAlign.Center) with { Asset="digital2010/data_caption_medium",SourceRect=new(0,0,176,32) });
        col=Down(col); boxes.Add(Box(col,Neutral) with { Asset="digital2010/data_neutral_medium",SourceRect=new(0,0,176,32) });
        double dt=(c.Now-at)*1000;
        if(dt is >=400 and <4000) { double t=dt-400, a=(1-Math.Cos(2*Math.PI*(t%1200)/1200))/2; if(t/600 is >=1 and <5) a=Math.Min(1,.3+.7*a); boxes.Add(Box(col,Fast) with { Alpha=(float)a,Asset="digital2010/data_faster_medium",SourceRect=new(0,0,176,32) }); }
        boxes.Add(Text(col,GpxFormat.LapTime(lap)));
        return Screen("main",key,boxes);
    }
    GpxScreen Winner(GpxContext c,WinnerInfo win)
    {
        var boxes=new List<GpxBox>(); var title=Step(2); boxes.Add(Box(title,First,"Winner") with { Asset="digital2010/data_winner" }); NameTeam(c,win.Car,boxes,Down(title));
        var col=Next(title);
        foreach(string text in new[]{GpxFormat.LapTime(win.TotalSeconds),GpxFormat.Distance(win.DistanceKm),GpxFormat.AvgSpeed(win.AvgKmh)}) { boxes.Add(Box(col,Neutral,text,GpxAlign.Center) with { Asset="digital2010/data_neutral_medium",SourceRect=new(0,0,176,32) }); col=Down(col); }
        return Screen("main","winner:"+win.Car.Index,boxes);
    }
    GpxScreen Control(GpxContext c,CarSnapshot car,bool penalty)
    {
        float w=W(663), x=512-w/2;
        // AMS2 has no retirement cause nor penalty type: omit the original reason rather than inventing an accident.
        string id=string.IsNullOrWhiteSpace(car.CarNumber)?c.Name(car):$"Car {car.CarNumber} ({c.Name(car)})";
        var boxes=new List<GpxBox>();
        int lines=penalty&&c.IsRace?2:1;
        boxes.Add(Box(Rect(x,Base-(lines+1)*Rh,W(222)),Title,"Race control",ink:Dark));
        string message=id+(penalty?(c.IsRace?" got a drive-through penalty":" Time Disallowed - Track Limits"):" Is out of the race");
        boxes.Add(Box(Rect(x,Base-lines*Rh,w),Caption,message) with { Asset="digital2010/race_control",SourceRect=new(0,0,663,32) });
        if(lines==2) boxes.Add(Box(Rect(x,Base-Rh,w),Caption,car.CurrentLap<1?"for jump starting":"for cutting") with { Asset="digital2010/race_control",SourceRect=new(0,0,663,32) });
        return Screen("main","control:"+car.Index,boxes,logo:false);
    }
    GpxScreen PitInfo(GpxContext c,CarSnapshot car,bool focused)
    {
        var boxes=new List<GpxBox>(); var name=Step(focused?2:1,!focused); boxes.Add(Box(name,Neutral,c.Name(car)));
        var lane=Down(name); boxes.Add(Box(lane,focused?Title:Caption,"Pit Lane",ink:focused?Dark:Light) with { Asset="digital2010/"+(focused?"timer/pitlane_timer":"data_caption") });
        if(focused)
        {
            boxes.Add(Text(Rect(lane.L+lane.W/2,lane.T,lane.W/2),GpxFormat.Tenths(c.Broadcast.PlayerPitLaneNow(c.Now)),ink:Dark));
            if(c.Broadcast.PlayerStopped || double.IsFinite(c.Broadcast.PlayerPitLaneStartT) && c.Broadcast.PlayerStopEndT>=c.Broadcast.PlayerPitLaneStartT)
            {
                var stop=Down(lane); boxes.Add(Box(stop,Slow,"Pit Stop",ink:Dark) with { Asset="digital2010/timer/pitstop_timer" });
                boxes.Add(Text(Rect(stop.L+stop.W/2,stop.T,stop.W/2),GpxFormat.Tenths(c.Broadcast.PlayerStopNow(c.Now)),ink:Dark));
                BigPosition(boxes,stop,c.PositionOf(car));
            }
            else BigPosition(boxes,lane,c.PositionOf(car));
        }
        else BigPosition(boxes,lane,c.PositionOf(car),false);
        return Screen("main","pit:"+car.Index,boxes,logo:focused);
    }
    (string Text,uint Ink) Info(GpxContext c,IReadOnlyList<StandingRow> rows,int index,string mode,bool bottom=false)
    {
        var r=rows[index]; var car=r.Car;
        if(Out(car)) return ("Out",Retired);
        if(mode=="pitstops") return (GpxFormat.Stops(c.Broadcast.StopsOf(car.Index)),Light);
        if(car.InPitLane) return car.PitState==PitState.DrivingOutOfPits?("Pit exit",Faster):("Pit",Pit);
        if(car.Position!=1 && _crossPosition.TryGetValue(car.Index,out var old) && old!=car.Position) return ("",Light);
        if(car.Position==1) return (bottom?(mode=="interval"?"Interval":"Leader"):Flag(c,"showLapInStandings",true)?GpxFormat.Lap(car.CurrentLap):"",Light);
        if(mode=="interval" && index>0)
        {
            var ahead=rows[index-1]; int laps=Math.Max(0,r.LapsBehind-ahead.LapsBehind);
            if(laps>0) return (GpxFormat.LapsBehind(laps),Light);
            if(r.GapToLeader is { } gap && (ahead.Car.Position==1 || ahead.GapToLeader.HasValue)) return (GpxFormat.SignedGap(Math.Max(0,gap-(ahead.GapToLeader??0))),Light);
            return ("",Light);
        }
        return (r.LapsBehind>0?GpxFormat.LapsBehind(r.LapsBehind):r.GapToLeader is { } g?GpxFormat.SignedGap(g):"",Light);
    }
    int StandingsPerPage(GpxContext c) { int n=Count(c,"carsPerPage",8); return n==5?8:n; }
    /// <summary>Standings da corrida orientados a evento: so na janela aberta quando o lider fecha a volta (estaticos, uma passagem por pagina); fora dela, slot vazio.</summary>
    GpxScreen? LapStandings(GpxContext c)
    {
        var pages=FrozenStandings(c,LapMode(c));
        return GpxRotation.EventPage(c.Split.LeaderLapT,c.Now,c.Options.StandingsPageSeconds,pages.Count) is { } page ? pages[page] : null;
    }
    static string LapMode(GpxContext c) => c.Options.RaceStandings=="pitstops"?"pitstops":"standings";
    readonly GpxEventSnapshot _frozen=new();
    /// <summary>Todas as paginas da classificacao montadas quando o lider cruzou a linha e congeladas ate o proximo cruzamento (posicoes, gaps, voltas, Pit/Out).</summary>
    IReadOnlyList<GpxScreen> FrozenStandings(GpxContext c,string mode)
        => _frozen.Get("standings:"+mode,c.Split.LeaderLapT,()=>Enumerable.Range(0,GpxRotation.PagesOf(Rows(c).Count,StandingsPerPage(c))).Select(p=>Standings(c,mode,p)).ToArray());
    GpxScreen Standings(GpxContext c,string mode,int? fixedPage=null)
    {
        var rows=Rows(c); int n=StandingsPerPage(c);
        int page=fixedPage??c.PageOf(rows.Count,n), lines=(n+1)/2; float width=W(320), y=Base-lines*Rh;
        var boxes=new List<GpxBox>();
        for(int i=0;i<n && page*n+i<rows.Count;i++)
        {
            int index=page*n+i; var car=rows[index].Car; float x=512-width+(i/lines)*width, t=y+(i%lines)*Rh; var info=Info(c,rows,index,mode);
            boxes.Add(Box(Rect(x,t,width),car.Position==1&&!Out(car)?First:Neutral) with { Asset="digital2010/positions_"+(car.Position==1&&!Out(car)?"first":"neutral"),SourceRect=new(0,0,320,32) });
            boxes.Add(Text(Rect(x,t,48),Number(car.Position),ink:Out(car)?Retired:Light));
            boxes.Add(Text(Rect(x+48,t,width*2/3-48),Tiny(c,car),GpxAlign.Left,Out(car)?Retired:Light));
            boxes.Add(Text(Rect(x+width*2/3,t,width/3),info.Text,ink:info.Ink));
        }
        return Screen("main","standings:"+mode,boxes,page,40);
    }
    /// <summary>Torre inferior: paginas montadas quando o lider cruzou a linha e congeladas ate o proximo cruzamento; a pagina corrente segue o ciclo.</summary>
    GpxScreen BottomTower(GpxContext c,string mode,int? dcPage=null)
    {
        int n=Count(c,"bottomCars",5,12);
        var pages=_frozen.Get("tower:"+mode+":"+n,c.Split.LeaderLapT,()=>Enumerable.Range(0,Math.Max(1,GpxRotation.PagesOf(Rows(c).Count,n))).Select(p=>BottomTowerPage(c,mode,n,p)).ToArray());
        return pages[Math.Clamp(dcPage??c.PageOf(pages.Length*n,n),0,pages.Length-1)];
    }
    GpxScreen BottomTowerPage(GpxContext c,string mode,int n,int page)
    {
        var rows=Rows(c);
        const float space=6.144f, inset=3.072f;
        float height=Rh+2*inset, cap=height*1.2f, width=((Right-Left)-cap-(n-1)*space)/n;
        var boxes=new List<GpxBox>
        {
            Box(Rect(Left,Bottom-height,cap,height),Caption) with { Asset="digital2010/timing_background_1",SourceRect=null },
            Box(Rect(Left+cap,Bottom-height,Right-Left-2*cap,height),Caption) with { Asset="digital2010/timing_background_2",SourceRect=null },
            Box(Rect(Right-cap,Bottom-height,cap,height),Caption) with { Asset="digital2010/timing_background_3",SourceRect=null },
        };
        for(int i=0;i<n&&page*n+i<rows.Count;i++)
        {
            int index=page*n+i; var car=rows[index].Car; float x=Left+cap/2+i*(width+space), y=Bottom-Rh-inset; var info=Info(c,rows,index,mode,true);
            string tex=car.Position==1?First:Neutral;
            if(!Out(car)&&_crossPosition.TryGetValue(car.Index,out var old)&&old!=car.Position) { tex=old>car.Position?Fast:Slow; info=("",Light); }
            string cellAsset=Out(car)?"data_neutral":tex==Fast?"positiongain":tex==Slow?"positionlost":car.Position==1?"bottomscreenfirst":"bottomscreenpos";
            boxes.Add(Box(Rect(x,y,width),tex) with { Asset="digital2010/"+cellAsset,SourceRect=Out(car)?new(0,0,222,32):new(0,0,209,26) });
            if(!Out(car)) boxes.Add(Text(Rect(x,y,46*width/209),Number(car.Position)));
            float nameX=Out(car)?x:x+46*width/209;
            boxes.Add(Text(Rect(nameX,y,x+width/2-nameX),Tiny(c,car),ink:Out(car)?Retired:Light));
            boxes.Add(Text(Rect(x+width/2,y,width/2),info.Text,ink:info.Ink));
        }
        return Screen("bottom","tower:"+mode,boxes,page);
    }
    /// <summary>Torre de siglas (progressao): montada quando o lider cruzou a linha e congelada ate o proximo cruzamento.</summary>
    GpxScreen Progression(GpxContext c) => _frozen.Get("progression:"+Count(c,"raceTowerCars",10),c.Split.LeaderLapT,()=>ProgressionNow(c));
    GpxScreen ProgressionNow(GpxContext c)
    {
        var boxes=new List<GpxBox>(); float width=133*Rh/32;
        foreach(var row in Rows(c).Take(Count(c,"raceTowerCars",10)))
        {
            var car=row.Car; float y=Top+boxes.Count/5*Rh;
            boxes.Add(Box(Rect(Left,y,width),car.Position==1?First:Neutral) with { Asset="digital2010/tower/"+(car.Position==1?"race_bg_first":"race_bg"),SourceRect=new(0,0,133,32) });
            boxes.Add(Text(Rect(Left,y,37.5f),Number(car.Position)));
            boxes.Add(Text(Rect(Left+37.5f+SmallPad,y,width-37.5f-2*SmallPad),Tiny(c,car),GpxAlign.Left));
            float x=Left+width-2*Pad/3, w=102*Rh/32;
            int? gain=c.Model.Grid is { } grid && grid.TryGetValue(car.Index,out var start)?start-car.Position:null;
            boxes.Add(Box(Rect(x,y,w),gain<0?Slow:gain>0?Fast:Caption) with { Asset="digital2010/tower/"+(gain<0?"posnegative":gain>0?"pospositive":"posneutral"),SourceRect=new(0,0,102,32) });
            boxes.Add(Text(Rect(x+22.5f,y,w-22.5f),gain is { } d?Number(Math.Abs(d)):"",ink:gain<0?Dark:Light));
        }
        return Screen("left","progression",boxes);
    }
    GpxScreen? GapNow(GpxContext c,string direction)
    {
        GpxGapWindow? w=direction=="ahead"?c.Split.Ahead:direction=="behind"?c.Split.Behind:null;
        // At the end of the field, GPx's 'behind' choice falls back to the preceding car.
        if(direction=="behind" && c.Focus is { } f && f.Position==Rows(c).Count) w=c.Split.Ahead;
        return w is not null && w.ActiveAt(c.Now)?Gap(c,w):null;
    }
    GpxScreen Gap(GpxContext c,GpxGapWindow w)
    {
        var front=w.Ahead?w.Other:w.Focus; var rear=w.Ahead?w.Focus:w.Other;
        var boxes=new List<GpxBox>(); float x=X1-W(222), y=Base-3*Rh;
        void Label(CarSnapshot car,float t) { boxes.Add(Box(Rect(x,t,W(222)),car.Position==1?First:Neutral) with { Asset="digital2010/"+(car.Position==1?"gap_first":"labeled_data_neutral") }); boxes.Add(Text(Rect(x,t,45),Number(car.Position))); boxes.Add(Text(Rect(x+45,t,W(222)-45),c.Name(car),GpxAlign.Left)); }
        Label(front,y); Label(rear,y+2*Rh);
        float half=W(222)/2+2*Pad/9;
        boxes.Add(Box(Rect(x,y+Rh,half),Neutral,w.Laps>0?GpxFormat.LapsBehind(w.Laps):w.Gap is { } gap?GpxFormat.SignedGap(gap):"+ "+GpxFormat.Tenths(w.Running(c.Now)),GpxAlign.Right) with { Asset="digital2010/race_gap",SourceRect=new(0,0,133,32) });
        // Change is delta from the previous split, so reconstruct the previous gap; never display delta as a gap.
        if(w.Laps==0 && w.Gap is { } current && w.Change is { } change && current-change>0) boxes.Add(Box(Rect(x+half-4*Pad/9,y+Rh,W(222)-half+4*Pad/9),Neutral,"("+GpxFormat.SignedGap(current-change)+")",GpxAlign.Right) with { Asset="digital2010/race_gap",SourceRect=new(0,0,133,32) });
        return Screen("gap","gap:"+w.Ahead+":"+w.Pass,boxes,priority:30);
    }
    void AddDirector(GpxContext c,List<GpxScreen> list,bool suppressGap=false)
    {
        var rows=Rows(c); int n=Count(c,"bottomCars",5,12), pages=Math.Max(1,(rows.Count+n-1)/n), page=(1+(int)(Math.Max(0,c.Now-_directorStart)/10))%pages;
        string mode=_leaderLap%5==0 && c.Broadcast.Stops.Any(x=>x.Value>0)?"pitstops":_leaderLap%2==1?"interval":"standings";
        list.Add(BottomTower(c,mode,page));
        var leader=rows.FirstOrDefault()?.Car;
        if(leader is { Sector:1 } && (_leaderLap==3 || _leaderLap==c.Session.LapsInEvent-3)) list.Add(Progression(c));
        if(suppressGap) return;
        var ahead=c.Split.Ahead is { } a&&a.ActiveAt(c.Now)?a:null; var behind=c.Split.Behind is { } b&&b.ActiveAt(c.Now)?b:null;
        var selected=ahead is null?behind:behind is null?ahead:(behind.Gap??behind.Running(c.Now))<(ahead.Gap??double.MaxValue)?behind:ahead;
        if(selected is null) return;
        if(selected.Pass!=_directorPass) { _directorPass=selected.Pass; _directorLatched=false; }
        if(!_directorLatched)
        {
            // The original retries the 30% cancellation next frame, then latches the choice for this split.
            // Hashing frame/pass keeps previews reproducible while preserving that transient cancellation.
            uint sample=(uint)c.Model.Frame^(uint)(selected.Pass*2654435761u); sample^=sample<<13; sample^=sample>>17; sample^=sample<<5;
            if(sample%10<3) return;
            _directorLatched=true; _directorAhead=selected.Ahead;
        }
        var latched=_directorAhead?ahead:behind;
        if(latched is not null) list.Add(Gap(c,latched));
    }
    GpxScreen? QualiTower(GpxContext c)
    {
        var quali=c.Model.Quali??(c.Previewing?QualiTable.Build(c.Session):null);
        if(quali is null) return null;
        var boxes=new List<GpxBox>(); float width=W(222); int i=0;
        foreach(var row in quali.Rows.Take(Count(c,"qualiTowerCars",22,22)))
        {
            if(!row.HasTime) break;
            var car=row.Car; float y=Top+i++*Rh;
            string time=row.Rank==1||Option(c,"qualiMode","gap")=="absolute"?GpxFormat.LapTime(row.BestLap):row.GapToFirst is { } gap?GpxFormat.SignedGap(gap):"";
            uint ink=_bestAt.TryGetValue(car.Index,out var at)&&c.Now-at is >=0 and <7&&Math.Abs(car.LastLapTime-car.BestLapTime)<.001?Faster:Light;
            boxes.Add(Box(Rect(Left,y,width),row.Rank==1?First:Neutral) with { Asset="digital2010/tower/"+(row.Rank==1?"bg_first":"bg_newlap") });
            boxes.Add(Text(Rect(Left,y,45),Number(row.Rank)));
            float nameWidth=(width-45-SmallPad)/3;
            boxes.Add(Text(Rect(Left+45+SmallPad,y,nameWidth),Tiny(c,car),GpxAlign.Left));
            boxes.Add(Text(Rect(Left+45+SmallPad+nameWidth,y,width-45-2*SmallPad-nameWidth),time,ink:ink));
            if(car.RaceState==RaceState.Finished || car.InPitLane&&Flag(c,"showPitIcon",true)) boxes.Add(Box(Rect(Left+width-2*Pad/3,y,W(45)),Caption) with { Asset="digital2010/tower/"+(car.RaceState==RaceState.Finished?"finished":"InPit"),SourceRect=new(0,0,45,32) });
        }
        return boxes.Count==0?null:Screen("left","quali",boxes);
    }
    GpxScreen? QualiSplit(GpxContext c)
    {
        if(c.Focus is not { } car) return null;
        var q=c.Model.QualiLap;
        if(q is null || q.CarIndex!=car.Index || q.InPit || q.OutLap || q.Elapsed is null) return QualiCard(c,car);
        var boxes=new List<GpxBox>(); var name=Step(1); boxes.Add(Box(name,Neutral,c.Name(car),GpxAlign.Center)); var timer=Down(name);
        double? elapsed=q.Elapsed, delta=null; bool finished=false, frozen=false, invalid=false;
        if(q.LastSplit is { } sp && c.Now-sp.At is >=0 and var age && age<c.Options.SplitSeconds) { elapsed=sp.Elapsed; delta=sp.DeltaLeader; frozen=true; }
        if(q.LastResult is { } result && c.Now-result.At is >=0 and var resultAge && resultAge<c.Options.SplitSeconds && (q.LastSplit is null || result.At>=q.LastSplit.At)) { elapsed=result.LapTime; delta=result.Invalid?null:result.GapToFirst; finished=frozen=true; invalid=result.Invalid; }
        boxes.Add(Box(timer,Neutral,invalid?GpxFormat.InvalidLap:frozen?GpxFormat.LapTime(elapsed):GpxFormat.Tenths(elapsed),GpxAlign.Right));
        if(delta is { } d)
        {
            var compare=Down(timer); bool fastest=d<0&&finished;
            boxes.Add(Box(compare,fastest?Fastest:d<0?Fast:Slow));
            if(!fastest) boxes.Add(Text(Rect(compare.L,compare.T,45),"1",ink:d>=0?Dark:Light));
            boxes.Add(Text(Rect(compare.L+(fastest?0:45),compare.T,compare.W-(fastest?0:45)),GpxFormat.SignedGap(d),ink:d>=0?Dark:Light));
        }
        if(c.Session.Kind==SessionKind.Qualify && finished && q.LastResult is { Improved:true,Invalid:false } improved) BigPosition(boxes,timer,improved.Position);
        return Screen("main","qualisplit:"+car.Index+":"+(finished?"finish":q.Sector),boxes);
    }
    GpxScreen QualiCard(GpxContext c,CarSnapshot car)
    {
        var boxes=new List<GpxBox>(); var name=Step(1); var row=c.Model.Quali?.Row(car.Index); NameTeam(c,car,boxes,name);
        if(c.Session.Kind==SessionKind.Qualify)
        {
            BigPosition(boxes,Down(name),c.PositionOf(car)); var col=Next(name);
            if(row is { HasTime:true })
            {
                boxes.Add(Box(col,Neutral,GpxFormat.LapTime(row.BestLap),GpxAlign.Center));
                double? gap=row.Rank==1?c.Model.Quali?.Rows.Skip(1).FirstOrDefault(x=>x.HasTime)?.GapToFirst:row.GapToFirst;
                if(gap is { } d) boxes.Add(Box(Down(col),Neutral,GpxFormat.SignedGap(d),GpxAlign.Center));
            }
        }
        else if(row is { HasTime:true }) { boxes.Clear(); boxes.Add(Box(name,Neutral,c.Name(car),GpxAlign.Center)); boxes.Add(Box(Down(name),Neutral,GpxFormat.LapTime(row.BestLap),GpxAlign.Center)); }
        return Screen("main","qualicard:"+car.Index,boxes);
    }
    GpxScreen? PreviewSplit(GpxContext c,CarSnapshot car,bool faster)
    {
        var state=QualiLapState.Empty with { CarIndex=car.Index,Lap=3,Elapsed=81.112,InPit=false,OutLap=false,Sector=1,
            LastSplit=new QualiSplit(1,31.112,null,faster?-.204:.204,c.Now),
            LastResult=faster?new QualiLapResult(2,80.874,1,-.112,null,true,false,[],c.Now,false):null };
        var session=c.Session with { Kind=SessionKind.Qualify };
        return QualiSplit(new GpxContext { Model=c.Model with { Session=session,QualiLap=state },Session=session,Options=c.Options,Settings=c.Settings,G=c.G,Sky=c.Sky,Split=c.Split });
    }
    GpxScreen? Preview(GpxContext c)
    {
        var cars=c.Session.Cars.OrderBy(x=>x.Position).ToList(); if(cars.Count==0)return null;
        var car=c.Focus??cars[0]; CarSnapshot At(int n)=>cars[Math.Min(n,cars.Count-1)];
        return c.Options.PreviewScreen!.ToLowerInvariant() switch
        {
            "time"=>Time(c,true), "fl"=>FastLap(c,cars[0],cars[0].BestLapTime>0?cars[0].BestLapTime:81.443,"preview:fl",c.Now-1),
            "ride"=>Card(c,car,"preview:ride"), "cap"=>DriverCaption(c,car), "card"=>QualiCard(c,car), "out"=>Control(c,At(1),false), "dt"=>Control(c,At(1),true),
            "pit"=>PitInfo(c,At(1),false), "pitt"=>PitInfo(c,car,true),
            "win"=>Winner(c,c.Broadcast.Winner??new WinnerInfo(cars[0],5534.532,305.28,198.721,c.Now)),
            "str"=>Standings(c,"standings"), "stp"=>Standings(c,"pitstops"), "sti"=>Standings(c,"interval"), "stq"=>QualiTower(c),
            "bottom"=>BottomTower(c,"standings"), "progression"=>Progression(c),
            "gapa"=>Gap(c,new GpxGapWindow(true,At(2),At(1),7,c.Now-1,c.Now+7,1.842,0,-.12)),
            "gapb"=>Gap(c,new GpxGapWindow(false,At(1),At(2),7,c.Now-1,c.Now+7,null,0,null)),
            "split"=>PreviewSplit(c,car,false), "splitgreen"=>PreviewSplit(c,car,true), _=>null,
        };
    }
}


