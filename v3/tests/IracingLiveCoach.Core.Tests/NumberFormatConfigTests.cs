using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class NumberFormatConfigTests
{
    [Theory]
    [InlineData(IRatingFormat.Full, 4820, "4.820")]
    [InlineData(IRatingFormat.Full, 950, "950")]
    [InlineData(IRatingFormat.Thousands, 4820, "4.8k")]
    [InlineData(IRatingFormat.Thousands, 1000, "1k")]
    [InlineData(IRatingFormat.Thousands, 940, "0.9k")]
    public void FormatIRating_follows_config(IRatingFormat format, int value, string expected)
    {
        var config = new NumberFormatConfig(format, SafetyRatingFormat.LetterAndNumber);
        Assert.Equal(expected, config.FormatIRating(value));
    }

    [Theory]
    [InlineData(SafetyRatingFormat.LetterAndNumber, "A 4.12", "A 4.12")]
    [InlineData(SafetyRatingFormat.NumberOnly, "A 4.12", "4.12")]
    [InlineData(SafetyRatingFormat.LetterOnly, "A 4.12", "A")]
    [InlineData(SafetyRatingFormat.NumberOnly, "Pro", "Pro")]
    [InlineData(SafetyRatingFormat.LetterOnly, "R 1.50", "R")]
    public void FormatLicense_splits_sdk_string(SafetyRatingFormat format, string sdk, string expected)
    {
        var config = new NumberFormatConfig(IRatingFormat.Full, format);
        Assert.Equal(expected, config.FormatLicense(sdk));
    }

    [Theory]
    [InlineData(NameDisplayFormat.Full, "Vitor Hugo Da Costa", "Vitor Hugo Da Costa")]
    [InlineData(NameDisplayFormat.Abbreviated, "Vitor Hugo Da Costa", "V. Costa")]
    [InlineData(NameDisplayFormat.Abbreviated, "Pace Car", "P. Car")]
    [InlineData(NameDisplayFormat.Abbreviated, "Madonna", "Madonna")]
    public void NameDisplay_formats_full_name(NameDisplayFormat format, string name, string expected)
    {
        Assert.Equal(expected, NameDisplay.Format(name, format));
    }
}
