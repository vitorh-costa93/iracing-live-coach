using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class SlowStopDetectorTests
{
    [Fact]
    public void Without_history_a_stop_over_the_limit_is_slow_and_loses_the_excess()
    {
        Assert.Null(SlowStopDetector.Evaluate(null, 5));
        Assert.Null(SlowStopDetector.Evaluate([], 5));
        Assert.Null(SlowStopDetector.Evaluate([4.9], 5));
        Assert.Null(SlowStopDetector.Evaluate([5.0], 5));
        var s = SlowStopDetector.Evaluate([11.1], 5)!;
        Assert.False(s.FromHistory);
        Assert.Equal(5, s.Reference);
        Assert.Equal(6.1, s.Lost, 6);
        Assert.Equal("-6.1s", SlowStopDetector.Format(s.Lost));
    }

    [Fact]
    public void With_history_the_reference_is_the_mean_of_the_last_three_previous_stops()
    {
        // Paradas anteriores 2.6 / 2.8 / 3.0 (a de 9.0 mais antiga fica fora da janela): referencia 2.8, perde 8.3 s.
        var s = SlowStopDetector.Evaluate([9.0, 2.6, 2.8, 3.0, 11.1], 5)!;
        Assert.True(s.FromHistory);
        Assert.Equal(2.8, s.Reference, 6);
        Assert.Equal(8.3, s.Lost, 6);
        Assert.Equal("-8.3s", SlowStopDetector.Format(s.Lost));
        // Abaixo do limite nunca e lenta, mesmo bem acima da media.
        Assert.Null(SlowStopDetector.Evaluate([2.5, 4.8], 5));
        // Acima do limite mas no ritmo normal do jogador (paradas longas de reabastecimento): nao e lenta.
        Assert.Null(SlowStopDetector.Evaluate([9.5, 9.0], 5));
        Assert.Null(SlowStopDetector.Evaluate([9.0, 9.02], 5));
        Assert.NotNull(SlowStopDetector.Evaluate([9.0, 9.4], 5));
    }

    [Fact]
    public void Broadcast_tracker_keeps_the_player_stop_history_and_resets_with_the_session()
    {
        var sim = new Sim(5000, (3000, 50), (2900, 50)) { PlayerIndex = 0 };
        var t = new BroadcastTracker();
        void Run(double secs) { for (int i = 0; i < secs * 10; i++) { sim.Step(0.1); t.Update(sim.Now, sim.Snapshot()); } }
        Run(1);
        Assert.Empty(t.State.PlayerStops);
        foreach (double stop in new[] { 3.0, 8.0 })
        {
            sim.Pit[0] = PitState.DrivingIntoPits; Run(1);
            sim.Pit[0] = PitState.InPit; Run(stop);
            Assert.Equal(stop == 3.0 ? 0 : 1, t.State.PlayerStops.Count);   // parada em curso nao entra no historico
            sim.Pit[0] = PitState.DrivingOutOfPits; Run(1);
            sim.Pit[0] = PitState.None; Run(1);
        }
        Assert.Equal(2, t.State.PlayerStops.Count);
        Assert.Equal(3.0, t.State.PlayerStops[0], 0.25);
        Assert.Equal(8.0, t.State.PlayerStops[1], 0.25);
        var slow = SlowStopDetector.Evaluate(t.State.PlayerStops, 5)!;
        Assert.Equal(5.0, slow.Lost, 0.3);
        t.Reset();
        Assert.Empty(t.State.PlayerStops);
    }
}
