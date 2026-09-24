using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class ColumnDefaultsTests
{
    private static ColumnDefinition Col(string key, int order, bool visible = true, float width = 30) =>
        new(key, ColumnWidthMode.Fixed, width, width, ColumnAlignment.Center, 0, 0, visible, order);

    [Fact]
    public void A_new_default_column_is_inserted_after_its_predecessor_and_orders_are_renumbered()
    {
        var defaults = new List<ColumnDefinition> { Col("position", 0), Col("posChange", 1), Col("carNumber", 2), Col("name", 3) };
        var saved = new List<ColumnDefinition> { Col("name", 0, width: 180), Col("position", 1), Col("carNumber", 2, visible: false) };
        var merged = ColumnDefaults.MergeMissing(saved, defaults);
        Assert.Equal(["name", "position", "posChange", "carNumber"], merged.Select(c => c.Key));
        Assert.Equal([0, 1, 2, 3], merged.Select(c => c.Order));
        Assert.Equal(180, merged[0].WidthPx);          // user's settings kept
        Assert.False(merged[3].Visible);
    }

    [Fact]
    public void Nothing_missing_returns_the_same_list()
    {
        var defaults = new List<ColumnDefinition> { Col("position", 0), Col("name", 1) };
        var saved = new List<ColumnDefinition> { Col("name", 0), Col("position", 1) };
        Assert.Same(saved, ColumnDefaults.MergeMissing(saved, defaults));
    }

    [Fact]
    public void Missing_first_column_goes_first()
    {
        var defaults = new List<ColumnDefinition> { Col("position", 0), Col("name", 1) };
        var merged = ColumnDefaults.MergeMissing([Col("name", 0)], defaults);
        Assert.Equal(["position", "name"], merged.Select(c => c.Key));
    }
}
