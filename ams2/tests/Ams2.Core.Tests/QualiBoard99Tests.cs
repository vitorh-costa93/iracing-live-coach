using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public sealed class QualiBoard99Tests
{
    [Fact]
    public void Official_nonplayer_leader_result_reconciles_same_observed_lap_only()
    {
        var sim = new Sim(3000, (2900, 45), (2900, 3000 / 60.010)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        while (sim.Now < 62.2) { sim.Step(.05); tracker.Update(sim.Now, sim.Snapshot()); }
        // A missing official best still lets us expose the already observed leader reference.
        var derived = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c.Index == 1 ? c with { BestLapTime = 60.010 } : c).ToArray() };
        var observed = tracker.Update(sim.Now, derived).LeaderReference!;
        Assert.NotNull(observed);
        sim.LastLap[1] = 60;
        sim.Step(.05);
        var official = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c.Index == 1 ? c with { BestLapTime = 60 } : c).ToArray() };
        var corrected = tracker.Update(sim.Now, official).LeaderReference!;
        Assert.NotNull(corrected);
        Assert.Equal(1, corrected.Car.Index);
        Assert.Equal(60, corrected.Times[2]);
        Assert.Equal(observed.Times[0], corrected.Times[0]);
        Assert.Equal(observed.Times[1], corrected.Times[1]);
        Assert.Equal(observed.SpeedsMps, corrected.SpeedsMps);
        sim.Step(.05);
        var unseen = official with { Cars = official.Cars.Select(c => c.Index == 1 ? c with { BestLapTime = 59.99 } : c).ToArray() };
        Assert.Null(tracker.Update(sim.Now, unseen).LeaderReference);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nonplayer_correction_requires_same_identity_and_crossing_settle_window(bool replacement)
    {
        var sim = new Sim(3000, (2900, 45), (2900, 3000 / 60.010)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        while (sim.Now < 62.2) { sim.Step(.05); tracker.Update(sim.Now, sim.Snapshot()); }
        sim.LastLap[1] = 60;
        sim.Step(replacement ? .05 : 2.1);
        var official = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c.Index == 1
            ? c with { BestLapTime = 60, Name = replacement ? "Replacement" : c.Name } : c).ToArray() };
        Assert.Null(tracker.Update(sim.Now, official).LeaderReference);
    }

    [Fact]
    public void Official_result_reconciles_reference_of_same_observed_best_lap()
    {
        var sim = new Sim(3000, (2900, 3000 / 60.010)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        while (sim.Now < 62.2) { sim.Step(.05); tracker.Update(sim.Now, sim.Snapshot()); }
        var observed = tracker.State.PersonalReference!;
        Assert.NotNull(observed);
        Assert.Equal(60.010, observed.Times[2]!.Value, 5);
        sim.LastLap[0] = 60;
        sim.Step(.05);
        var official = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c with { BestLapTime = 60 }).ToArray() };
        var q = tracker.Update(sim.Now, official);
        Assert.True(q.LastResult!.FromMemory);
        Assert.Equal(60, q.LastResult.LapTime);
        Assert.NotNull(q.PersonalReference);
        Assert.Equal(60, q.PersonalReference!.Times[2]);
        Assert.Equal(observed.Times[0], q.PersonalReference.Times[0]);
        Assert.Equal(observed.Times[1], q.PersonalReference.Times[1]);
        Assert.Equal(observed.SpeedsMps, q.PersonalReference.SpeedsMps);
    }

    [Fact]
    public void Next_split_cannot_compare_against_unobserved_new_official_best()
    {
        var sim = new Sim(3000, (2900, 50)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        while (sim.Now < 62.2) { sim.Step(.05); tracker.Update(sim.Now, sim.Snapshot()); }
        Assert.NotNull(tracker.State.PersonalReference);
        while (sim.Now < 82.2)
        {
            sim.Step(.05);
            var official = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c with { BestLapTime = 59 }).ToArray() };
            tracker.Update(sim.Now, official);
        }
        var split = tracker.State.LastSplit!;
        Assert.NotNull(split);
        Assert.Null(split.PersonalReference);
        Assert.Null(split.LeaderReference);
        Assert.Null(split.DeltaPersonal);
        Assert.Null(split.DeltaLeader);
    }

    static QualiLapState Lap => QualiLapState.Empty with { CarIndex = 0, Elapsed = 10, LeaderBestLap = 60 };
    static CarSnapshot Car => new Sim(3000, (0, 50)).Snapshot().Cars[0];
    static QualiTimingReference Reference => new(Car, [20, 40, 60], [80, 81, 82]);

    [Fact]
    public void Outlap_never_returns_classification_and_running_only_keeps_clock()
    {
        Assert.Equal(QualiBoard99Stage.OutLap, QualiBoard99Presentation.Resolve(Lap with { OutLap = true }, 1).Stage);
        Assert.Equal(QualiBoard99Stage.Hidden, QualiBoard99Presentation.Resolve(Lap with { InPit = true }, 1).Stage);
        var live = QualiBoard99Presentation.Resolve(Lap, 1);
        Assert.Equal(QualiBoard99Stage.Running, live.Stage);
        Assert.Null(live.ReferenceTime); Assert.Null(live.PlayerSpeedMps); Assert.Null(live.Delta);
    }

    [Fact]
    public void Approach_and_crossing_keep_speed_from_reference_lap_and_crossing()
    {
        var q = Lap with { Elapsed = 16, LeaderReference = Reference };
        var approaching = QualiBoard99Presentation.Resolve(q, 50);
        Assert.Equal(QualiBoard99Stage.Approach, approaching.Stage);
        Assert.Equal(20, approaching.ReferenceTime); Assert.Equal(80, approaching.ReferenceSpeedMps);
        Assert.Null(approaching.PlayerSpeedMps); Assert.Null(approaching.Delta);
        var split = new QualiSplit(1, 20.3, null, .3, 51) { SpeedMps = 75, LeaderReference = Reference };
        q = q with { Sector = 1, Elapsed = 21, LastSplit = split, LeaderReference = null };
        var crossed = QualiBoard99Presentation.Resolve(q, 52);
        Assert.Equal(QualiBoard99Stage.Split, crossed.Stage);
        Assert.Equal(20.3, crossed.Time); Assert.Equal(80, crossed.ReferenceSpeedMps);
        Assert.Equal(75, crossed.PlayerSpeedMps); Assert.Equal(.3, crossed.Delta);
        Assert.Equal(QualiBoard99Stage.Running, QualiBoard99Presentation.Resolve(q, 54).Stage);
    }

    [Fact]
    public void Independent_minima_do_not_fabricate_a_partial_or_speed()
    {
        var q = Lap with { Elapsed = 19, OverallBestSectors = [18, 18, 18] };
        Assert.Equal(QualiBoard99Stage.Running, QualiBoard99Presentation.Resolve(q, 20).Stage);
        q = q with { Sector = 1, LastSplit = new(1, 20, null, .5, 21) };
        var crossed = QualiBoard99Presentation.Resolve(q, 21);
        Assert.Equal(19.5, crossed.ReferenceTime);
        Assert.Null(crossed.ReferenceSpeedMps); Assert.Null(crossed.PlayerSpeedMps);
    }

    [Fact]
    public void Finish_expires_and_does_not_treat_completed_outlap_as_timed_result()
    {
        var result = new QualiLapResult(1, 60.4, 5, .4, null, false, false, [null, null, null], 100, false) { SpeedMps = 73 };
        var q = Lap with { LastResult = result };
        var view = QualiBoard99Presentation.Resolve(q, 102);
        Assert.Equal(QualiBoard99Stage.Finish, view.Stage); Assert.Equal(5, view.Position); Assert.Equal(73, view.PlayerSpeedMps);
        Assert.Equal(QualiBoard99Stage.Running, QualiBoard99Presentation.Resolve(q, 106).Stage);
        Assert.Equal(QualiBoard99Stage.Running, QualiBoard99Presentation.Resolve(q with { LastResult = result with { WasOutLap = true } }, 102).Stage);
    }

    [Fact]
    public void Tracker_best_lap_reference_speeds_survive_later_acceleration_and_reset_clears_them()
    {
        var sim = new Sim(3000, (2900, 50)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        for (int frame = 0; frame < 1245; frame++)
        {
            sim.Step(.05); tracker.Update(sim.Now, sim.Snapshot());
        }
        var best = tracker.State.PersonalReference;
        Assert.NotNull(best);
        Assert.All(best!.SpeedsMps, speed => Assert.Equal(50, speed));
        Assert.InRange(best.Times[2]!.Value, 59.99, 60.01);
        sim.Speeds[0] = 80; sim.Step(.05);
        Assert.Equal(best.SpeedsMps, tracker.Update(sim.Now, sim.Snapshot()).PersonalReference!.SpeedsMps);
        var official = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c with { BestLapTime = 59 }).ToArray() };
        Assert.Null(tracker.Update(sim.Now, official).PersonalReference);
        tracker.Reset();
        Assert.Null(tracker.Update(sim.Now, sim.Snapshot()).PersonalReference);
    }

    [Fact]
    public void Tracker_interpolates_speed_and_retains_it_after_car_accelerates()
    {
        var sim = new Sim(3000, (2900, 50)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        tracker.Update(0, sim.Snapshot());
        sim.Step(2.1); tracker.Update(sim.Now, sim.Snapshot());
        sim.Step(19.8); tracker.Update(sim.Now, sim.Snapshot());
        sim.Speeds[0] = 70;
        sim.Step(.2);
        var q = tracker.Update(sim.Now, sim.Snapshot());
        Assert.NotNull(q.LastSplit);
        Assert.InRange(q.LastSplit!.SpeedMps!.Value, 50, 70);
        var frozen = q.LastSplit.SpeedMps;
        sim.Speeds[0] = 90; sim.Step(.2);
        Assert.Equal(frozen, tracker.Update(sim.Now, sim.Snapshot()).LastSplit!.SpeedMps);
        Assert.Null(tracker.Update(0, sim.Snapshot()).LastSplit);
    }
}
