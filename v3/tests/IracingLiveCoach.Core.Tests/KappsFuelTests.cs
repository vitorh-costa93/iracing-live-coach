using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class KappsFuelTests
{
    // Kapps prints paired with the 5 Hz SDK log (race3.csv, Road Atlanta 45-min race, 24/09/2026). F = fuel at the
    // player's crossing, rate = the row's consumption, LIR = Laps in Race, leader = class leader's CarIdxLap,
    // yamlLaps = the player's ResultsPositions LapsComplete at the crossing (one lap behind the telemetry).
    [Theory]
    //          F       rate    LIR    leader yamlLaps  Kapps LR  Kapps Refuel
    [InlineData(55.43, 2.1979, 38.34, 2, 0, 25.22, 28.62)] // fc_01, lap 2
    [InlineData(53.32, 2.1870, 37.87, 3, 1, 24.38, 25.92)] // fc_03, lap 3
    [InlineData(51.14, 2.1884, 38.21, 4, 2, 23.37, 28.16)] // fc_07, lap 4
    [InlineData(49.01, 2.1881, 38.33, 5, 3, 22.40, 28.09)] // fc_09, lap 5
    [InlineData(46.83, 2.1865, 38.41, 6, 4, 21.42, 28.01)] // fc_12, lap 6
    [InlineData(44.63, 2.1865, 38.39, 7, 5, 20.41, 28.03)] // fc_16, lap 7
    public void Laps_remain_and_refuel_match_the_kapps_prints(double fuel, double rate, double lir, int leaderLap, int yamlLaps, double kappsLr, double kappsRefuel)
    {
        int? left = KappsFuel.RaceLapsLeft(lir, leaderLap, yamlLaps, finished: false);
        var lr = KappsFuel.LapsRemain(fuel, rate);
        Assert.Equal(kappsLr, lr!.Value, 0.006);
        Assert.Equal(kappsRefuel, KappsFuel.Refuel(lr, rate, left)!.Value, 0.03);
    }

    [Fact]
    public void Laps_left_is_the_class_estimate_minus_the_laps_already_done()
    {
        Assert.Equal(36, KappsFuel.RaceLapsLeft(37.87, 3, 1, false));   // ceil 38 - 2
        Assert.Equal(9, KappsFuel.RaceLapsLeft(37.47, 30, 9, false));   // lapped player: the class leader's laps count
        Assert.Equal(0, KappsFuel.RaceLapsLeft(37.47, 30, 9, true));    // after the flag
        Assert.Null(KappsFuel.RaceLapsLeft(null, 3, 1, false));
    }

    [Fact]
    public void Refuel_adds_half_a_litre_only_from_one_litre_and_is_never_negative()
    {
        Assert.Equal(10 * 2.0 + 0.5, KappsFuel.Refuel(20, 2.0, 30)!.Value, 6);
        Assert.Equal(0.4, KappsFuel.Refuel(29.8, 2.0, 30)!.Value, 6);    // 0.4 L: no reserve
        Assert.Equal(0, KappsFuel.Refuel(35, 2.0, 30)!.Value, 6);
    }

    // fc_22 (lap 9): F 40.20 (5 Hz log: 40.19-40.20 at the crossing), laps left 39 - 8 = 31, black box +28.5 L
    // with the fuel box ticked; Kapps LR 18.27 / 18.39 / 18.23.
    [Theory]
    [InlineData(2.2003, true, 0.48)]   // Average: short by more than 2 L -> the black box counts
    [InlineData(2.1870, false, 0.90)]  // Qualify (stored ceil 2.187)
    [InlineData(2.2052, false, 0.33)]  // Last
    public void Fuel_at_end_matches_the_kapps_print_with_the_black_box(double rate, bool average, double kapps)
    {
        var lr = KappsFuel.LapsRemain(40.20, rate);
        Assert.Equal(kapps, KappsFuel.FuelAtEnd(lr, rate, 31, 28.5, average)!.Value, 0.015);
    }

    [Fact]
    public void Average_row_ignores_the_black_box_when_the_car_is_within_two_litres()
    {
        // e = (20 - 21) x 1.5 = -1.5 >= -2 -> the black box is ignored on Average, not on Qualify/Last.
        Assert.Equal(0, KappsFuel.FuelAtEnd(20, 1.5, 21, 10, averageRow: true)!.Value, 6);
        Assert.Equal(8.5, KappsFuel.FuelAtEnd(20, 1.5, 21, 10, averageRow: false)!.Value, 6);
        Assert.Equal(3, KappsFuel.FuelAtEnd(22, 1.5, 20, 10, averageRow: true)!.Value, 6);
    }

    [Fact]
    public void Average_is_the_trimmed_mean_of_the_last_five_valid_laps()
    {
        var c = new FuelConsumption { HistoryAverage = 2.187 };
        Assert.Equal(2.187, c.Average);
        c.Add(new FuelLap(2.130, true, 2), false);
        Assert.Equal(2.130, c.Average!.Value, 6);                       // 1 lap: plain mean
        c.Add(new FuelLap(2.180, true, 3), false);
        Assert.Equal(2.155, c.Average!.Value, 6);                       // 2 laps: plain mean
        c.Add(new FuelLap(2.128, false, 4), false);                     // invalid: Last empty, list unchanged
        Assert.Null(c.Last);
        c.Add(new FuelLap(2.180, true, 5), false);
        Assert.Equal(2.180, c.Last);
        Assert.Equal(2.180, c.Average!.Value, 6);                       // 3 laps: drop 2.130 and one 2.180
        foreach (var (u, n) in new[] { (2.208, 6), (2.205, 7), (2.203, 8) }) c.Add(new FuelLap(u, true, n), false);
        // last five: 2.180 2.180 2.208 2.205 2.203 -> without 2.180 and 2.208
        Assert.Equal((2.180 + 2.205 + 2.203) / 3, c.Average!.Value, 6);
        Assert.Equal(5, c.Laps.Count);
    }

    [Fact]
    public void Stored_values_are_rounded_up_to_the_thousandth()
    {
        Assert.Equal(2.187, FuelConsumption.Store(2.18612));
        Assert.Equal(2.2, FuelConsumption.Store(2.2));
    }

    [Fact]
    public void Qualify_is_confirmed_only_when_the_results_show_the_lap_as_the_fastest()
    {
        var c = new FuelConsumption();
        c.Add(new FuelLap(2.18612, true, 2), inQualifying: true);
        Assert.False(c.ConfirmQualify(lapsComplete: 1, fastestLap: 1)); // results not updated yet
        Assert.Null(c.Qualify);
        Assert.False(c.ConfirmQualify(2, 1));                            // lap 2 was not the fastest
        Assert.True(c.ConfirmQualify(2, 2));
        Assert.Equal(2.187, c.Qualify);
    }

    private static FuelLapTick Tick(double fuel, double pct, int lap, int flags = 0, bool pit = false, int surface = 3, double wear = 1, int state = 4, bool race = true) =>
        new(race, false, state, flags, fuel, pct, true, pit, surface, wear, lap);

    /// <summary>Drives one lap from pct 0.95 of the previous lap to the next line crossing.</summary>
    private static FuelLap? Lap(FuelLapTracker t, ref double fuel, ref int lap, double burn, Func<double, FuelLapTick>? mid = null)
    {
        t.Update(Tick(fuel, 0.02, lap));
        if (mid is not null) t.Update(mid(fuel));
        fuel -= burn;
        t.Update(Tick(fuel, 0.95, lap));
        lap++;
        return t.Update(Tick(fuel, 0.01, lap));
    }

    [Fact]
    public void Tracker_measures_laps_at_the_line_and_skips_lap_one()
    {
        var t = new FuelLapTracker();
        double fuel = 57.66; int lap = 1;
        t.Update(Tick(fuel, 0.95, 0));
        Assert.Null(t.Update(Tick(fuel, 0.001, 1)));                    // green crossing: lap 1 starts
        var l1 = Lap(t, ref fuel, ref lap, 2.233);
        Assert.False(l1!.Value.Valid);                                  // lap 1 never counts in a race
        var l2 = Lap(t, ref fuel, ref lap, 2.130);
        Assert.True(l2!.Value.Valid);
        Assert.Equal(2.130, l2.Value.Usage, 6);
    }

    [Fact]
    public void Tracker_invalidates_refuel_tyres_pit_stall_and_flags()
    {
        foreach (var dirty in new Func<double, FuelLapTick>[]
        {
            f => Tick(f + 5, 0.5, 3),                                   // fuel went up
            f => Tick(f, 0.5, 3, wear: 0.99),                           // tyres changed
            f => Tick(f, 0.5, 3, surface: 1),                           // track -> pit stall
            f => Tick(f, 0.5, 3, flags: 0x4000),                        // caution
            f => Tick(f, 0.5, 3, flags: 0x200),                         // one lap to green
            f => Tick(f, 0.5, 3, surface: -1),                          // not in the world
        })
        {
            var t = new FuelLapTracker();
            double fuel = 50; int lap = 2;
            t.Update(Tick(fuel, 0.95, lap)); t.Update(Tick(fuel, 0.01, ++lap));
            Assert.False(Lap(t, ref fuel, ref lap, 2.2, dirty)!.Value.Valid);
            Assert.True(Lap(t, ref fuel, ref lap, 2.2)!.Value.Valid); // the next lap counts again
        }
    }

    [Fact]
    public void Out_lap_started_on_pit_road_does_not_count_but_the_next_does()
    {
        var t = new FuelLapTracker();
        double fuel = 57.8; int lap = 10;
        t.Update(Tick(fuel, 0.95, lap, pit: true));
        t.Update(Tick(fuel, 0.01, ++lap, pit: true));                  // crossing the line in the pit lane
        fuel -= 2.3; t.Update(Tick(fuel, 0.95, lap));
        Assert.False(t.Update(Tick(fuel, 0.01, ++lap))!.Value.Valid);
        Assert.True(Lap(t, ref fuel, ref lap, 2.2)!.Value.Valid);
    }

    [Fact]
    public void Panel_latches_rows_and_fuel_at_end_follows_the_black_box_outside_the_pit_stall()
    {
        var p = new KappsFuelPanelState();
        p.SetLapsLeft(31, 38.57);
        p.Recompute(40.22, 2.2014, 2.187, 2.2063);
        var a = p.Snapshot(39.5, 28.5, inPitStall: false);
        Assert.Equal(18.27, a.Rows[0].LapsRemain!.Value, 0.005);      // latched at 40.22, not the live 39.5
        Assert.Equal(0.48, a.Rows[0].FuelAtEnd!.Value, 0.015);
        var more = p.Snapshot(39.4, 33.5, inPitStall: false);
        Assert.Equal(5.48, more.Rows[0].FuelAtEnd!.Value, 0.015);     // +5 L in the black box
        var box = p.Snapshot(39.4, 60, inPitStall: true);
        Assert.Equal(5.48, box.Rows[0].FuelAtEnd!.Value, 0.015);      // frozen in the pit stall
        Assert.Equal(38.57, box.LapsInRace);
    }
}
