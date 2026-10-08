using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class RaceRatingFieldTests
{
    private static RaceRatingField Field()
    {
        var field = new RaceRatingField();
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000)]);
        return field;
    }

    [Fact]
    public void Joining_mid_race_counts_every_entrant_even_without_live_distance()
    {
        var field = Field();
        var actual = field.Project([new(1, 8, .5, 1), new(2, -1, -1, 2), new(3, -1, -1, 3)], false);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(2, 3000, 2), new(3, 5000, 3)]);
        Assert.Equal(Sof.Compute([1000, 3000, 5000]), field.Sof);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
    }

    [Fact]
    public void Full_grid_preserves_existing_formula_and_projection()
    {
        var field = Field();
        Assert.Equal(Sof.Compute([1000, 3000, 5000]), field.Sof);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(2, 3000, 2), new(3, 5000, 3)]);
        var actual = field.Project([new(1, 5, .8, 1), new(2, 5, .6, 2), new(3, 5, .4, 3)], false);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
    }

    [Fact]
    public void Disconnect_and_rating_update_do_not_change_sof_or_remove_opponent()
    {
        var field = Field();
        var sof = field.Sof;
        var before = field.Project([new(1, 5, .8, 1), new(2, 5, .6, 2), new(3, 5, .4, 3)], false);
        field.Observe([new(1, 10, 9000), new(3, 10, 5000)]);
        var after = field.Project([new(1, 5, .8, 1), new(2, -1, -1, 0), new(3, 5, .4, 2)], false);
        Assert.Equal(sof, field.Sof);
        Assert.Equal(3, field.Count);
        foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
    }

    [Fact]
    public void Overtake_changes_projection_but_not_sof()
    {
        var field = Field();
        var sof = field.Sof;
        var before = field.Project([new(1, 5, .8, 1), new(2, 5, .6, 2), new(3, 5, .4, 3)], false);
        var after = field.Project([new(1, 5, .8, 1), new(2, -1, -1, 0), new(3, 5, .9, 1)], false);
        Assert.Equal(sof, field.Sof);
        Assert.True(after[3] > before[3]);
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public void Multiclass_excludes_pace_and_spectators_and_uses_official_finish()
    {
        var field = Field();
        field.Observe([new(4, 20, 2000), new(5, 20, 4000), new(6, 10, 10000, PaceCar: true), new(7, 10, 10000, Spectator: true)]);
        Assert.Equal(Sof.Compute([1000, 3000, 5000]), field.ClassSof[10]);
        Assert.Equal(Sof.Compute([2000, 4000]), field.ClassSof[20]);
        var actual = field.Project([new(1, -1, -1, 3), new(2, -1, -1, 2), new(3, -1, -1, 1), new(4, -1, -1, 5), new(5, -1, -1, 4)], true);
        var expected = IRatingProjection.Compute([new(3, 5000, 1), new(2, 3000, 2), new(1, 1000, 3)]);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.Equal(5, actual.Count);
    }

    private static readonly RatingEventKey Event = RatingEventKey.From(100, 5000, 7);
    private const int Qualify = 1, Race = 2;

    private static RaceRatingField EventField()
    {
        var field = new RaceRatingField();
        field.Enter(Event, Race);
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000), new(4, 10, 2500)]);
        return field;
    }

    [Fact]
    public void Driver_leaving_before_the_green_keeps_counting()
    {
        var field = EventField();
        var sof = field.Sof;
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000)]); // 4 left the grid
        Assert.Equal(sof, field.Sof);
        Assert.Equal(Sof.Compute([1000, 3000, 5000, 2500]), field.Sof);
        var grid = field.Project([new(1, 0, 0, 1), new(2, 0, 0, 2), new(3, 0, 0, 3), new(4, -1, -1, 0)], officialOrder: true);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(2, 3000, 2), new(3, 5000, 3), new(4, 2500, 4)]);
        foreach (var pair in expected) Assert.Equal(pair.Value, grid[pair.Key]);
    }

    [Fact]
    public void Driver_leaving_after_the_green_keeps_rating_and_last_progress()
    {
        var field = EventField();
        var sof = field.Sof;
        field.Project([new(1, 3, .5, 1), new(4, 3, .4, 2), new(2, 3, .3, 3), new(3, 3, .2, 4)], false);
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000)]);
        var after = field.Project([new(1, 3, .6, 1), new(4, 3, -1, 0), new(2, 3, .35, 2), new(3, 3, .25, 3)], false);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(4, 2500, 2), new(2, 3000, 3), new(3, 5000, 4)]);
        Assert.Equal(sof, field.Sof);
        foreach (var pair in expected) Assert.Equal(pair.Value, after[pair.Key]);
    }

    [Fact]
    public void Late_entrant_is_added_like_kapps()
    {
        var field = EventField();
        field.Observe([new(1, 10, 1000), new(5, 10, 4000)]);
        Assert.Equal(5, field.Count);
        Assert.Equal(Sof.Compute([1000, 3000, 5000, 2500, 4000]), field.Sof);
    }

    [Fact]
    public void Driver_list_losing_entries_does_not_change_the_field()
    {
        var field = EventField();
        var sof = field.Sof;
        var classSof = field.ClassSof[10];
        field.Observe([]);
        field.Observe([new(2, 10, 3000)]);
        Assert.Equal(4, field.Count);
        Assert.Equal(sof, field.Sof);
        Assert.Equal(classSof, field.ClassSof[10]);
    }

    [Fact]
    public void Sdk_reconnection_into_the_same_event_does_not_reset()
    {
        var field = EventField();
        var sof = field.Sof;
        field.Project([new(1, 3, .5, 1), new(4, 3, .4, 2), new(2, 3, .3, 3), new(3, 3, .2, 4)], false);
        // Reconnected: same WeekendInfo SessionID/SubSessionID (even with a new SessionUniqueID after a sim
        // restart), same session number, and DriverInfo no longer lists the driver who left.
        field.Enter(RatingEventKey.From(100, 5000, 99), Race);
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000)]);
        Assert.Equal(4, field.Count);
        Assert.Equal(sof, field.Sof);
        var after = field.Project([new(1, 3, .6, 1), new(4, -1, -1, 0), new(2, 3, .35, 2), new(3, 3, .25, 3)], false);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(4, 2500, 2), new(2, 3000, 3), new(3, 5000, 4)]);
        foreach (var pair in expected) Assert.Equal(pair.Value, after[pair.Key]);
    }

    [Fact]
    public void Session_change_in_the_event_keeps_drivers_but_clears_progress()
    {
        var field = new RaceRatingField();
        field.Enter(Event, Qualify);
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000), new(4, 10, 2500)]);
        field.Project([new(4, 9, .9, 1)], false); // qualifying laps must not leak into the race order
        field.Enter(Event, Race);
        field.Observe([new(1, 10, 1000), new(2, 10, 3000), new(3, 10, 5000)]);
        Assert.Equal(Sof.Compute([1000, 3000, 5000, 2500]), field.Sof);
        var race = field.Project([new(1, 1, .5, 1), new(2, 1, .4, 2), new(3, 1, .3, 3), new(4, -1, -1, 0)], false);
        var expected = IRatingProjection.Compute([new(1, 1000, 1), new(2, 3000, 2), new(3, 5000, 3), new(4, 2500, 4)]);
        foreach (var pair in expected) Assert.Equal(pair.Value, race[pair.Key]);
    }

    [Fact]
    public void New_event_resets_the_field()
    {
        var field = EventField();
        field.Enter(RatingEventKey.From(101, 5001, 7), Race);
        Assert.Equal(0, field.Count);
        Assert.Null(field.Sof);
        field.Observe([new(1, 10, 1500)]);
        Assert.Equal(1500, field.Sof!.Value, 8);
    }

    [Fact]
    public void Offline_event_identity_is_the_session_unique_id()
    {
        Assert.Equal(RatingEventKey.From(0, 0, 7), RatingEventKey.From(0, 0, 7));
        Assert.NotEqual(RatingEventKey.From(0, 0, 7), RatingEventKey.From(0, 0, 8));
        Assert.Equal(RatingEventKey.From(100, 5000, 7), RatingEventKey.From(100, 5000, 8));
    }

    [Fact]
    public void Final_results_rank_unclassified_cars_after_classified_ones()
    {
        var field = EventField();
        // 3 is still running (no final position yet) and has more progress than everyone: it must not be
        // interleaved with the official positions.
        var actual = field.Project([new(1, 10, .1, 2), new(2, 10, .1, 1), new(3, 11, .9, 0), new(4, 10, .1, 3)], officialOrder: true);
        var expected = IRatingProjection.Compute([new(2, 3000, 1), new(1, 1000, 2), new(4, 2500, 3), new(3, 5000, 4)]);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
    }

    [Fact]
    public void Late_initial_data_is_added_and_reset_does_not_leak_previous_race()
    {
        var field = Field();
        field.Observe([new(4, 20, 2000)]);
        Assert.Equal(4, field.Count);
        field.Reset();
        Assert.Null(field.Sof);
        Assert.Empty(field.ClassSof);
        field.Observe([new(1, 20, 1500)]);
        Assert.Equal(1500, field.Sof!.Value, 8);
        Assert.Equal(1, field.Count);
    }
}
