using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

/// <summary>Drives the engineer with synthetic laps of 90 s (10 samples per lap, one every 9 s).</summary>
internal sealed class EngSim
{
    public readonly RaceEngineer E = new();
    public readonly List<CoachMessage> Msgs = new();
    public double T = 1000;
    public int Lap = 1;
    public double Fuel;
    public string Session = "Race";
    public string Lang = "en";
    public int TotalLaps = -1;
    public EngineerData Data = new()
    {
        SessionState = 4, Position = 5, ClassPosition = 5, Incidents = 0, TrackTempC = 30, Precipitation = 0,
        EngineWarnings = 0, WaterTempC = 90, OilTempC = 100,
    };

    public EngSim(double fuel) { Fuel = fuel; }

    public IReadOnlyList<CoachMessage> Step(double pct, bool pit = false, double speed = 50, Func<EngineerData, EngineerData>? mod = null)
    {
        var d = Data with { FuelLevelL = Fuel };
        if (TotalLaps > 0) d = d with { SessionLapsRemain = TotalLaps - Lap + 1 };
        if (mod != null) d = mod(d);
        var res = E.Update(new CoachSample(T, Lap, pct, speed, 0, 1, 0, 4, 7000, pit, true, d), Session, Lang).ToList();
        Msgs.AddRange(res);
        return res;
    }

    /// <summary>One lap burning <paramref name="use"/> liters; pitFrom = first step on pit road (-1 none);
    /// refuel = liters added during the pit steps.</summary>
    public void DriveLap(double use, int pitFrom = -1, double refuel = 0, Func<int, EngineerData, EngineerData>? perStep = null)
    {
        double f0 = Fuel;
        for (int i = 0; i < 10; i++)
        {
            bool pit = pitFrom >= 0 && i >= pitFrom;
            Fuel = f0 - use * i / 10.0 + (pit && i == 9 ? refuel : 0);
            int step = i;
            Step(i / 10.0, pit, pit ? 20 : 50, perStep == null ? null : d => perStep(step, d));
            T += 9;
        }
        Fuel = f0 - use + refuel;
        Lap++;
    }

    public List<CoachMessage> Keys(string prefix) => Msgs.Where(m => m.Key.StartsWith(prefix)).ToList();
    public int Count(string key) => Msgs.Count(m => m.Key == key);

    /// <summary>Single-sample helper for flag/state tests (does not advance laps).</summary>
    public IReadOnlyList<CoachMessage> Tick(Func<EngineerData, EngineerData> mod, double dt = 1, double speed = 50, bool pit = false)
    {
        T += dt;
        return Step(0.5, pit, speed, mod);
    }
}

public class RaceEngineerTests
{
    // ------------------------------------------------------------ fuel arithmetic

    [Fact]
    public void Fuel_average_ignores_pit_and_yellow_laps_and_uses_last_five()
    {
        var sim = new EngSim(100);
        sim.DriveLap(2.0); sim.DriveLap(2.0); sim.DriveLap(2.0);
        sim.Step(0);
        Assert.Equal(2.0, sim.E.Status.AvgPerLap, 6);

        sim.DriveLap(2.0, pitFrom: 8, refuel: 20); // pit lane touched + refuel: ignored
        sim.Step(0);
        Assert.Equal(2.0, sim.E.Status.AvgPerLap, 6);

        sim.DriveLap(1.0, perStep: (i, d) => d with { SessionFlags = 0x8 }); // yellow lap, slow: ignored
        sim.Step(0);
        Assert.Equal(2.0, sim.E.Status.AvgPerLap, 6);

        // Laps 2,2,3,3,3 -> window of the last five valid laps.
        sim.DriveLap(2.0); sim.DriveLap(3.0); sim.DriveLap(3.0); sim.DriveLap(3.0);
        sim.Step(0);
        Assert.Equal((2.0 + 2.0 + 3.0 + 3.0 + 3.0) / 5.0, sim.E.Status.AvgPerLap, 6);
    }

    [Fact]
    public void Fuel_average_needs_two_laps()
    {
        var sim = new EngSim(50);
        sim.DriveLap(2.0); sim.Step(0);
        Assert.True(double.IsNaN(sim.E.Status.AvgPerLap));
        sim.DriveLap(2.0); sim.Step(0);
        Assert.Equal(2.0, sim.E.Status.AvgPerLap, 6);
    }

