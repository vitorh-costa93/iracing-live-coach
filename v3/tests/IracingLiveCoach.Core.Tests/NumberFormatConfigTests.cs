using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class NumberFormatConfigTests
{
    [Theory]
    [InlineData(IRatingFormat.Full, 4820, "4.820")]
    [InlineData(IRatingFormat.Full, 950, "950")]
    [InlineData(IRatingFormat.Thousands, 4820, "4.8k")]
    [InlineData(IRatingFormat.Thousands, 1000, "1.0k")]
    [InlineData(IRatingFormat.Thousands, 940, "0.9k")]
    [InlineData(IRatingFormat.Thousands, 3965, "3.9k")] // Kapps truncates (print 24/09/2026)
    [InlineData(IRatingFormat.Thousands, 3469, "3.4k")]
    [InlineData(IRatingFormat.Thousands, 0, "0.0k")]    // AI
    [InlineData(IRatingFormat.Plain, 4820, "4820")]
    [InlineData(IRatingFormat.Plain, 950, "950")]
    public void FormatIRating_follows_config(IRatingFormat format, int value, string expected)
    {
        var config = new NumberFormatConfig(format, SafetyRatingFormat.LetterAndNumber);
        Assert.Equal(expected, config.FormatIRating(value));
    }

    [Theory]
    [InlineData(SafetyRatingFormat.LetterAndNumber, "A 4.12", "A4.1")]
    [InlineData(SafetyRatingFormat.LetterAndNumber, "B 1.16", "B1.1")] // Kapps truncates (print 24/09/2026)
    [InlineData(SafetyRatingFormat.LetterAndNumber, "A 2.37", "A2.3")]
    [InlineData(SafetyRatingFormat.LetterAndNumber, "R 0.00", "R0.0")]
    [InlineData(SafetyRatingFormat.LetterAndNumber, "Pro", "Pro")]
    [InlineData(SafetyRatingFormat.NumberOnly, "A 4.12", "4.1")]
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
    [InlineData(NameDisplayFormat.FirstLast, "Vitor Hugo Da Costa", "Vitor Costa")]
    [InlineData(NameDisplayFormat.FirstLast, "Pace Car", "Pace Car")]
    [InlineData(NameDisplayFormat.FirstLast, "Madonna", "Madonna")]
    public void NameDisplay_formats_full_name(NameDisplayFormat format, string name, string expected)
    {
        Assert.Equal(expected, NameDisplay.Format(name, format));
    }

    [Theory]
    [InlineData("R 0.00", "#DE251B")]
    [InlineData("A 2.37", "#006EFF")]
    [InlineData("B 1.16", "#33CC00")]
    [InlineData("", null)]
    public void License_pill_colour_follows_the_letter_like_kapps(string lic, string? hex) =>
        Assert.Equal(hex, LicenseStyle.ColorHex(lic));
}
