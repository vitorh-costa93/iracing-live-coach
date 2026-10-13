using Ams2.Shared.Liveries;

namespace Ams2.Shared.Tests;

public class DriverCountryTests
{
    [Theory]
    [InlineData("ITA", "it")] [InlineData("FRA", "fr")] [InlineData("GBR", "gb")] [InlineData("BRA", "br")] [InlineData("DEU", "de")]
    [InlineData("FIN", "fi")] [InlineData("JPN", "jp")] [InlineData("AUT", "at")] [InlineData("BEL", "be")] [InlineData("USA", "us")]
    [InlineData("SWE", "se")] [InlineData("CAN", "ca")] [InlineData("ESP", "es")] [InlineData("AUS", "au")] [InlineData("NLD", "nl")]
    [InlineData("CHE", "ch")] [InlineData("PRT", "pt")] [InlineData("POL", "pl")] [InlineData("COL", "co")] [InlineData("ARG", "ar")]
    [InlineData(" fin ", "fi")] [InlineData("fi", "fi")]
    public void Alpha3_maps_to_lowercase_alpha2(string input, string expected) => Assert.Equal(expected, CountryCodes.ToAlpha2(input));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("XXX")] [InlineData("Q")] [InlineData("zz")]
    public void Unknown_or_empty_has_no_flag(string? input) => Assert.Null(CountryCodes.ToAlpha2(input));

    [Fact]
    public void Every_known_alpha2_has_a_bundled_flag_png()
    {
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ams2.OverlayHost", "Assets", "Flags"));
        Assert.True(Directory.Exists(dir), dir);
        foreach (var a2 in CountryCodes.KnownAlpha2) Assert.True(File.Exists(Path.Combine(dir, a2 + ".png")), a2);
    }

    static DriverCountryResolver Resolver() => new([
        new LiveryEntry("L1", "Kimi Räikkönen", "FIN", "McLaren", "m"),
        new LiveryEntry("L2", "Rubens Barrichello", "BRA", "Ferrari", "m"),
        new LiveryEntry("L3", "Michael Schumacher", "DEU", "Ferrari", "m"),
        new LiveryEntry("L4", "Ralf Schumacher", "DEU", "Williams", "m"),
        new LiveryEntry("L5", "Mick Smith", "GBR", "", "m"),
        new LiveryEntry("L6", "Mark Smith", "USA", "", "m"),
        new LiveryEntry("L7", "Sem Pais", "", "", "m"),
    ]);

    [Fact]
    public void Resolves_by_full_name_ignoring_accents_and_case()
    {
        var r = Resolver();
        Assert.Equal("FIN", r.Resolve("Kimi Raikkonen"));
        Assert.Equal("FIN", r.Resolve("KIMI RÄIKKÖNEN"));
        Assert.Equal("BRA", r.Resolve("  rubens   barrichello "));
    }

    [Fact]
    public void Falls_back_to_initial_plus_surname_only_when_unambiguous()
    {
        var r = Resolver();
        Assert.Equal("BRA", r.Resolve("R. Barrichello"));
        Assert.Equal("FIN", r.Resolve("K Raikkonen"));
        Assert.Equal("DEU", r.Resolve("M. Schumacher")); // mesmo pais nos dois "S" -> sem conflito
        Assert.Equal("", r.Resolve("S. Smith"));         // sem inicial correspondente
        Assert.Equal("", r.Resolve("M. Smith"));         // Mick (GBR) x Mark (USA): conflito, nao chuta
    }

    [Fact]
    public void No_match_or_no_country_gives_empty()
    {
        var r = Resolver();
        Assert.Equal("", r.Resolve("Fulano de Tal"));
        Assert.Equal("", r.Resolve("Sem Pais"));
        Assert.Equal("", r.Resolve(""));
        Assert.Equal("", r.Resolve(null));
        Assert.Equal("BRA", r.Resolve("Rubens Barrichello")); // cache nao altera o resultado
        Assert.Equal("BRA", r.Resolve("Rubens Barrichello"));
    }
}