    [Theory]
    [InlineData(2.0, 2.0, -1, 14, 3)]   // short 2.0 + half lap (1.0) = 3.0 -> 3 L
    [InlineData(2.0, 2.4, -1, 14, 4)]   // 2.0 + 1.2 = 3.2 -> 4 L
    [InlineData(2.0, 2.0, 16, 14, 2)]   // tank only has 2 L of room
    [InlineData(0.2, 2.0, -1, 5, 2)]    // 0.2 + 1.0 = 1.2 -> 2 L
    [InlineData(-3.0, 2.0, -1, 5, 0)]   // enough: never negative
    public void AddFuel_includes_half_lap_reserve_rounds_up_and_respects_capacity(double shortL, double avg, double cap, double fuel, int expected) =>
        Assert.Equal(expected, EngineerMath.AddFuelL(shortL, avg, cap, fuel));

    [Fact]
    public void Distance_to_finish_from_laps_and_from_time()
    {
        Assert.Equal(7.75, EngineerMath.DistanceToFinishLaps(8, -1, 0.25, 90)!.Value, 9);
        Assert.Equal(0.5, EngineerMath.DistanceToFinishLaps(1, -1, 0.5, 90)!.Value, 9);
        Assert.Equal(0.0, EngineerMath.DistanceToFinishLaps(0, -1, 0.5, 90)!.Value, 9);
        // 600 s left, 90 s laps, halfway through the lap: crossings at 45+90k, last before time out k=6, flag one lap later.
        Assert.Equal(7.5, EngineerMath.DistanceToFinishLaps(32767, 600, 0.5, 90)!.Value, 9);
        Assert.Equal(8, EngineerMath.CrossingsToFinish(7.5, 0.5));
        // Clock ends before the next line: the flag is that line.
        Assert.Equal(0.5, EngineerMath.DistanceToFinishLaps(32767, 30, 0.5, 90)!.Value, 9);
        Assert.Null(EngineerMath.DistanceToFinishLaps(32767, 1e9, 0.5, 90));
        Assert.Null(EngineerMath.DistanceToFinishLaps(32767, 600, 0.5, double.NaN));
        Assert.Equal(9, EngineerMath.LastPitLap(3, 0.5, 6.8)); // 6.8 + 0.5 = 7.3 -> floor 7 -> lap 3 + 7 - 1
    }

    [Fact]
    public void Fuel_to_finish_says_how_much_to_add_once()
    {
        var sim = new EngSim(18) { TotalLaps = 10 };
        sim.DriveLap(2.0); sim.DriveLap(2.0);
        sim.DriveLap(2.0); sim.DriveLap(2.0); sim.DriveLap(2.0); // laps 3..5

        var fin = sim.Msgs.Where(m => m.Key == "fuel.finish").ToList();
        Assert.Single(fin);
        // Start of lap 3: 14 L, 8 laps to go x 2.0 = 16 L -> 2.0 short, +1.0 reserve -> add 3.
        Assert.Equal("2.0", fin[0].Args["short"]);
        Assert.Equal("3", fin[0].Args["add"]);
        Assert.Equal("To finish you are 2.0 liters short. Add 3 liters at the pit.", fin[0].Text);
        var win = sim.Msgs.Single(m => m.Key == "pit.window");
        Assert.Equal("7", win.Args["n"]);   // 14 L / 2.0
        Assert.Equal("9", win.Args["lap"]); // lap 3 + 7 - 1
        Assert.True(win.AllowStopped);
        Assert.Equal(0, sim.Count("fuel.enough"));
    }

    [Fact]
    public void Fuel_enough_to_finish_is_confirmed_once_with_margin()
    {
        var sim = new EngSim(40) { TotalLaps = 10 };
        for (int i = 0; i < 6; i++) sim.DriveLap(2.0);
        Assert.Equal(1, sim.Count("fuel.enough"));
        Assert.Equal(0, sim.Count("fuel.finish"));
    }

    [Fact]
    public void Fuel_warnings_fire_once_per_threshold_on_the_exact_laps_left()
    {
        var sim = new EngSim(24);
        for (int lap = 1; lap <= 12; lap++) sim.DriveLap(2.0);
        var fuel = sim.Keys("fuel.").Select(m => m.Key + ":" + (m.Args.TryGetValue("n", out var n) ? n : "")).ToList();
        Assert.Equal(new[] { "fuel.laps:5", "fuel.laps:3", "fuel.critical:2", "fuel.empty:" }, fuel);
        Assert.Equal("About 5 laps of fuel left.", sim.Keys("fuel.laps")[0].Text);
        Assert.Equal("Fuel critical, less than 2 laps left.", sim.Msgs.Single(m => m.Key == "fuel.critical").Text);
        Assert.True(sim.Keys("fuel.").All(m => !m.Interrupt));
    }

