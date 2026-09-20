using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class ColumnFitterTests
{
    private static ColumnDefinition Col(string key, float width, bool visible = true, float padRight = 6f) =>
        new(key, ColumnWidthMode.Fixed, width, width, ColumnAlignment.Center, 0, padRight, visible, 0);

    private static List<ColumnDefinition> Table() =>
    [
        Col("position", 30, padRight: 0), Col("flag", 30, padRight: 0), Col("name", 150), Col("license", 56),
        Col("lastLap", 80), Col("pit", 68), Col("gap", 66, visible: false),
    ];

    [Fact]
    public void Total_width_counts_visible_columns_paddings_and_left_margin()
    {
        // 8 + 30 + 30 + (150+6) + (56+6) + (80+6) + (68+6) = 446 ; hidden gap ignored
        Assert.Equal(446f, ColumnFitter.TotalWidth(Table(), -1f));
    }

    [Fact]
    public void Padding_override_replaces_right_padding_of_padded_columns_only()
    {
        // padded columns: name, license, lastLap, pit -> right padding 6 becomes 2 (-16 in total)
        Assert.Equal(430f, ColumnFitter.TotalWidth(Table(), 2f));
    }

    [Fact]
    public void Fit_does_nothing_when_already_within_the_limit()
    {
        var fitted = ColumnFitter.Fit(Table(), Table(), 500f, -1f, out var remaining);
        Assert.Equal(0f, remaining);
        Assert.Equal(Table().Select(c => c.WidthPx), fitted.Select(c => c.WidthPx));
    }

    [Fact]
    public void Fit_narrows_the_name_column_first()
    {
        var fitted = ColumnFitter.Fit(Table(), Table(), 420f, -1f, out var remaining); // needs -26
        Assert.Equal(0f, remaining);
        Assert.Equal(124f, fitted.Single(c => c.Key == "name").WidthPx);
        Assert.Equal(80f, fitted.Single(c => c.Key == "lastLap").WidthPx); // others untouched
    }

    [Fact]
    public void Fit_never_narrows_name_below_its_minimum_and_then_trims_data_columns_within_the_floor()
    {
        var fitted = ColumnFitter.Fit(Table(), Table(), 380f, -1f, out var remaining); // needs -66
        Assert.Equal(ColumnFitter.MinNameWidth, fitted.Single(c => c.Key == "name").WidthPx);
        Assert.True(fitted.Single(c => c.Key == "lastLap").WidthPx < 80f);
        Assert.True(fitted.Single(c => c.Key == "lastLap").WidthPx >= 80f * ColumnFitter.FitFloor - 1f);
        Assert.True(ColumnFitter.TotalWidth(fitted, -1f) <= 380f + remaining + 0.01f);
    }

    [Fact]
    public void Fit_keeps_icon_columns_and_reports_what_still_exceeds()
    {
        var fitted = ColumnFitter.Fit(Table(), Table(), 100f, -1f, out var remaining);
        Assert.True(remaining > 0f);
        Assert.Equal(30f, fitted.Single(c => c.Key == "position").WidthPx);
        Assert.Equal(30f, fitted.Single(c => c.Key == "flag").WidthPx);
        Assert.Equal(ColumnFitter.TotalWidth(fitted, -1f) - 100f, remaining, 0.01);
    }

    [Fact]
    public void Fit_does_not_touch_hidden_columns_or_the_input_list()
    {
        var input = Table();
        var fitted = ColumnFitter.Fit(input, Table(), 300f, -1f, out _);
        Assert.Equal(66f, fitted.Single(c => c.Key == "gap").WidthPx);
        Assert.Equal(150f, input.Single(c => c.Key == "name").WidthPx);
    }
}
