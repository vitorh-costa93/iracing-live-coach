using System.Collections.Generic;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class SessionVisibilityTests
{
    [Theory]
    [InlineData("RACE", SessionKind.Race)]
    [InlineData("OPEN QUALIFY", SessionKind.Qualify)]
    [InlineData("LONE QUALIFY", SessionKind.Qualify)]
    [InlineData("PRACTICE", SessionKind.Practice)]
    [InlineData("WARMUP", SessionKind.Practice)]
    [InlineData("OFFLINE TESTING", SessionKind.Practice)]
    public void Classify_buckets_known_session_types(string text, SessionKind expected) =>
        Assert.Equal(expected, SessionKinds.Classify(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SOMETHING NEW")]
    public void Classify_returns_null_for_unknown(string? text) => Assert.Null(SessionKinds.Classify(text));

    [Fact]
    public void IsVisible_hides_only_listed_kinds_and_never_on_unknown()
    {
        var config = new SessionVisibilityConfig(new Dictionary<string, List<string>> { ["radar"] = ["Practice", "Qualify"] });
        Assert.False(config.IsVisible("radar", SessionKind.Practice));
        Assert.True(config.IsVisible("radar", SessionKind.Race));
        Assert.True(config.IsVisible("radar", null));
        Assert.True(config.IsVisible("fuel", SessionKind.Practice));
    }

    [Theory]
    [InlineData("Lone Qualify", true)]
    [InlineData("LONE QUALIFY", true)]
    [InlineData("Open Qualify", false)]
    [InlineData("Race", false)]
    [InlineData(null, false)]
    public void Only_lone_qualifying_is_a_solo_session(string? type, bool solo) =>
        Assert.Equal(solo, SessionKinds.IsSolo(type));
}