    [Fact]
    public void Box_this_lap_when_fuel_only_reaches_the_next_line_once_per_lap()
    {
        var sim = new EngSim(24);
        for (int lap = 1; lap <= 12; lap++) sim.DriveLap(2.0);
        var box = sim.Keys("pit.box");
        // Lap 11 start: 4 L = 2.0 laps, minus the 1.0 to the line = 1.0 < 1.15; lap 10 still has 2.0.
        Assert.Equal(2, box.Count);
        Assert.All(box, m => Assert.True(m.AllowStopped));
        Assert.Equal("Box this lap.", box[0].Text);
    }

    [Fact]
    public void No_box_call_when_the_fuel_is_enough_to_the_flag()
    {
        // Last lap, 1.5 L at half distance with 2.0 L/lap: lapsLeft 0.75 < 1.15 but only 1.0 L is needed.
        var sim = new EngSim(6.5) { TotalLaps = 3 };
        sim.DriveLap(2.0); sim.DriveLap(2.0);
        sim.DriveLap(2.0);
        Assert.Equal(0, sim.Count("pit.box"));
        Assert.Equal(0, sim.Count("fuel.finish"));
    }

    // ------------------------------------------------------------ pit

    [Fact]
    public void Pit_exit_confirms_fuel_added_and_rearms_fuel_calls()
    {
        var sim = new EngSim(24);
        for (int lap = 1; lap <= 8; lap++) sim.DriveLap(2.0); // fuel.laps:5 fired
        Assert.Equal(1, sim.Count("fuel.laps"));
        sim.Fuel = 8;
        sim.Tick(d => d); // on track
        sim.Tick(d => d, dt: 1, speed: 20, pit: true);
        sim.Fuel = 8;
        sim.T += 10; sim.Step(0.9, true, 0);
        sim.Fuel = 28; // refuelled 20 L
        sim.T += 10; sim.Step(0.9, true, 0);
        var res = sim.Tick(d => d, dt: 2, speed: 25, pit: false);
        var m = Assert.Single(res.Where(x => x.Key == "pit.exit"));
        Assert.Equal("28.0", m.Args["to"]);
        Assert.Equal("20.0", m.Args["added"]);
        Assert.Equal("Out of the pits. Fuel 28.0 liters, 20.0 added.", m.Text);
        Assert.True(m.AllowStopped);

        // 28 L at 2.0/lap: the 5-laps warning is armed again once the tank runs down.
        for (int lap = 0; lap < 11; lap++) sim.DriveLap(2.0);
        Assert.True(sim.Count("fuel.laps") >= 2);
    }

