using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class GapAndRelativeTests
{
    const double Len = 5000;

    [Fact]
    public void Gap_matches_distance_over_speed_for_cars_ahead_and_behind()
    {
        // todos a 50 m/s: carro 50 m à frente => +1,0 s; carro 100 m atrás => -2,0 s
        var sim = new Sim(Len, (2000, 50), (2050, 50), (1900, 50)) { PlayerIndex = 0 };
        var tracker = new GapTracker();
        for (int i = 0; i < 600; i++) { sim.Step(1.0 / 60); tracker.Update(sim.Now, Len, sim.Snapshot().Cars); }

        var rows = RelativeBuilder.Build(sim.Snapshot(), tracker, sim.Now);
        Assert.Equal(1.0, rows.Single(r => r.Car.Index == 1).GapSeconds!.Value, 0.05);
        Assert.Equal(-2.0, rows.Single(r => r.Car.Index == 2).GapSeconds!.Value, 0.05);
    }

    [Fact]
    public void Gap_with_a_slower_neighbour_is_measured_at_the_passage_not_by_instant_speed()
    {
        var sim = new Sim(Len, (1000, 60), (1100, 55)) { PlayerIndex = 0 };
        var tracker = new GapTracker();
        for (int i = 0; i < 300; i++) { sim.Step(1.0 / 60); tracker.Update(sim.Now, Len, sim.Snapshot().Cars); }
        var row = RelativeBuilder.Build(sim.Snapshot(), tracker, sim.Now).Single(r => r.Car.Index == 1);
        double dist = row.DistanceMeters;
        Assert.InRange(row.GapSeconds!.Value, dist / 60 - 0.1, dist / 55 + 0.1);
    }

    [Fact]
    public void Relative_orders_neighbours_across_the_start_finish_line()
    {
        // jogador antes da linha; carros 1 e 3 já passaram dela (à frente), carro 2 está atrás
        var sim = new Sim(Len, (4950, 50), (30, 50), (4800, 50), (60, 50)) { PlayerIndex = 0 };
        var rows = RelativeBuilder.Build(sim.Snapshot(), new GapTracker(), 0, 2, 2);
        Assert.Equal([3, 1, 0, 2], rows.Select(r => r.Car.Index).ToArray());
        Assert.True(rows[0].DistanceMeters > rows[1].DistanceMeters);
        Assert.True(rows[3].DistanceMeters < 0);
    }

    [Fact]
    public void Lapped_and_lap_ahead_cars_are_flagged()
    {
        var sim = new Sim(Len, (2 * Len + 1000, 50), (Len + 1100, 50), (3 * Len + 900, 50)) { PlayerIndex = 0 };
        var rows = RelativeBuilder.Build(sim.Snapshot(), new GapTracker(), 0);
        Assert.True(rows.Single(r => r.Car.Index == 1).Lapped);
        Assert.True(rows.Single(r => r.Car.Index == 2).LapAhead);
    }

    [Fact]
    public void Relative_is_stable_in_a_24_car_race_over_several_laps()
    {
        var cars = Enumerable.Range(0, 24).Select(i => (startDist: i * 200.0, speed: 60.0 + (i % 5) * 0.4)).ToArray();
        var sim = new Sim(Len, cars) { PlayerIndex = 10 };
        var tracker = new GapTracker();
        int samples = 0;
        double maxAhead = 0;
        for (int step = 0; step < 60 * 60 * 5; step++) // 5 min a 60 Hz
        {
            sim.Step(1.0 / 60);
            var s = sim.Snapshot();
            tracker.Update(sim.Now, Len, s.Cars);
            if (sim.Now < 120 || step % 30 != 0) continue; // depois de ~1,5 volta tudo deve ter gap
            var rows = RelativeBuilder.Build(s, tracker, sim.Now);
            Assert.Equal(9, rows.Count);
            var ahead = rows.TakeWhile(r => !r.IsPlayer).ToList();
            var behind = rows.SkipWhile(r => !r.IsPlayer).Skip(1).ToList();
            Assert.All(rows.Where(r => !r.IsPlayer), r => Assert.True(r.GapSeconds.HasValue, $"t={sim.Now:F1} car {r.Car.Index} dist {r.DistanceMeters:F1} me {s.PlayerCar!.LapDistance:F1} spd {r.Car.SpeedMps}"));
            Assert.All(ahead, r => Assert.True(r.GapSeconds >= 0));
            Assert.All(behind, r => Assert.True(r.GapSeconds <= 0));
            Assert.True(ahead.Zip(ahead.Skip(1)).All(p => p.First.GapSeconds >= p.Second.GapSeconds - 0.05));
            maxAhead = Math.Max(maxAhead, ahead.Max(r => r.GapSeconds!.Value));
            samples++;
        }
        Assert.True(samples > 100);
        Assert.True(maxAhead < 40);
    }

    [Fact]
    public void Gap_is_null_without_data_and_garage_cars_are_excluded()
    {
        var s = new Sim(Len, (1000, 50), (1100, 50)).Snapshot();
        Assert.Null(RelativeBuilder.Build(s, new GapTracker(), 0).Single(r => r.Car.Index == 1).GapSeconds);

        var inGarage = s with { Cars = s.Cars.Select(c => c.Index == 1 ? c with { PitState = PitState.InGarage } : c).ToList() };
        Assert.Single(RelativeBuilder.Build(inGarage, new GapTracker(), 0));
    }
}

