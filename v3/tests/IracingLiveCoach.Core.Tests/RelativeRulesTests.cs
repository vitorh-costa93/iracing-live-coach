using System.Collections.Generic;
using System.Linq;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class RelativeRulesTests
{
    [Theory]
    [InlineData(-3, true)]
    [InlineData(-4, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Includes_respects_ahead_and_behind_independently(int offset, bool expected)
    {
        Assert.Equal(expected, new RelativeRules(3, 2).Includes(offset));
    }

    [Fact]
    public void Clamped_limits_to_the_reader_maximum_and_zero()
    {
        var clamped = new RelativeRules(99, -5).Clamped();
        Assert.Equal(RelativeRules.Max, clamped.Ahead);
        Assert.Equal(0, clamped.Behind);
    }

    [Fact]
    public void TopN_included_in_total_vs_added_on_top()
    {
        StandingsRow Row(int pos, bool player = false) => new(pos, "D" + pos, 1, null, null, player, "", "", null, 1000, 1, "", null, null, null, "GT3", null, pos, null, null, null, null, false, "", "", 1);
        var rows = Enumerable.Range(1, 10).Select(i => Row(i, i == 6)).ToList();

        var included = StandingsSelection.GroupAndSelect(rows, new StandingsPresentationOptions(TopNPerClass: 1, OwnClassRows: 4, OtherClassRows: 1, KeepPlayerWindow: true, TopNCountsTowardTotal: true));
        var added = StandingsSelection.GroupAndSelect(rows, new StandingsPresentationOptions(TopNPerClass: 1, OwnClassRows: 4, OtherClassRows: 1, KeepPlayerWindow: true, TopNCountsTowardTotal: false));
        Assert.Equal(4, included.Single().Rows.Count);
        Assert.Equal(5, added.Single().Rows.Count);
        Assert.Contains(added.Single().Rows, r => r.Position == 1);
        Assert.Contains(added.Single().Rows, r => r.IsPlayer);
    }
}