    [Fact]
    public void Pit_exit_without_fuel_says_no_fuel_added_and_ignores_a_blip()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.Tick(d => d, speed: 20, pit: true);
        var blip = sim.Tick(d => d, dt: 1, speed: 20, pit: false); // 1 s on the pit road: no confirmation
        Assert.DoesNotContain(blip, m => m.Key.StartsWith("pit.exit"));
        sim.Tick(d => d, speed: 20, pit: true);
        sim.Tick(d => d, dt: 5, speed: 0, pit: true);
        var res = sim.Tick(d => d, dt: 2, speed: 20, pit: false);
        Assert.Contains(res, m => m.Key == "pit.exit.nofuel");
    }

    [Fact]
    public void Pit_limiter_reminder_only_when_it_is_off_and_published()
    {
        EngSim Enter(int warnings)
        {
            var sim = new EngSim(30);
            sim.Data = sim.Data with { EngineWarnings = warnings };
            sim.Tick(d => d);
            sim.Tick(d => d, speed: 30, pit: true);
            sim.Tick(d => d, dt: 2, speed: 30, pit: true);
            sim.Tick(d => d, dt: 1, speed: 30, pit: true);
            return sim;
        }
        Assert.Equal(1, Enter(0).Count("pit.limiter"));
        Assert.Equal(0, Enter(0x10).Count("pit.limiter")); // limiter already on
        Assert.Equal(0, Enter(-1).Count("pit.limiter"));   // channel not published
    }

    // ------------------------------------------------------------ flags

    [Fact]
    public void Flags_fire_only_on_the_bit_transition()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        for (int i = 0; i < 5; i++) sim.Tick(d => d with { SessionFlags = 0x8 }); // yellow held
        Assert.Equal(1, sim.Count("flag.yellow"));
        var y = sim.Msgs.Single(m => m.Key == "flag.yellow");
        Assert.True(y.Interrupt);
        Assert.Equal(CoachMessages.PriorityEngineerUrgent, y.Priority);

        for (int i = 0; i < 3; i++) sim.Tick(d => d with { SessionFlags = 0 });
        Assert.Equal(1, sim.Count("flag.green"));

        sim.Tick(d => d with { SessionFlags = 0x4000 }); // caution: a new yellow
        Assert.Equal(2, sim.Count("flag.yellow"));
    }

    [Theory]
    [InlineData(0x20, "flag.blue", true)]
    [InlineData(0x2, "flag.white", false)]
    [InlineData(0x10000, "flag.black", true)]
    [InlineData(0x20000, "flag.black", true)]
    [InlineData(0x10, "flag.red", true)]
    [InlineData(0x100000, "flag.repair", true)]
    public void Each_flag_has_its_call(int bit, string key, bool interrupt)
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.Tick(d => d with { SessionFlags = bit });
        sim.Tick(d => d with { SessionFlags = bit });
        var m = Assert.Single(sim.Msgs.Where(x => x.Key == key));
        Assert.Equal(interrupt, m.Interrupt);
    }

    [Fact]
    public void Checkered_flag_reports_the_finishing_position_and_blue_flag_has_a_cooldown()
    {
        var sim = new EngSim(30);
        sim.Data = sim.Data with { ClassPosition = 3, Position = 7 };
        sim.Tick(d => d);
        sim.Tick(d => d with { SessionFlags = 0x1 });
        var m = Assert.Single(sim.Msgs.Where(x => x.Key == "flag.checkered.pos"));
        Assert.Equal("Checkered flag, finished P3.", m.Text);

        var s2 = new EngSim(30);
        s2.Tick(d => d);
        s2.Tick(d => d with { SessionFlags = 0x20 });
        s2.Tick(d => d with { SessionFlags = 0 });
        s2.Tick(d => d with { SessionFlags = 0x20 }); // flickers back 1 s later
        Assert.Equal(1, s2.Count("flag.blue"));
        s2.Tick(d => d with { SessionFlags = 0 }, dt: 30);
        s2.Tick(d => d with { SessionFlags = 0x20 });
        Assert.Equal(2, s2.Count("flag.blue"));
    }

    [Fact]
    public void A_flag_already_showing_when_the_engineer_starts_is_not_announced()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d with { SessionFlags = 0x8 });
        sim.Tick(d => d with { SessionFlags = 0x8 });
        Assert.Empty(sim.Msgs);
    }

    [Fact]
    public void Race_start_is_called_once_on_the_green()
    {
        var sim = new EngSim(30);
        sim.Data = sim.Data with { SessionState = 3 };
        sim.Tick(d => d);
        sim.Tick(d => d);
        Assert.Empty(sim.Msgs);
        sim.Tick(d => d with { SessionState = 4 });
        sim.Tick(d => d with { SessionState = 4, SessionFlags = unchecked((int)0x80000000) });
        Assert.Equal(1, sim.Count("race.start"));
        Assert.Equal("Green green green, go go go.", sim.Msgs.Single().Text);
    }

    [Fact]
    public void Session_end_is_called_outside_races_only()
    {
        var p = new EngSim(30) { Session = "Practice" };
        p.Tick(d => d);
        p.Tick(d => d with { SessionState = 6 });
        Assert.Equal(1, p.Count("session.end"));

        var r = new EngSim(30);
        r.Tick(d => d);
        r.Tick(d => d with { SessionState = 6 });
        Assert.Equal(0, r.Count("session.end"));
    }

    // ------------------------------------------------------------ race

    [Fact]
    public void Position_change_is_announced_after_it_holds_and_not_when_it_flickers()
    {
        var sim = new EngSim(30);
        sim.Data = sim.Data with { SessionState = 3 };
        sim.Tick(d => d);
        sim.Tick(d => d with { SessionState = 4 }); // green
        sim.Data = sim.Data with { SessionState = 4 };
        sim.Msgs.Clear();
        sim.Tick(d => d with { SessionState = 4, ClassPosition = 4 }, dt: 3); // inside the 10 s start window
        sim.Tick(d => d with { ClassPosition = 4 }, dt: 4);
        Assert.Empty(sim.Msgs);

        sim.Tick(d => d with { ClassPosition = 3 }, dt: 20);   // flicker: back after 1 s
        sim.Tick(d => d with { ClassPosition = 4 }, dt: 1);
        sim.Tick(d => d with { ClassPosition = 4 }, dt: 5);
        Assert.Empty(sim.Msgs);

        sim.Tick(d => d with { ClassPosition = 3 }, dt: 1);
        sim.Tick(d => d with { ClassPosition = 3 }, dt: 3.5);
        var m = Assert.Single(sim.Msgs);
        Assert.Equal("race.gain", m.Key);
        Assert.Equal("Up to P3.", m.Text);

        sim.Tick(d => d with { ClassPosition = 5 }, dt: 1);
        sim.Tick(d => d with { ClassPosition = 5 }, dt: 3.5); // only 4.5 s after the last call: cooldown 10 s
        Assert.Single(sim.Msgs);
        sim.Tick(d => d with { ClassPosition = 5 }, dt: 20);
        sim.Tick(d => d with { ClassPosition = 6 }, dt: 1);
        sim.Tick(d => d with { ClassPosition = 6 }, dt: 3.5);
        Assert.Equal(2, sim.Msgs.Count);
        Assert.Equal("race.loss", sim.Msgs[1].Key);
    }

    [Fact]
    public void Attack_and_defend_need_a_held_gap_below_one_second_with_hysteresis_and_cooldown()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.T += 60;
        sim.Tick(d => d with { GapAheadSec = 0.8 });
        Assert.Empty(sim.Msgs);                                   // not held yet
        sim.Tick(d => d with { GapAheadSec = 0.8 }, dt: 2.5);
        var m = Assert.Single(sim.Msgs);
        Assert.Equal("race.attack", m.Key);
        Assert.Equal("Target ahead, 0.8 seconds. Attack.", m.Text);

        for (int i = 0; i < 10; i++) sim.Tick(d => d with { GapAheadSec = 0.7 }, dt: 3); // stays close: no repeat
        Assert.Single(sim.Msgs);

        sim.Tick(d => d with { GapAheadSec = 1.3 });              // between 1.0 and 1.6: still not re-armed
        sim.Tick(d => d with { GapAheadSec = 0.9 });
        sim.Tick(d => d with { GapAheadSec = 0.9 }, dt: 3);
        Assert.Single(sim.Msgs);

        sim.Tick(d => d with { GapAheadSec = 2.0 }, dt: 3);       // re-armed, but the cooldown (90 s) has not passed
        sim.Tick(d => d with { GapAheadSec = 0.9 });
        sim.Tick(d => d with { GapAheadSec = 0.9 }, dt: 3);
        Assert.Single(sim.Msgs);

        sim.Tick(d => d with { GapAheadSec = 2.0 }, dt: 80);
        sim.Tick(d => d with { GapAheadSec = 0.9 });
        sim.Tick(d => d with { GapAheadSec = 0.9 }, dt: 3);
        Assert.Equal(2, sim.Msgs.Count);

        // Behind
        sim.Tick(d => d with { GapBehindSec = 0.46 }, dt: 5);
        sim.Tick(d => d with { GapBehindSec = 0.46 }, dt: 3);
        var def = sim.Msgs.Last();
        Assert.Equal("race.defend", def.Key);
        Assert.Equal("Car behind, 0.5 seconds. Defend.", def.Text);
    }

    [Fact]
    public void Laps_to_go_are_called_at_milestones_only_once()
    {
        var sim = new EngSim(100) { TotalLaps = 12 };
        for (int lap = 1; lap <= 12; lap++) sim.DriveLap(1.0);
        var ns = sim.Keys("race.laps").Select(m => m.Args["n"]).ToList();
        Assert.Equal(new[] { "10", "5", "3", "2" }, ns);
        Assert.Equal("5 laps to go.", sim.Keys("race.laps")[1].Text);
    }

    // ------------------------------------------------------------ car / weather

    [Fact]
    public void Incidents_contact_and_heavy_contact()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.Tick(d => d with { Incidents = 1 });
        var c = Assert.Single(sim.Msgs);
        Assert.Equal("car.contact", c.Key);
        Assert.Equal("Contact, 1 incidents.", c.Text);
        Assert.False(c.Interrupt);

        sim.Tick(d => d with { Incidents = 1 }, dt: 30);          // no change: silent
        sim.Tick(d => d with { Incidents = 5 }, dt: 1);
        var h = sim.Msgs.Last();
        Assert.Equal("car.heavy", h.Key);
        Assert.True(h.Interrupt);
        Assert.Equal("5", h.Args["n"]);
    }

    [Fact]
    public void Water_and_oil_temperature_warn_once_until_they_cool_down()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        for (int i = 0; i < 5; i++) sim.Tick(d => d with { WaterTempC = 119, OilTempC = 155 }, dt: 2);
        Assert.Equal(1, sim.Count("car.water"));
        Assert.Equal(1, sim.Count("car.oil"));
        sim.Tick(d => d with { WaterTempC = 105, OilTempC = 135 }, dt: 100); // cooled below the clear levels
        sim.Tick(d => d with { WaterTempC = 119, OilTempC = 155 }, dt: 1);
        Assert.Equal(2, sim.Count("car.water"));
        Assert.Equal(2, sim.Count("car.oil"));

        var w = new EngSim(30);
        w.Tick(d => d);
        w.Tick(d => d with { EngineWarnings = 0x1 }); // the car's own water warning
        Assert.Equal(1, w.Count("car.water"));
    }

    [Fact]
    public void Track_temperature_and_rain_are_called_once()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.Tick(d => d with { TrackTempC = 33 });               // +3: below the 4 degree step
        Assert.Empty(sim.Msgs);
        sim.Tick(d => d with { TrackTempC = 35 }, dt: 5);
        var m = Assert.Single(sim.Msgs);
        Assert.Equal("Track temperature up to 35 degrees.", m.Text);
        sim.Tick(d => d with { TrackTempC = 36 }, dt: 5);
        sim.Tick(d => d with { Precipitation = 0.3 }, dt: 5);
        sim.Tick(d => d with { Precipitation = 0.3 }, dt: 5);
        Assert.Equal(1, sim.Count("weather.rain"));
    }

    // ------------------------------------------------------------ silence

    [Fact]
    public void Silent_outside_races_for_strategy_but_critical_fuel_still_warns()
    {
        var sim = new EngSim(24) { Session = "Practice" };
        for (int lap = 1; lap <= 12; lap++)
            sim.DriveLap(2.0, perStep: (i, d) => d with { SessionFlags = lap == 6 && i >= 5 ? 0x8 : 0, GapAheadSec = 0.5, GapBehindSec = 0.5, ClassPosition = 1 + lap });
        Assert.Equal(0, sim.Count("flag.yellow"));
        Assert.Equal(0, sim.Count("race.attack"));
        Assert.Equal(0, sim.Count("race.defend"));
        Assert.Equal(0, sim.Count("race.gain") + sim.Count("race.loss"));
        Assert.Equal(0, sim.Count("fuel.laps"));
        Assert.Equal(1, sim.Count("fuel.critical"));
        Assert.Equal(1, sim.Count("fuel.empty"));
    }

    [Fact]
    public void Silent_for_replay_spectating_and_when_disabled()
    {
        var sim = new EngSim(30);
        sim.Tick(d => d);
        sim.Tick(d => d with { NotDriving = true, SessionFlags = 0x8, Incidents = 9 });
        sim.Tick(d => d with { NotDriving = true, SessionFlags = 0x8, Incidents = 9 });
        Assert.Empty(sim.Msgs);
        // Back to driving: the flag that was already showing is a new baseline, not a transition.
        sim.Tick(d => d with { SessionFlags = 0x8, Incidents = 9 });
        sim.Tick(d => d with { SessionFlags = 0x8, Incidents = 9 });
        Assert.Empty(sim.Msgs);

        var off = new EngSim(30);
        off.E.Options.Enabled = false;
        off.Tick(d => d);
        off.Tick(d => d with { SessionFlags = 0x8, Incidents = 9 });
        Assert.Empty(off.Msgs);
    }

    [Fact]
    public void Category_toggles_silence_only_their_calls()
    {
        var sim = new EngSim(30);
        sim.E.Options.Flags = false;
        sim.E.Options.Fuel = false;
        sim.Tick(d => d);
        sim.Tick(d => d with { SessionFlags = 0x8, Incidents = 2 });
        Assert.DoesNotContain(sim.Msgs, m => m.Key.StartsWith("flag."));
        Assert.Contains(sim.Msgs, m => m.Key == "car.contact");

        var f = new EngSim(24);
        f.E.Options.Fuel = false;
        for (int lap = 1; lap <= 12; lap++) f.DriveLap(2.0);
        Assert.Empty(f.Keys("fuel."));
        Assert.NotEmpty(f.Keys("pit.box")); // pit calls are a separate family
    }

    [Fact]
    public void Calls_in_other_languages_use_localized_numbers()
    {
        var sim = new EngSim(18) { TotalLaps = 10, Lang = "pt-BR" };
        for (int i = 0; i < 3; i++) sim.DriveLap(2.0);
        var m = sim.Msgs.Single(x => x.Key == "fuel.finish");
        Assert.Equal("Para terminar faltam 2,0 litros. Adicione 3 litros no pit.", m.Text);
    }

    // ------------------------------------------------------------ phrases

    [Fact]
    public void Every_engineer_phrase_exists_in_all_languages_with_the_same_placeholders()
    {
        var keys = EngineerPhrases.Table["en"].Keys.ToList();
        Assert.True(keys.Count > 40);
        var ph = new Regex(@"\{(\w+)\}");
        foreach (var lang in CoachPhrases.Languages)
        {
            Assert.Equal(keys.Count, EngineerPhrases.Table[lang].Count);
            foreach (var k in keys)
            {
                Assert.True(EngineerPhrases.Table[lang].ContainsKey(k), $"{lang}:{k}");
                var txt = CoachPhrases.Render(lang, k);
                Assert.NotEqual(k, txt);
                if (lang != "en" && lang != "fr") Assert.NotEqual(CoachPhrases.Render("en", k), txt);
                var want = ph.Matches(EngineerPhrases.Table["en"][k]).Select(x => x.Value).OrderBy(x => x);
                var got = ph.Matches(EngineerPhrases.Table[lang][k]).Select(x => x.Value).OrderBy(x => x);
                Assert.Equal(want, got);
                Assert.True(CoachPhrases.HasKey(k));
            }
        }
    }

    // ------------------------------------------------------------ voice queries

    [Fact]
    public void Query_intents_include_summary_and_the_engineer_questions()
    {
        foreach (var i in new[] { "summary", "fuel", "gapahead", "gapbehind", "position", "laps", "pit" })
            Assert.Contains(i, CoachQuery.Intents);
    }

    private static CoachQueryState State(string lang, EngineerStatus? e) => new(lang, null, null, 0, null, null, e);

    [Fact]
    public void Query_fuel_gap_position_laps_and_pit_answers()
    {
        var st = new EngineerStatus(14.0, 2.0, 7.0, 8, 2.0, 3, false, 4, 0.84, 2.46);
        Assert.Equal("14.0 liters, about 7 laps. To finish, add 3 liters.", CoachQuery.Answer("fuel", State("en", st)));
        Assert.Equal("14.0 liters, about 7 laps. Enough to finish.",
            CoachQuery.Answer("fuel", State("en", st with { ShortL = -4, AddL = 0 })));
        Assert.Equal("14.0 liters, about 7 laps.",
            CoachQuery.Answer("fuel", State("en", st with { ShortL = double.NaN, AddL = -1 })));
        Assert.Equal("14.0 liters. Still measuring consumption.",
            CoachQuery.Answer("fuel", State("en", st with { LapsLeft = double.NaN })));
        Assert.Equal("No fuel data yet.", CoachQuery.Answer("fuel", State("en", null)));

        Assert.Equal("0.8 seconds to the car ahead.", CoachQuery.Answer("gapahead", State("en", st)));
        Assert.Equal("2.5 seconds to the car behind.", CoachQuery.Answer("gap behind", State("en", st)));
        Assert.Equal("No gap available.", CoachQuery.Answer("gapahead", State("en", st with { GapAheadSec = double.NaN })));
        Assert.Equal("You are P4.", CoachQuery.Answer("position", State("en", st)));
        Assert.Equal("Position unknown.", CoachQuery.Answer("position", State("en", EngineerStatus.Empty)));
        Assert.Equal("8 laps remaining.", CoachQuery.Answer("laps", State("en", st)));
        Assert.Equal("Race length unknown.", CoachQuery.Answer("laps", State("en", EngineerStatus.Empty)));
        Assert.Equal("No need to pit. Fuel lasts 7 laps.", CoachQuery.Answer("pit", State("en", st)));
        Assert.Equal("Yes, box this lap.", CoachQuery.Answer("pit", State("en", st with { BoxNow = true })));
        Assert.Equal("No pit information yet.", CoachQuery.Answer("pit", State("en", EngineerStatus.Empty)));

        // Answers exist in every language.
        foreach (var lang in CoachPhrases.Languages)
            foreach (var i in new[] { "fuel", "gapahead", "gapbehind", "position", "laps", "pit" })
                Assert.NotEqual(CoachPhrases.Render(lang, "answer.unknown"), CoachQuery.Answer(i, State(lang, st)));
        Assert.Equal("0,8 segundos para o carro da frente.", CoachQuery.Answer("gapahead", State("pt-BR", st)));
    }

    // ------------------------------------------------------------ scheduler / engine integration

    [Fact]
    public void Scheduler_speaks_pit_calls_while_stopped_but_holds_other_calls()
    {
        var s = new CoachMessageScheduler();
        var args = new Dictionary<string, string>();
        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "fuel.laps", args, 4, "fuel", TtlSec: 20));
        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "pit.box", args, 5, "box", AllowStopped: true, TtlSec: 10));
        CoachSample At(double t, double v, bool pit) => new(t, 1, 0.5, v, 0, 0, 0, 1, 1000, pit, true);

        Assert.Equal("box", s.Tick(At(10, 0, true), 3000)!.Text);       // stopped in the pit lane: only the pit call
        Assert.Null(s.Tick(At(20, 0, true), 3000));                      // fuel call still waiting
        Assert.Equal("fuel", s.Tick(At(21, 40, false), 3000)!.Text);     // on track and moving
    }

    [Fact]
    public void Scheduler_expires_engineer_calls_by_their_ttl_and_lets_urgent_ones_through_sooner()
    {
        var s = new CoachMessageScheduler();
        var args = new Dictionary<string, string>();
        CoachSample At(double t) => new(t, 1, 0.5, 40, 0, 1, 0, 4, 6000, false, true);
        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "a", args, 4, "A", TtlSec: 5));
        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "b", args, 4, "B", TtlSec: 5));
        Assert.Equal("A", s.Tick(At(10), 3000)!.Text);
        Assert.Null(s.Tick(At(13.0), 3000));
        Assert.Null(s.Tick(At(20), 3000));                // B expired (queued at 10, ttl 5)
        Assert.Equal(0, s.PendingGeneral);

        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "c", args, 4, "C"));
        Assert.Equal("C", s.Tick(At(30), 3000)!.Text);
        s.Enqueue(new CoachMessage(CoachMessageKind.Engineer, "flag", args, 6, "FLAG", Interrupt: true));
        Assert.Equal("FLAG", s.Tick(At(31.6), 3000)!.Text); // urgent spacing 1.5 s instead of 4 s
    }

    [Fact]
    public void CoachEngine_routes_engineer_calls_through_the_scheduler_and_respects_the_toggle()
    {
        var dir = CoachSim.TempDir();
        try
        {
            var msgs = new List<CoachMessage>();
            var log = new List<string>();
            var e = new CoachEngine(dir, () => new DateTime(2026, 10, 19)) { BrakeTonesEnabled = false };
            e.MessageReady += msgs.Add;
            e.EngineerLogLine += log.Add;
            e.SetContext(new CoachContext("monza full", "ferrari 296", 3000, "Race"));

            double t = 100;
            CoachSample S(int flags, double v = 50, bool pit = false) => new(t += 1, 1, 0.3, v, 0, 1, 0, 4, 6000, pit, true,
                new EngineerData { FuelLevelL = 30, SessionState = 4, SessionFlags = flags });
            e.Feed(S(0));
            e.Feed(S(0x8)); e.Feed(S(0x8)); e.Feed(S(0x8));
            var y = msgs.Single(m => m.Kind == CoachMessageKind.Engineer);
            Assert.Equal("flag.yellow", y.Key);
            Assert.True(y.Interrupt);
            Assert.Contains(log, l => l.Contains("SAY flag.yellow"));

            e.EngineerEnabled = false;
            e.Feed(S(0)); e.Feed(S(0x20)); e.Feed(S(0x20)); e.Feed(S(0x20));
            Assert.Single(msgs.Where(m => m.Kind == CoachMessageKind.Engineer));

            // Old-style sample without engineer data keeps working.
            e.Feed(new CoachSample(t += 1, 1, 0.3, 50, 0, 1, 0, 4, 6000, false, true));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Engineer_log_appends_and_rotates_at_the_size_limit()
    {
        var dir = CoachSim.TempDir();
        try
        {
            var path = Path.Combine(dir, "coach-engineer.log");
            using (var log = new EngineerLog(path))
            {
                var line = new string('x', 1000);
                for (int i = 0; i < 700; i++) { log.Append(line); if (i % 100 == 99) log.Flush(); }
                log.Flush();
                log.Append("last line");
                log.Flush();
            }
            Assert.True(File.Exists(path + ".1"));
            Assert.True(new FileInfo(path).Length < EngineerLog.MaxBytes + 200 * 1024);
            Assert.EndsWith("last line" + Environment.NewLine, File.ReadAllText(path));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Settings_have_the_engineer_toggles_on_by_default_and_roundtrip()
    {
        var dir = CoachSim.TempDir();
        try
        {
            var path = Path.Combine(dir, "coach-settings.json");
            var s = new CoachSettings();
            Assert.True(s.EngineerEnabled && s.FuelCalls && s.FlagCalls && s.GapCalls && s.PitCalls);
            s.GapCalls = false; s.Save(path);
            var back = CoachSettings.Load(path);
            Assert.False(back.GapCalls);
            Assert.True(back.FuelCalls);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
