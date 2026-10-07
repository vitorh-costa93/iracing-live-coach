using Ams2.OverlayHost.Data;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class LayoutPreviewTests
{
    [Fact]
    public void Preview_has_data_without_the_game_and_selects_quali_for_quali_widgets()
    {
        var race = LayoutPreview.ForWidget("fuel");
        Assert.True(race.Connected);
        Assert.NotNull(race.Fuel);
        Assert.NotEmpty(race.Standings);
        var quali = LayoutPreview.ForWidget("qualitower");
        Assert.Equal(SessionIds.Qualify, quali.SessionGroup);
        Assert.NotNull(quali.Quali);
    }

    [Fact]
    public void Preview_preserves_live_customization_and_does_not_change_saved_event_options()
    {
        var saved = new WidgetSettings { Id = "pittimer", TextScale = 1.5f, Font = "Arial", Columns = [], Options = new() { ["always"] = "false" } };
        var preview = LayoutPreview.Settings(saved);
        Assert.Equal(1.5f, preview.TextScale);
        Assert.Equal("Arial", preview.Font);
        Assert.Contains("always", preview.Columns!);
        Assert.Equal("true", preview.Options!["always"]);
        Assert.Empty(saved.Columns!);
        Assert.Equal("false", saved.Options!["always"]);
        Assert.Contains("laps", LayoutPreview.Settings(new WidgetSettings { Id = "fuel" }).Columns!);
    }
}
