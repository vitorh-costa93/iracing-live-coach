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
