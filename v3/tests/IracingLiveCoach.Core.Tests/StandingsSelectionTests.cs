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
            new StandingsPresentationOptions(TopNPerClass: 1, OwnClassRows: 5, OtherClassRows: 1));

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

    private static StandingsRow Row(int position, int classId, int classPosition, bool player) =>
        new(position, $"Driver {position}", 1, null, null, player, "", "A", null, 1000, classId,
            "Ferrari", null, null, null, classId == 1 ? "GT3" : "GTP", null, classPosition,
            null, null, null, null, false, "—", position.ToString());
}
