using System.Text.Json;
using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class ColumnSessionVisibilityTests
{
    private static ColumnDefinition Column => new("lastLap", ColumnWidthMode.Fixed, 80, 80, ColumnAlignment.Right, 0, 0, true, 0);

    [Theory]
    [InlineData("PRACTICE", true)]
    [InlineData("WARMUP", true)]
    [InlineData("LONE QUALIFY", true)]
    [InlineData("OPEN QUALIFY", true)]
    [InlineData("RACE", false)]
    [InlineData(null, true)]
    public void Practice_and_qualifying_only_removes_race_column_and_its_width(string? session, bool visible)
    {
        var configured = Column with { ShowInRace = false };
        var applied = ColumnSessionVisibility.Apply([configured], session);
        Assert.Equal(visible, applied.Single().Visible);
        Assert.Equal(visible ? 80f : 0f, WidgetLayoutEngine.SumVisibleColumnFootprints(applied));
        Assert.True(configured.Visible);
    }

    [Fact]
    public void Hidden_column_stays_hidden_and_session_switch_restores_allowed_column()
    {
        Assert.False(ColumnSessionVisibility.Apply([Column with { Visible = false }], "PRACTICE").Single().Visible);
        var configured = Column with { ShowInPractice = false, ShowInQualify = false };
        Assert.False(ColumnSessionVisibility.Apply([configured], "PRACTICE").Single().Visible);
        Assert.False(ColumnSessionVisibility.Apply([configured], "QUALIFY").Single().Visible);
        Assert.True(ColumnSessionVisibility.Apply([configured], "RACE").Single().Visible);
    }

    [Fact]
    public void Old_profiles_default_to_all_sessions_and_new_flags_round_trip()
    {
        string oldJson = JsonSerializer.Serialize(Column);
        using var document = JsonDocument.Parse(oldJson);
        var oldFields = document.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith("ShowIn")).ToDictionary(p => p.Name, p => p.Value.Clone());
        var restored = JsonSerializer.Deserialize<ColumnDefinition>(JsonSerializer.Serialize(oldFields))!;
        Assert.True(restored.ShowInPractice && restored.ShowInQualify && restored.ShowInRace);
        var limited = Column with { ShowInRace = false };
        Assert.Equal(limited, JsonSerializer.Deserialize<ColumnDefinition>(JsonSerializer.Serialize(limited)));
    }
}
