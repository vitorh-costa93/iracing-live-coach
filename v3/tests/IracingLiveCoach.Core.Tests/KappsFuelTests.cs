using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class KappsFuelTests
{
    // Synchronized Kapps prints + SDK, 24/09/2026 (Road Atlanta, 45-min race). F = fuel at the player's crossing.
    [Theory]
    //          lc  F       LIR    rate     Kapps LR  Kapps Refuel
    [InlineData(1, 55.43, 38.34, 2.1979, 25.22, 28.62)]
    [InlineData(2, 53.30, 37.87, 2.1862, 24.38, 25.92)]
    [InlineData(3, 51.12, 38.21, 2.1875, 23.37, 28.16)]
    [InlineData(4, 48.99, 38.33, 2.1871, 22.40, 28.09)]
    [InlineData(4, 48.99, 38.33, 2.1861, 22.41, 28.04)] // Qualify row
    public void Laps_remain_and_refuel_match_the_kapps_prints(int lc, double fuel, double lir, double rate, double kappsLr, double kappsRefuel)
    {
        var row = KappsFuel.Row("Average", rate, fuel, KappsFuel.LapsToGo(lir, lc), plannedAdd: 0);
        Assert.Equal(kappsLr, row.LapsRemain!.Value, 1);
        Assert.InRange(row.Refuel!.Value, kappsRefuel - 0.02, kappsRefuel + 0.02);
    }

    [Fact]
    public void Fuel_at_end_is_what_is_left_after_the_planned_stop_and_follows_the_black_box()
    {
        // lap 2 print: black box +18.5 L -> 55.43 + 18.5 - 84.05 < 0 -> Kapps 0.00.
        var togo = KappsFuel.LapsToGo(38.34, 1);
        Assert.Equal(0, KappsFuel.Row("Average", 2.1979, 55.43, togo, 18.5).FuelAtEnd);
        // Enough planned fuel: the surplus; one litre more in the black box = one litre more at the end.
        var a = KappsFuel.Row("Average", 2.1979, 55.43, togo, 40).FuelAtEnd!.Value;
        var b = KappsFuel.Row("Average", 2.1979, 55.43, togo, 41).FuelAtEnd!.Value;
        Assert.Equal(55.43 + 40 - 2.1979 * togo!.Value, a, 6);
        Assert.Equal(1.0, b - a, 6);
        // Refuel does not depend on the black box (Kapps).
        Assert.Equal(KappsFuel.Row("A", 2.1979, 55.43, togo, 0).Refuel, KappsFuel.Row("A", 2.1979, 55.43, togo, 30).Refuel);
    }

    [Fact]
    public void Values_are_frozen_for_the_lap_except_fuel_level_and_the_black_box()
    {
        var latch = new KappsFuelLatch();
        var p1 = latch.Update(2, 53.30, 37.87, 2.1862, 2.1866, 2.13, 23);
        var p2 = latch.Update(2, 52.21, 38.10, 2.2500, 2.1866, 2.20, 23); // later in the same lap
        Assert.Equal(p1.Rows[0].LapsRemain, p2.Rows[0].LapsRemain);
        Assert.Equal(p1.Rows[0].Refuel, p2.Rows[0].Refuel);
        Assert.Equal(52.21, p2.FuelLevel);
        var p3 = latch.Update(2, 52.20, 38.10, 2.25, 2.1866, 2.2, 90);   // driver raises the black box
        Assert.True(p3.Rows[0].FuelAtEnd > p2.Rows[0].FuelAtEnd);
        var p4 = latch.Update(3, 51.12, 38.21, 2.1875, 2.1866, 2.18, 23); // next lap: recomputed
        Assert.NotEqual(p1.Rows[0].LapsRemain, p4.Rows[0].LapsRemain);
    }

    [Fact]
    public void Unknown_rate_or_race_length_gives_no_invented_numbers()
    {
        var r = KappsFuel.Row("Last", null, 50, 30, 0);
        Assert.Null(r.LapsRemain); Assert.Null(r.Refuel); Assert.Null(r.FuelAtEnd);
        var n = KappsFuel.Row("Average", 2.2, 50, null, 0);
        Assert.NotNull(n.LapsRemain); Assert.Null(n.Refuel);
    }
}
