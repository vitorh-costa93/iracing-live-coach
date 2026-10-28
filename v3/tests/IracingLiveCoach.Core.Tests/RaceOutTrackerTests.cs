using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public sealed class RaceOutTrackerTests
{
    private const int Out = RaceOutTracker.NotInWorld;

    private static OutCarSample S(int idx, int laps, double pct, int surface = 3) => new(idx, 0, laps, pct, surface);

    private static List<int> Order(List<LiveRaceEntry> e) =>
        e.OrderByDescending(x => x.Laps).ThenByDescending(x => x.DistPct).Select(x => x.CarIdx).ToList();

    [Fact]
    public void Out_car_stays_in_order_parked_at_last_progress()
    {
        var t = new RaceOutTracker();
        t.Resolve([S(1, 5, .9), S(2, 5, .5), S(3, 4, .8)], true); // car 2 runs, then leaves
        var list = t.Resolve([S(1, 5, .95), S(2, 0, -1, Out), S(3, 4, .9)], true);
        Assert.Equal([1, 2, 3], Order(list));
        Assert.True(list.Single(e => e.CarIdx == 2).Out);
    }

    [Fact]
    public void Player_gains_position_only_after_passing_the_parked_point()
    {
        var t = new RaceOutTracker();
        t.Resolve([S(1, 5, .9), S(2, 5, .5), S(3, 4, .3)], true);
        Assert.Equal([1, 2, 3], Order(t.Resolve([S(1, 5, .95), S(2, 0, -1, Out), S(3, 4, .45)], true)));
        Assert.Equal([1, 2, 3], Order(t.Resolve([S(1, 5, .99), S(2, 0, -1, Out), S(3, 4, .99)], true)));
        Assert.Equal([1, 3, 2], Order(t.Resolve([S(1, 5, .99), S(2, 0, -1, Out), S(3, 5, .6)], true)));
    }

    [Fact]
    public void Out_car_feeds_irating_projection_like_the_rating_field()
    {
        var t = new RaceOutTracker();
        var field = new RaceRatingField();
        field.Observe([new(1, 0, 3000), new(2, 0, 2000), new(3, 0, 1000)]);
        var before = new[] { S(1, 5, .9), S(2, 5, .5), S(3, 4, .3) };
        t.Resolve(before, true);
        field.Project(before.Select(s => new RatingPosition(s.CarIdx, s.Laps, s.DistPct, 0)), false);
        var raw = new[] { S(1, 5, .95), S(2, 5, -1, Out), S(3, 4, .45) };
        var entries = t.Resolve(raw, true);
        var projected = field.Project(raw.Select(s => new RatingPosition(s.CarIdx, s.Laps, s.DistPct, 0)), false);
        var expected = IRatingProjection.Compute(Order(entries).Select((idx, i) => new IRatingEntry(idx, idx switch { 1 => 3000, 2 => 2000, _ => 1000 }, i + 1)));
        foreach (var kv in expected) Assert.Equal(kv.Value, projected[kv.Key]);
    }

    [Fact]
    public void Returning_to_the_world_clears_out()
    {
        var t = new RaceOutTracker();
        t.Resolve([S(2, 5, .5)], true);
        Assert.True(t.Resolve([S(2, 5, -1, Out)], true).Single().Out);
        var back = t.Resolve([S(2, 5, .6)], true).Single();
        Assert.False(back.Out);
        Assert.Equal(.6, back.DistPct);
    }

    [Fact]
    public void Never_seen_car_or_pit_stall_car_is_not_out()
    {
        var t = new RaceOutTracker();
        Assert.Empty(t.Resolve([S(4, 0, -1, Out)], true)); // never on track in the race
        var pit = t.Resolve([S(5, 0, .02, 1)], true).Single(); // pit stall: valid distance
        Assert.False(pit.Out);
        t.Resolve([S(6, 3, .5)], false); // not recording (parade/warm-up): nothing retained
        Assert.Empty(t.Resolve([S(6, 3, -1, Out)], true));
    }
}
