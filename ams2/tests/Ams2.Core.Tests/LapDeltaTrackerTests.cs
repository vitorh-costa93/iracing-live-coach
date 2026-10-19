using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class LapDeltaTrackerTests
{
    [Fact]
    public void First_clean_lap_becomes_the_reference_and_the_next_lap_shows_a_signed_delta()
    {
        var sim = new Sim(3000, (2900, 50)) { PlayerIndex = 0, Kind = SessionKind.Practice, AutoLapTimes = true };
        var quali = new QualiLapTracker();
        var delta = new LapDeltaTracker();
        LapDeltaState state = LapDeltaState.Empty;
        const double dt = 1.0 / 30;
        void Run(double seconds, double? speed = null)
        {
            if (speed is { } v) sim.Speeds[0] = v;
            for (double t = 0; t < seconds; t += dt)
            {
                sim.Step(dt);
                var s = sim.Snapshot();
                var q = quali.Update(sim.Now, s);
                state = delta.Update(s, q);
            }
        }

        Run(2.5);                       // cruza a linha: comeca a volta medida
        Assert.Null(state.Delta);
        Run(60);                        // volta limpa a 50 m/s (60 s) fecha: vira a referencia
        Run(10);
        Assert.NotNull(state.BestLap);
        Assert.Equal(60, state.BestLap!.Value, 1);
        Assert.Equal(0, state.Delta!.Value, 1);   // mesmo ritmo = delta ~0

        Run(10, speed: 60);             // mais rapido que a referencia: delta negativo
        Assert.True(state.Delta < -0.5, $"delta {state.Delta}");
        Run(20, speed: 40);             // bem mais lento: delta passa a positivo
        Assert.True(state.Delta > 0, $"delta {state.Delta}");
    }
}
