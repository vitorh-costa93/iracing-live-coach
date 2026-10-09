using Ams2.Core;
using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class VictoryDetectorTests
{
    static CarSnapshot Car(int i, int pos, RaceState st, int laps, bool player) =>
        new(i, $"C{i}", "car", "cls", pos, pos, laps, laps + 1, 0, 0, 0, 0, 0, PitState.None, st, false, player);

    static SessionSnapshot Snap(RaceState me, int pos = 1, int laps = 5, SessionKind kind = SessionKind.Race, uint game = 2, bool inSession = true, string track = "T")
    {
        var cars = new List<CarSnapshot> { Car(0, pos, me, laps, true), Car(1, pos == 1 ? 2 : 1, RaceState.Racing, laps, false) };
        var wheels = Enumerable.Repeat(new WheelSnapshot(0, 0, 0, 0, ""), 4).ToList();
        var player = new PlayerSnapshot(0, new InputsSnapshot(0, 0, 0, 0, 0, 0, 0, 0), 3, 0, 0, 0, 50, 80, wheels, 0);
        return new SessionSnapshot(9, 0, inSession, kind, track, "", 4000, 5, null, 0, new WeatherSnapshot(0, 0, 0, 0, 0, 0, 0, 0), cars, player, game);
    }

    [Fact]
    public void Win_fires_once_when_leader_player_finishes()
    {
        var d = new VictoryDetector();
        Assert.False(d.Update(Snap(RaceState.Racing)));
        Assert.True(d.Update(Snap(RaceState.Finished, laps: 6)));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6)));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6)));
    }

    [Fact]
    public void Finishing_second_does_not_fire()
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing, pos: 2));
        Assert.False(d.Update(Snap(RaceState.Finished, pos: 2, laps: 6)));
    }

    [Theory]
    [InlineData(SessionKind.Qualify)]
    [InlineData(SessionKind.Practice)]
    [InlineData(SessionKind.TimeAttack)]
    public void Never_fires_outside_race(SessionKind kind)
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing, kind: kind));
        Assert.False(d.Update(Snap(RaceState.Finished, kind: kind, laps: 6)));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void Never_fires_outside_game_state_2(uint game)
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6, game: game)));
    }

    [Fact]
    public void Not_in_session_never_fires()
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6, inSession: false)));
    }

    [Fact]
    public void Connecting_to_an_already_finished_race_stays_silent()
    {
        var d = new VictoryDetector();
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6)));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6)));
    }

    [Fact]
    public void Replay_of_the_finished_race_after_menu_does_not_refire()
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing));
        Assert.True(d.Update(Snap(RaceState.Finished, laps: 6)));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6, game: 1)));   // menu: reseta
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6, game: 5)));   // replay
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6)));            // voltou sem ter visto Racing: mudo
    }

    [Fact]
    public void New_race_after_restart_fires_again()
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing));
        Assert.True(d.Update(Snap(RaceState.Finished, laps: 6)));
        d.Update(Snap(RaceState.NotStarted, laps: 0));   // reinicio: voltas caem, reseta e rearma
        d.Update(Snap(RaceState.Racing, laps: 1));
        Assert.True(d.Update(Snap(RaceState.Finished, laps: 6)));
    }

    [Fact]
    public void Session_change_resets_the_armed_state()
    {
        var d = new VictoryDetector();
        d.Update(Snap(RaceState.Racing, track: "A"));
        Assert.False(d.Update(Snap(RaceState.Finished, laps: 6, track: "B")));
    }
}
