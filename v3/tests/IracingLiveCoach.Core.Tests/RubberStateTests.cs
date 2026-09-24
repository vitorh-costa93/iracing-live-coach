using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class RubberStateTests
{
    // Example event: practice with a real state, then sessions that carry it over.
    private static readonly Dictionary<int, string?> Event = new()
    {
        [0] = "high usage", [1] = "carry over", [2] = "carry over",
    };

    [Fact]
    public void Carry_over_takes_the_state_of_the_nearest_earlier_session() =>
        Assert.Equal("high usage", RubberState.Resolve(Event, 2));

    [Fact]
    public void A_real_state_is_shown_as_is() =>
        Assert.Equal("high usage", RubberState.Resolve(Event, 0));

    [Fact]
    public void Nearest_earlier_real_state_wins()
    {
        var states = new Dictionary<int, string?> { [0] = "clean", [1] = "moderately low usage", [2] = "Carry Over" };
        Assert.Equal("moderately low usage", RubberState.Resolve(states, 2));
    }

    [Fact]
    public void Nothing_to_inherit_keeps_carry_over_and_unknown_is_null()
    {
        Assert.Equal("carry over", RubberState.Resolve(new Dictionary<int, string?> { [0] = "carry over" }, 0));
        Assert.Null(RubberState.Resolve(Event, 7));
    }
}
