using Ams2.Core.Calc.GpxSat;

namespace Ams2.Core.Tests;

public class GpxPassageTests
{
    static CarSnapshot Car(int id, int position, int sector = 0) => new(id, "Driver " + id, "Car", "F1", position, position,
        1, 2, 0, sector, 80, 80, 50, PitState.None, RaceState.Racing, false, id == 0);
    static SessionSnapshot Session(params CarSnapshot[] cars) => new(14, 1, true, SessionKind.Race, "Track", "", 5000, 20, null, 0,
        new(20, 30, 0, 0, 0, 0, 0, 0), cars, new(0, new(0,0,0,0,0,0,0,0),3,9000,18000,50,40,100,[],.5));

    [Fact]
    public void Same_frame_crossings_are_independent_of_car_enumeration_order()
    {
        foreach (bool focusFirst in new[] { true, false })
        {
            var focus = Car(0, 2); var ahead = Car(1, 1); var behind = Car(2, 3);
            var tracker = new GpxSplitTracker();
            SessionSnapshot Frame(int sector) => focusFirst ? Session(focus with { Sector=sector }, ahead with { Sector=sector }, behind with { Sector=sector })
                : Session(behind with { Sector=sector }, ahead with { Sector=sector }, focus with { Sector=sector });
            tracker.Update(0,Frame(0)); tracker.Update(1,Frame(1));
            Assert.Equal(0,tracker.Ahead!.Gap);
            Assert.Equal(0,tracker.Behind!.Gap);
        }
    }

    [Fact]
    public void Missing_new_reference_never_reuses_the_previous_sector_gap()
    {
        var focus=Car(0,2); var ahead=Car(1,1); var t=new GpxSplitTracker();
        t.Update(0,Session(focus,ahead));
        t.Update(1,Session(focus,ahead with { Sector=1 }));
        t.Update(2,Session(focus with { Sector=1 },ahead with { Sector=1 }));
        Assert.Equal(1,t.Ahead!.Gap);
        t.Update(3,Session(focus with { Sector=2 },ahead with { Sector=1 }));
        Assert.Null(t.Ahead);
    }

    [Fact]
    public void Map_requires_observed_geometry_and_resets_for_a_different_track()
    {
        var map = new GpxTrackMap();
        var s=Session(Car(0,1)); map.Observe(s); Assert.False(map.Ready); Assert.Equal(0,map.Count);
        for(int i=0; i<256; i++)
        {
            double a=2*Math.PI*i/256;
            if (i==100) continue;
            map.Observe(s with { Cars=[Car(0,1) with { LapDistance=(i+.1)*5000/256,PosX=100*Math.Cos(a),PosZ=100*Math.Sin(a) }] });
        }
        Assert.True(map.Ready);
        Assert.Equal(254,map.Segments.Count());
        var point=map.Project(new(100,0),0,0,256); Assert.InRange(point.X,0,256); Assert.InRange(point.Y,0,256);
        map.Observe(s with { Track="Other" }); Assert.False(map.Ready); Assert.Equal(0,map.Count);
    }

    [Fact]
    public void Leader_lap_close_opens_standings_window_only_on_the_leader_crossing_the_line()
    {
        var t = new GpxSplitTracker();
        var leader = Car(1, 1, 2); var other = Car(2, 2, 2);
        t.Update(0, Session(leader, other));
        Assert.Null(t.LeaderLapT);
        t.Update(1, Session(leader with { Sector = 0, LapsCompleted = leader.LapsCompleted + 1 }, other));   // lider cruza a linha
        Assert.Equal(1, t.LeaderLapT);
        t.Update(5, Session(leader with { Sector = 0, LapsCompleted = leader.LapsCompleted + 1 }, other with { Sector = 0, LapsCompleted = other.LapsCompleted + 1 }));   // P2 cruza: nao reabre
        Assert.Equal(1, t.LeaderLapT);
    }

    [Fact]
    public void Standings_event_window_is_finite_static_per_page_and_empty_outside()
    {
        Assert.Null(GpxRotation.EventPage(null, 100, 12, 1));            // sem evento: nada
        Assert.Null(GpxRotation.EventPage(10, 9.9, 12, 1));
        Assert.Equal(0, GpxRotation.EventPage(10, 10, 12, 2));
        Assert.Equal(0, GpxRotation.EventPage(10, 21.9, 12, 2));         // estatica dentro da pagina
        Assert.Equal(1, GpxRotation.EventPage(10, 22, 12, 2));
        Assert.Null(GpxRotation.EventPage(10, 34, 12, 2));               // some
        Assert.Null(GpxRotation.EventPage(10, 11, 12, 0));               // sem dado
    }
}
