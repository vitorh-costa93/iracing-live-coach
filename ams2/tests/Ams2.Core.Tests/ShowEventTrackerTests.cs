using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

/// <summary>Eventos da Exibicao (gatilho "por evento"): os detectados aqui e os repassados dos outros trackers.</summary>
public class ShowEventTrackerTests
{
    static SessionSnapshot Base()
    {
        var sim = new Sim(1000, (500, 50), (300, 50));
        sim.Race[0] = RaceState.NotStarted; sim.Race[1] = RaceState.NotStarted;
        return sim.Snapshot();
    }

    static SessionSnapshot Map(SessionSnapshot s, Func<CarSnapshot, CarSnapshot> f) => s with { Cars = s.Cars.Select(f).ToArray() };

    [Fact]
    public void First_read_only_arms_then_changes_become_events()
    {
        var t = new ShowEventTracker();
        var s = Base();
        var first = t.Update(10, s);
        Assert.All(ShowEventTimesIds, id => Assert.Null(first.Last(id)));

        var leader = s.Cars.First(c => c.Position == 1);
        var lapped = Map(s, c => c.Index == leader.Index ? c with { LapsCompleted = c.LapsCompleted + 1 } : c);
        Assert.Equal(11, t.Update(11, lapped).LeaderLap);

        var best = Map(lapped, c => c with { BestLapTime = 80 });
        Assert.Equal(12, t.Update(12, best).BestLap);
        Assert.Equal(12, t.Update(13, best).BestLap);              // sem melhora, mantem o instante
        Assert.Equal(14, t.Update(14, Map(best, c => c.Index == 1 ? c with { BestLapTime = 79.5 } : c)).BestLap);

        var yellow = Map(best, c => c with { BestLapTime = 79.5 }) with { FlagColour = 6 };
        Assert.Equal(15, t.Update(15, yellow).Flag);
        Assert.Equal(15, t.Update(16, yellow with { FlagColour = 0 }).Flag);   // fim da bandeira nao e evento

        var green = Map(yellow, c => c with { RaceState = RaceState.Racing });
        Assert.Equal(17, t.Update(17, green).Start);
    }

    [Fact]
    public void Session_change_resets_and_other_trackers_are_forwarded()
    {
        var t = new ShowEventTracker();
        var s = Base();
        t.Update(1, s);
        t.Update(2, s with { FlagColour = 2 });
        var practice = t.Update(3, s with { Kind = SessionKind.Practice, FlagColour = 5 });
        Assert.Null(practice.Last("flag"));                                // nova sessao: so arma

        var bc = BroadcastState.Empty with { PlayerLapChangedT = 4, PlayerPositionChangedT = 5, LastPitEntryT = 6, PlayerStopStartT = 7, PlayerPitLaneStartT = 6.5 };
        var e = t.Update(8, s with { Kind = SessionKind.Practice, FlagColour = 5 }, bc);
        Assert.Equal((4.0, 5.0, 6.0, 7.0), (e.PlayerLap, e.Position, e.Pit, e.PlayerPit));
        Assert.Equal(4, e.Last("playerLap"));
        Assert.Null(e.Last("unknown"));
    }

    static readonly string[] ShowEventTimesIds =
        ["leaderLap", "playerLap", "bestLap", "position", "pit", "playerPit", "flag", "start", "sector", "finish", "sessionEnd"];
}
