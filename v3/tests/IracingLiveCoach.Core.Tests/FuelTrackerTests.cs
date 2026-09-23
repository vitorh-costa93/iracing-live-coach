using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class FuelTrackerTests
{
    /// <summary>Drives one full lap: a few mid-lap ticks, then the crossing.</summary>
    private static bool Lap(FuelTracker t, ref double fuel, ref int lap, double burn, bool dirtyMidLap = false, double lapTime = 80)
    {
        t.Update(fuel - burn / 2, lap, dirtyMidLap, lapTime);
        fuel -= burn;
        lap++;
        return t.Update(fuel, lap, false, lapTime);
    }

    private static FuelTracker AtLine(ref double fuel, ref int lap)
    {
        var t = new FuelTracker();
        t.Update(fuel, lap, false, 0);          // attach mid-lap
        fuel -= 1.0; lap++;
        t.Update(fuel, lap, false, 0);          // first crossing: baseline only
        return t;
    }

    [Fact]
    public void First_crossing_is_only_a_baseline_and_full_laps_are_averaged()
    {
        double fuel = 50; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        Assert.Null(t.AverageFuel);
        Lap(t, ref fuel, ref lap, 2.0);
        Lap(t, ref fuel, ref lap, 2.2);
        Assert.Equal(2.1, t.AverageFuel!.Value, 6);
        Assert.Equal(2.2, t.MaxFuel!.Value, 6);
        Assert.Equal(80, t.AverageLapTime!.Value, 6);
    }

    [Fact]
    public void Pace_caution_and_pit_laps_are_excluded_but_still_reported_as_last_lap()
    {
        double fuel = 50; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        Assert.False(Lap(t, ref fuel, ref lap, 0.8, dirtyMidLap: true)); // formation / safety car
        Assert.Equal(0.8, t.LastLapUsed!.Value, 6);
        Assert.True(t.LastLapDirty);
        Assert.Null(t.AverageFuel);
        Assert.True(Lap(t, ref fuel, ref lap, 2.1));
        Assert.Equal(2.1, t.AverageFuel!.Value, 6);
    }

    [Fact]
    public void A_refuel_during_the_lap_makes_it_dirty()
    {
        double fuel = 20; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        t.Update(fuel - 1, lap, false, 80);
        t.Update(fuel + 25, lap, false, 80);    // fuel added in the pits
        fuel += 24; lap++;
        Assert.False(t.Update(fuel, lap, false, 80));
        Assert.Null(t.AverageFuel);
    }

    [Fact]
    public void A_suspiciously_low_lap_is_rejected_once_there_is_a_reference()
    {
        double fuel = 50; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        Lap(t, ref fuel, ref lap, 2.0);
        Lap(t, ref fuel, ref lap, 2.0);
        Assert.False(Lap(t, ref fuel, ref lap, 0.5));
        Assert.Equal(2.0, t.AverageFuel!.Value, 6);
    }

    [Fact]
    public void Only_the_last_five_clean_laps_count()
    {
        double fuel = 60; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        foreach (var burn in new[] { 3.0, 2.0, 2.0, 2.0, 2.0, 2.0 }) Lap(t, ref fuel, ref lap, burn);
        Assert.Equal(2.0, t.AverageFuel!.Value, 6);
    }

    [Fact]
    public void Skipped_crossings_do_not_count_as_one_lap()
    {
        double fuel = 50; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        fuel -= 4; lap += 2;                     // tow / reset jumped two laps
        Assert.False(t.Update(fuel, lap, false, 80));
        Assert.Null(t.AverageFuel);
    }

    [Fact]
    public void Seeded_history_is_used_until_a_real_lap_exists()
    {
        double fuel = 50; int lap = 0;
        var t = AtLine(ref fuel, ref lap);
        t.Seed(1.72, 78);
        Assert.Equal(1.72, t.AverageFuel!.Value, 6);
        Assert.True(t.AverageIsFromHistory);
        Lap(t, ref fuel, ref lap, 1.80);
        Assert.Equal(1.80, t.AverageFuel!.Value, 6);
        Assert.False(t.AverageIsFromHistory);
    }

    [Fact]
    public void History_store_round_trips_per_car_and_track()
    {
        var path = Path.Combine(Path.GetTempPath(), "lc-fuel-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new FuelHistoryStore(path).Put(71, 14, 1.72, 78.5);
            var back = new FuelHistoryStore(path).Get(71, 14);
            Assert.Equal(new FuelHistoryEntry(1.72, 78.5), back);
            Assert.Null(new FuelHistoryStore(path).Get(71, 15));
        }
        finally { File.Delete(path); }
    }
}
