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
    public void Line_crossing_survives_laps_completed_updating_one_frame_before_sector()
    {
        var focus = Car(0, 2, 2); var ahead = Car(1, 1, 2); var t = new GpxSplitTracker();
        CarSnapshot Lap(CarSnapshot c, int sector) => c with { LapsCompleted = c.LapsCompleted + 1, Sector = sector };
        t.Update(0, Session(focus, ahead));
        t.Update(1, Session(focus, Lap(ahead, 2)));            // laps +1, sector ainda 2
        t.Update(2, Session(focus, Lap(ahead, 0)));            // sector zera: nao duplica
        t.Update(3, Session(Lap(focus, 2), Lap(ahead, 0)));
        t.Update(4, Session(Lap(focus, 0), Lap(ahead, 0)));
        Assert.Null(t.Ahead);   // linha de chegada = so classificacao: sem janela de gap
        t.Update(5, Session(Lap(focus, 0), Lap(ahead, 1)));
        t.Update(7, Session(Lap(focus, 1), Lap(ahead, 1)));
        Assert.Equal(1, t.Ahead!.Split);   // a passagem da linha nao foi duplicada: o proximo gap e o do S1 (gap 2 medido na linha de referencia)
        Assert.Equal(2, t.Ahead.Gap);
        // ordem inversa (sector zera antes de laps) continua valendo.
        var f2 = Car(0, 2, 2); var a2 = Car(1, 1, 2); var t2 = new GpxSplitTracker();
        t2.Update(0, Session(f2, a2));
        t2.Update(1, Session(f2, a2 with { Sector = 0 }));
        t2.Update(2, Session(f2, Lap(a2, 0)));
        t2.Update(3, Session(f2 with { Sector = 0 }, Lap(a2, 0)));
        t2.Update(4, Session(Lap(f2, 0), Lap(a2, 0)));
        Assert.Null(t2.Ahead);
        t2.Update(5, Session(Lap(f2, 0), Lap(a2, 1)));
        t2.Update(7, Session(Lap(f2, 1), Lap(a2, 1)));
        Assert.Equal(2, t2.Ahead!.Gap);
    }

    [Fact]
    public void Gap_opens_at_s1_and_s2_every_lap_not_at_the_line_and_line_two_is_the_previous_sector_gap()
    {
        var focus = Car(0, 2); var ahead = Car(1, 1); var t = new GpxSplitTracker();
        t.Update(0, Session(focus, ahead));
        // Volta 1: S1 (gap 2) sem anterior; S2 (gap 3) com anterior = S1.
        ahead = ahead with { Sector = 1 }; t.Update(10, Session(focus, ahead));
        focus = focus with { Sector = 1 }; t.Update(12, Session(focus, ahead));
        Assert.Equal(1, t.Ahead!.Split); Assert.Equal(2, t.Ahead.Gap!.Value, 3); Assert.Null(t.Ahead.Previous);
        ahead = ahead with { Sector = 2 }; t.Update(20, Session(focus, ahead));
        focus = focus with { Sector = 2 }; t.Update(23, Session(focus, ahead));
        Assert.Equal(2, t.Ahead!.Split); Assert.Equal(3, t.Ahead.Gap!.Value, 3); Assert.Equal(2, t.Ahead.Previous!.Value, 3);
        // Linha: sem gap.
        ahead = ahead with { LapsCompleted = 2, Sector = 0 }; t.Update(30, Session(focus, ahead));
        focus = focus with { LapsCompleted = 2, Sector = 0 }; t.Update(34, Session(focus, ahead));
        Assert.Null(t.Ahead); Assert.Null(t.Behind); Assert.Null(t.Nearest(34));
        // Volta 2: no S1 a linha 2 e o gap do S2 da volta anterior.
        ahead = ahead with { Sector = 1 }; t.Update(40, Session(focus, ahead));
        focus = focus with { Sector = 1 }; t.Update(45, Session(focus, ahead));
        Assert.Equal(1, t.Ahead!.Split); Assert.Equal(5, t.Ahead.Gap!.Value, 3); Assert.Equal(3, t.Ahead.Previous!.Value, 3);
    }

    [Fact]
    public void Previous_gap_is_dropped_when_the_nearest_car_changed()
    {
        var focus = Car(0, 3); var a1 = Car(1, 2); var a2 = Car(2, 1); var t = new GpxSplitTracker();
        t.Update(0, Session(focus, a1, a2));
        a1 = a1 with { Sector = 1 }; a2 = a2 with { Sector = 1 }; t.Update(10, Session(focus, a1, a2));
        focus = focus with { Sector = 1 }; t.Update(12, Session(focus, a1, a2));
        Assert.Equal(2, t.Ahead!.Gap!.Value, 3);
        focus = focus with { Position = 2 }; a1 = a1 with { Position = 3 }; a2 = a2 with { Sector = 2 };
        t.Update(20, Session(focus, a1, a2));
        a1 = a1 with { Sector = 2 }; t.Update(21, Session(focus, a1, a2));
        focus = focus with { Sector = 2 }; t.Update(22, Session(focus, a1, a2));
        Assert.Equal(a2.Index, t.Ahead!.Other.Index);
        Assert.Null(t.Ahead.Previous);
    }

    [Fact]
    public void Nearest_picks_the_smaller_gap_and_falls_back_to_the_only_side()
    {
        // Lider: so existe o de tras.
        var l = Car(0, 1); var lb = Car(2, 2); var tl = new GpxSplitTracker();
        tl.Update(0, Session(l, lb));
        tl.Update(1, Session(l with { Sector = 1 }, lb));
        Assert.Null(tl.Ahead);
        Assert.False(tl.Nearest(1.5)!.Ahead);
        tl.Update(3, Session(l with { Sector = 1 }, lb with { Sector = 1 }));
        Assert.Equal(2, tl.Nearest(3)!.Gap);

        // Carro de tras mais proximo que o da frente.
        var f = Car(0, 2); var a = Car(1, 1); var b = Car(3, 3); var t = new GpxSplitTracker();
        t.Update(0, Session(f, a, b));
        t.Update(1, Session(f, a with { Sector = 1 }, b));
        t.Update(2, Session(f with { Sector = 1 }, a with { Sector = 1 }, b));
        Assert.True(t.Nearest(2.1)!.Ahead);                                // de tras ainda correndo: fica a frente (gap 1)
        t.Update(2.5, Session(f with { Sector = 1 }, a with { Sector = 1 }, b with { Sector = 1 }));
        Assert.False(t.Nearest(2.5)!.Ahead);                               // 0,5 < 1
        Assert.Equal(.5, t.Nearest(2.5)!.Gap);

        // Carro de tras mais longe: fica o da frente.
        var t3 = new GpxSplitTracker();
        t3.Update(0, Session(f, a, b));
        t3.Update(1, Session(f, a with { Sector = 1 }, b));
        t3.Update(2, Session(f with { Sector = 1 }, a with { Sector = 1 }, b));
        t3.Update(5, Session(f with { Sector = 1 }, a with { Sector = 1 }, b with { Sector = 1 }));
        Assert.True(t3.Nearest(5)!.Ahead);

        // Ultimo: so o da frente.
        var tu = new GpxSplitTracker();
        tu.Update(0, Session(f, a));
        tu.Update(1, Session(f, a with { Sector = 1 }));
        tu.Update(2, Session(f with { Sector = 1 }, a with { Sector = 1 }));
        Assert.True(tu.Nearest(2)!.Ahead);
        Assert.Null(tu.Nearest(20));
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
