using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class NameDisplayTests
{
    [Theory]
    [InlineData(NameDisplayFormat.Full, "Vitor Hugo Da Costa")]
    [InlineData(NameDisplayFormat.Abbreviated, "V. Costa")]
    [InlineData(NameDisplayFormat.FirstLast, "Vitor Costa")]
    public void Formats_a_multi_part_name(NameDisplayFormat format, string expected) =>
        Assert.Equal(expected, NameDisplay.Format("Vitor Hugo Da Costa", format));

    [Fact]
    public void First_last_keeps_a_single_word_name() =>
        Assert.Equal("Senna", NameDisplay.Format("Senna", NameDisplayFormat.FirstLast));

    [Fact]
    public void Existing_enum_values_are_unchanged_for_saved_profiles()
    {
        Assert.Equal(0, (int)NameDisplayFormat.Full);
        Assert.Equal(1, (int)NameDisplayFormat.Abbreviated);
        Assert.Equal(2, (int)NameDisplayFormat.FirstLast);
    }
}
