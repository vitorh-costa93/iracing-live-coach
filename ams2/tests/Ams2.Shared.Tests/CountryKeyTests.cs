using Ams2.Shared.Liveries;
using Xunit;

namespace Ams2.Shared.Tests;

public class CountryKeyTests
{
    [Theory]
    [InlineData("FIN", "fi")]
    [InlineData("fin", "fi")]
    [InlineData("GER", "de")]
    [InlineData("NED", "nl")]
    [InlineData("GBR", "gb")]
    [InlineData("ENG", "gb-eng")]
    [InlineData("Brazil", "br")]
    [InlineData(" Italy ", "it")]
    [InlineData("MX", "mx")]
    public void Maps_game_codes_names_and_iso2_to_the_flag_asset_key(string country, string key)
        => Assert.Equal(key, CountryKey.Of(country));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Atlantis")]
    [InlineData("XYZZY")]
    public void Unknown_or_empty_country_has_no_flag(string? country) => Assert.Null(CountryKey.Of(country));

    [Fact]
    public void Every_mapped_key_has_a_bundled_flag_png()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost", "Assets", "Flags"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = Path.Combine(dir!.FullName, "src", "Ams2.OverlayHost", "Assets", "Flags");
        string[] samples = ["FIN", "GBR", "ENG", "ITA", "MEX", "BRA", "GER", "NED", "ESP", "FRA", "AUS", "USA", "JPN", "CAN", "AUT", "DEN", "THA", "MON"];
        foreach (var c in samples)
            Assert.True(File.Exists(Path.Combine(flags, CountryKey.Of(c) + ".png")), c);
    }
}
