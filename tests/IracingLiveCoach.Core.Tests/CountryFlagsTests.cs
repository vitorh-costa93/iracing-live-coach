using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class CountryFlagsTests
{
    [Theory]
    [InlineData("Brazil", "🇧🇷")]
    [InlineData("United States", "🇺🇸")]
    [InlineData("United Kingdom", "🇬🇧")]
    [InlineData("Germany", "🇩🇪")]
    [InlineData("Portugal", "🇵🇹")]
    public void ToEmoji_maps_known_country_names(string countryName, string expectedEmoji)
    {
        Assert.Equal(expectedEmoji, CountryFlags.ToEmoji(countryName));
    }

    [Fact]
    public void ToEmoji_returns_a_globe_fallback_for_an_unrecognized_name()
    {
        Assert.Equal("🌐", CountryFlags.ToEmoji("Some Made-Up Place"));
    }

    [Fact]
    public void ToEmoji_returns_the_fallback_for_null_or_empty()
    {
        Assert.Equal("🌐", CountryFlags.ToEmoji(null));
        Assert.Equal("🌐", CountryFlags.ToEmoji(""));
    }

    [Fact]
    public void ToEmoji_is_case_insensitive()
    {
        Assert.Equal("🇧🇷", CountryFlags.ToEmoji("brazil"));
        Assert.Equal("🇧🇷", CountryFlags.ToEmoji("BRAZIL"));
    }
}
