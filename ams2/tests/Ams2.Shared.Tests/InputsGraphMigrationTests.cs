using System.Text.Json;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public sealed class InputsGraphMigrationTests
{
    [Theory]
    [InlineData(true, true, true, 498)]
    [InlineData(false, true, true, 180)]
    [InlineData(false, false, true, 92)]
    [InlineData(false, true, false, 92)]
    [InlineData(false, false, false, 0)]
    public void Migration_preserves_graph_customization_and_visual_position(bool speedo, bool gear, bool bars, int top)
    {
        var columns = new List<string> { "graph" };
        if (speedo) columns.Add("speedo");
        if (gear) columns.Add("gear");
        if (bars) columns.Add("bars");
        var source = new WidgetSettings { Id = "inputs", X = 321, Y = 456, Scale = .8f, TextScale = 1.25f,
            Opacity = .6f, Font = "Arial", FontWeight = 700, TextColor = "#112233", LabelColor = "#445566",
            ValueColor = "#778899", Sessions = [SessionIds.Race], Columns = columns.ToArray(),
            ColumnWidths = new() { ["graph"] = 150 } };
        var p = new Profile { SchemaVersion = 4, ThemeId = "f1-2004", Widgets = [source] }.Normalized();
        var graph = p.Get("inputgraph")!;
        Assert.True(graph.Visible);
        Assert.Equal((source.X, source.Y + top), (graph.X, graph.Y));
        Assert.Equal((source.Scale * source.TextScale!.Value, (float?)null, source.Opacity, source.Font, source.FontWeight),
            (graph.Scale, graph.TextScale, graph.Opacity, graph.Font, graph.FontWeight));
        Assert.Equal((source.TextColor, source.LabelColor, source.ValueColor), (graph.TextColor, graph.LabelColor, graph.ValueColor));
        Assert.Equal(source.Sessions, graph.Sessions);
        Assert.Equal(150, graph.ColumnWidths!["graph"]);
        Assert.Null(p.Get("inputs")!.ColumnWidths);
        Assert.DoesNotContain("graph", p.Get("inputs")!.Columns!);
        Assert.Equal(JsonSerializer.Serialize(p), JsonSerializer.Serialize(p.Normalized()));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Graph_migration_respects_old_visibility(bool visible, bool enabled)
    {
        var p = new Profile { SchemaVersion = 4, ThemeId = "f1-2004", Widgets =
            [new WidgetSettings { Id = "inputs", Visible = visible, Columns = enabled ? ["graph"] : [] }] }.Normalized();
        Assert.False(p.Get("inputgraph")!.Visible);
    }

    [Fact]
    public void Explicit_graph_settings_are_never_overwritten_by_migration()
    {
        var p = new Profile { SchemaVersion = 4, ThemeId = "f1-2004", Widgets =
            [new WidgetSettings { Id = "inputs" }, new WidgetSettings { Id = "inputgraph", X = 77, Y = 88, Visible = false }] }.Normalized();
        Assert.Equal((77, 88, false), (p.Get("inputgraph")!.X, p.Get("inputgraph")!.Y, p.Get("inputgraph")!.Visible));
    }

    [Fact]
    public void Theme_definitions_and_defaults_keep_the_graph_exclusive_to_2004()
    {
        var p = ProfileFactory.CreateDefault("Graph", "f1-2004");
        Assert.Equal((1700, 690, .6f), (p.Get("inputs")!.X, p.Get("inputs")!.Y, p.Get("inputs")!.Scale));
        Assert.Equal((1700, 988, .6f), (p.Get("inputgraph")!.X, p.Get("inputgraph")!.Y, p.Get("inputgraph")!.Scale));
        Assert.Empty(WidgetCatalog.Find("inputs", "f1-2004")!.Widths);
        foreach (string theme in new[] { "f1-1998", "f1-2018" })
        {
            Assert.Null(WidgetCatalog.Find("inputgraph", theme));
            Assert.Contains(WidgetCatalog.Find("inputs", theme)!.Columns, c => c.Id == "graph");
            Assert.NotEmpty(WidgetCatalog.Find("inputs", theme)!.Widths);
            Assert.Null((p with { ThemeId = theme }).Normalized().Get("inputgraph"));
        }
    }
}
