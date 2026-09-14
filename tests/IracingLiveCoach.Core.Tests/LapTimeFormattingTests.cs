// tests/IracingLiveCoach.Core.Tests/LapTimeFormattingTests.cs
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class LapTimeFormattingTests
{
    [Theory]
    [InlineData(92.345, "1:32.345")]
    [InlineData(45.0, "0:45.000")]
    [InlineData(125.678, "2:05.678")]
    [InlineData(9.5, "0:09.500")]
    public void Format_renders_minutes_seconds_milliseconds(double seconds, string expected)
    {
        Assert.Equal(expected, LapTimeFormatting.Format(seconds));
    }
}