public class FuelTrackerTests
{
    static SessionSnapshot At(SessionSnapshot s, int laps, double fuel, PitState pit = PitState.None)
    {
        var cars = s.Cars.Select(c => c.IsPlayer ? c with { LapsCompleted = laps, PitState = pit } : c).ToList();
        return s with { Cars = cars, Player = s.Player! with { FuelLiters = fuel } };
    }

    [Fact]
    public void Average_consumption_and_laps_remaining()
    {
        var f = new FuelTracker();
        var b = new Sim(5000, (0, 50)).Snapshot();
        f.Update(At(b, 0, 30));           // início: a primeira volta é parcial
        f.Update(At(b, 1, 27));           // volta parcial, descartada
        f.Update(At(b, 2, 24.5));         // 2,5 L
        var e = f.Update(At(b, 3, 21.9)); // 2,6 L
        Assert.Equal(2.55, e.PerLapAverage!.Value, 3);
        Assert.Equal(21.9 / 2.55, e.LapsRemainingOnFuel!.Value, 2);
        Assert.Equal(17, e.LapsToFinish); // 20 voltas de evento - 3
        Assert.Equal(17 * 2.55 - 21.9, e.LitersToAdd!.Value, 2);
    }

    [Fact]
    public void Pit_lap_and_refuelling_do_not_pollute_the_average()
    {
        var f = new FuelTracker();
        var b = new Sim(5000, (0, 50)).Snapshot();
        f.Update(At(b, 0, 60)); f.Update(At(b, 1, 57)); f.Update(At(b, 2, 54.5));
        f.Update(At(b, 2, 50, PitState.InPit));
        f.Update(At(b, 2, 70, PitState.InPit));  // reabasteceu
        f.Update(At(b, 2, 70));
        var e = f.Update(At(b, 3, 67.5));        // volta com pit: descartada
        Assert.Equal(2.5, e.PerLapAverage!.Value, 3);
        var e2 = f.Update(At(b, 4, 65.0));       // volta limpa: 2,5
        Assert.Equal(2.5, e2.PerLapAverage!.Value, 3);
    }

    [Fact]
    public void No_estimate_before_a_clean_lap()
    {
        var e = new FuelTracker().Update(At(new Sim(5000, (0, 50)).Snapshot(), 0, 60));
        Assert.Null(e.PerLapAverage);
        Assert.Null(e.LitersToAdd);
        Assert.Equal(60, e.LitersLeft);
    }
}
