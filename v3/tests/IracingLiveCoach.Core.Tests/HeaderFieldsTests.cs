using System;
using System.Linq;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class HeaderFieldsTests
{
    private static readonly SessionStatus Session = new("GT3", "RACE", 3, 24, "", "", 2150.0, 12);
    private static readonly PlayerCarStatus Player = new(54.5, "extensive usage", 92.345, 93.0, 22.2);
    private static readonly DateTime Now = new(2026, 9, 18, 13, 4, 0);

    [Fact]
    public void Compose_follows_list_order_and_visibility()
    {
        var fields = new[] { new HeaderFieldConfig("lap", true), new HeaderFieldConfig("type", true), new HeaderFieldConfig("sof", false) };
        Assert.Equal("LAP 3/24   RACE", HeaderFields.Compose(fields, Session, Player, Now));
    }

    [Fact]
    public void Compose_skips_fields_without_data_instead_of_placeholders()
    {
        var fields = new[] { new HeaderFieldConfig("bb", true), new HeaderFieldConfig("local", true) };
        Assert.Equal("LOCAL 13:04", HeaderFields.Compose(fields, null, null, Now));
    }

    [Fact]
    public void Text_formats_player_fields()
    {
        Assert.Equal("BB 54.5%", HeaderFields.Text("bb", Session, Player, Now));
        Assert.Equal("TRACK 22°C", HeaderFields.Text("track", Session, Player, Now));
        Assert.Equal("RUBBER EXTENSIVE USAGE", HeaderFields.Text("rubber", Session, Player, Now));
        Assert.Equal("BEST 1:32.345", HeaderFields.Text("best", Session, Player, Now));
        Assert.Equal("SOF 2.150", HeaderFields.Text("sof", Session, Player, Now));
        Assert.Equal("13:04", HeaderFields.Text("clock", Session, Player, Now));
    }

    [Fact]
    public void Incidents_show_the_count_and_the_session_limit_when_there_is_one()
    {
        Assert.Equal("INC 4x/17x", HeaderFields.Text("incidents", null, new PlayerCarStatus(null, null, null, null, null, 4, 17), Now));
        Assert.Equal("INC 0x", HeaderFields.Text("incidents", null, new PlayerCarStatus(null, null, null, null, null, 0, null), Now));
        Assert.Null(HeaderFields.Text("incidents", null, new PlayerCarStatus(null, null, null, null, null), Now));
    }

    [Fact]
    public void Complete_appends_missing_keys_hidden_and_drops_unknown()
    {
        var completed = HeaderFields.Complete([new("local", true), new("bogus", true)]);
        Assert.Equal("local", completed[0].Key);
        Assert.DoesNotContain(completed, f => f.Key == "bogus");
        Assert.Equal(HeaderFields.AllKeys.Count, completed.Count);
        Assert.All(completed.Skip(1), f => Assert.False(f.Visible));
    }

    [Fact]
    public void Timed_race_total_is_the_kapps_fractional_projection_truncated()
    {
        var timed = new SessionStatus("GT3", "RACE", 9, 12, "", "", 3000, 15, TotalLapsEstimated: true, TotalLapsProjected: 11.5779);
        Assert.Equal("LAP 9/≈11.57", HeaderFields.Text("lap", timed, null, DateTime.Now));
        var noProjection = timed with { TotalLapsProjected = null };
        Assert.Equal("LAP 9/≈12", HeaderFields.Text("lap", noProjection, null, DateTime.Now));
        var lapLimited = timed with { TotalLapsEstimated = false, TotalLaps = 30 };
        Assert.Equal("LAP 9/30", HeaderFields.Text("lap", lapLimited, null, DateTime.Now));
    }
}
