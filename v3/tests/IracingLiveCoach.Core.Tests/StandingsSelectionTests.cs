using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class StandingsSelectionTests
{
    [Fact]
    public void GroupAndSelect_UsesClassLocalTopNAndPlayerWindowWithoutDuplicates()
    {
        var rows = new List<StandingsRow>
        {
            Row(1, 1, 1, false),
            Row(2, 1, 2, false),
            Row(3, 1, 3, true),
            Row(4, 1, 4, false),
            Row(5, 1, 5, false),
            Row(6, 2, 1, false),
            Row(7, 2, 2, false),
        };

        var groups = StandingsSelection.GroupAndSelect(rows,
            new StandingsPresentationOptions(TopNPerClass: 1, OwnClassRows: 5, OtherClassRows: 1, TopNCountsTowardTotal: true));

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, groups.Single(g => g.ClassId == 1).Rows.Select(r => r.ClassPosition));
        Assert.Equal(new[] { 1 }, groups.Single(g => g.ClassId == 2).Rows.Select(r => r.ClassPosition));
        Assert.Equal(5, groups.Single(g => g.ClassId == 1).Rows.Distinct().Count());
    }

    [Fact]
    public void GroupAndSelect_MonoclassDoesNotRequireClassLabelToSelectRows()
    {
        var rows = Enumerable.Range(1, 7).Select(position => Row(position, 7, position, position == 6));

        var groups = StandingsSelection.GroupAndSelect(rows,
            new StandingsPresentationOptions(TopNPerClass: 0, OwnClassRows: 5, OtherClassRows: 1));

        var group = Assert.Single(groups);
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, group.Rows.Select(r => r.ClassPosition));
    }

    [Fact]
    public void GroupAndSelect_OrdersClassPanelsByClassSpeedNotByOverallLeader()
    {
        // Slowest class (rank 3) leads overall, the player's class is the middle one.
        var rows = new List<StandingsRow>
        {
            Row(1, 30, 1, false) with { ClassRank = 3 },
            Row(2, 20, 1, true) with { ClassRank = 2 },
            Row(3, 10, 1, false) with { ClassRank = 1 },
            Row(4, 30, 2, false) with { ClassRank = 3 },
        };

        var groups = StandingsSelection.GroupAndSelect(rows);

        Assert.Equal(new[] { 10, 20, 30 }, groups.Select(g => g.ClassId));
    }

    [Fact]
    public void ClassDriverCounts_CountsEntrantsPerClassAndKappsText()
    {
        var entrants = new[] { (0, 1), (1, 1), (2, 1), (3, 2), (4, 2), (5, 3), (6, 3) };
        var counts = ClassDriverCounts.Compute(entrants, new[] { 0, 1, 5, 6 });

        Assert.Equal(new ClassDriverCount(3, 2), counts[1]);
        Assert.Equal("2/3", counts[1].Text);  // some with a time
        Assert.Equal("2", counts[2].Text);    // nobody with a time: plain total
        Assert.Equal("2", counts[3].Text);    // everybody with a time: plain total
    }

    [Fact]
    public void ClassDriverCounts_MatchesKappsLoneQualifyPrint()
    {
        // 24/09/2026 AI session: classes of 14/13/13 with 2/0/3 practice times -> "2/14", "13", "3/13".
        var entrants = Enumerable.Range(0, 14).Select(i => (i, 4029))
            .Concat(Enumerable.Range(14, 13).Select(i => (i, 2523)))
            .Concat(Enumerable.Range(27, 13).Select(i => (i, 4011)));
        var counts = ClassDriverCounts.Compute(entrants, new[] { 0, 1, 27, 28, 29 });

        Assert.Equal("2/14", counts[4029].Text);
        Assert.Equal("13", counts[2523].Text);
        Assert.Equal("3/13", counts[4011].Text);
    }

    [Theory]
    [InlineData(6, new[] { 1, 2, 4, 5, 6, 7, 8 })]
    [InlineData(14, new[] { 1, 2, 10, 11, 12, 13, 14 })]
    public void Default_options_are_kapps_top_two_plus_five_around_the_player(int player, int[] expected)
    {
        var rows = Enumerable.Range(1, 14).Select(p => Row(p, 1, p, p == player));
        var group = Assert.Single(StandingsSelection.GroupAndSelect(rows));
        Assert.Equal(expected, group.Rows.Select(r => r.ClassPosition));
    }

    private static StandingsRow Row(int position, int classId, int classPosition, bool player) =>
        new(position, $"Driver {position}", 1, null, null, player, "", "A", null, 1000, classId,
            "Ferrari", null, null, null, classId == 1 ? "GT3" : "GTP", null, classPosition,
            null, null, null, null, false, "—", position.ToString());
}
