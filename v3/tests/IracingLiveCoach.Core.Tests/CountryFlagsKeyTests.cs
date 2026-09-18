using System.IO;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class CountryFlagsKeyTests
{
    [Theory]
    [InlineData("Brazil", "br")]
    [InlineData("France", "fr")]
    [InlineData("United Kingdom", "gb")]
    [InlineData("England", "gb-eng")]
    [InlineData("Scotland", "gb-sct")]
    [InlineData("Wales", "gb-wls")]
    [InlineData("Northern Ireland", "gb-nir")]
    [InlineData("Korea, Republic of", "kr")]
    [InlineData("Türkiye", "tr")]
    [InlineData("Taiwan", "tw")]
    [InlineData("New Zealand", "nz")]
    [InlineData("Ivory Coast", "ci")]
    public void Name_round_trips_to_asset_key(string name, string expectedKey)
    {
        Assert.Equal(expectedKey, CountryFlags.ToAssetKey(CountryFlags.ToEmoji(name)));
    }

    [Fact]
    public void Fallback_and_unknown_have_no_asset_key()
    {
        Assert.Null(CountryFlags.ToAssetKey(CountryFlags.ToEmoji("Atlantis")));
        Assert.Null(CountryFlags.ToAssetKey(null));
        Assert.Null(CountryFlags.ToAssetKey(""));
    }

    [Fact]
    public void Every_mapped_country_has_a_bundled_flag_image()
    {
        // Guards the table against drifting from the shipped assets (a mapped country with no PNG
        // would silently render an empty cell for those drivers).
        string flags = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "IracingLiveCoach.OverlayHost", "Assets", "Flags"));
        Assert.True(Directory.Exists(flags), flags);
        foreach (var name in new[] { "Brazil", "Germany", "Japan", "Australia", "South Africa", "Argentina", "Spain", "Italy", "Poland", "Taiwan", "England" })
        {
            string key = CountryFlags.ToAssetKey(CountryFlags.ToEmoji(name))!;
            Assert.True(File.Exists(Path.Combine(flags, key + ".png")), key);
        }
    }
}
