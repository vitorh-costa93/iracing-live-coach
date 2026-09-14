using System.Collections.Generic;

namespace IracingLiveCoach.Core;

/// <summary>Maps a driver's FlairName (the country display name iRacing's 2025 Season 3 "flair"
/// feature publishes on DriverInfo.Drivers[], e.g. "Brazil") to a Unicode regional-indicator flag
/// emoji, rendered by the OS's own emoji font (Segoe UI Emoji on Windows) -- no bundled flag image
/// assets needed. Coverage is deliberately bounded to countries iRacing's own driver base
/// realistically spans, with an explicit globe fallback for anything not in the table -- never a
/// blank/missing glyph and never a guessed flag for an unrecognized name.</summary>
public static class CountryFlags
{
    private const string Fallback = "🌐";

    // Regional-indicator flag emoji are built from two Unicode "regional indicator symbol"
    // characters per ISO-3166-alpha-2 code (e.g. "BR" -> 🇧 🇷). This table lists the alpha-2 code
    // directly; ToEmoji() converts the code to the two-codepoint emoji at lookup time so this
    // table stays readable as plain country-name -> code pairs.
    private static readonly Dictionary<string, string> CodesByCountryName = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Brazil"] = "BR",
        ["United States"] = "US",
        ["United Kingdom"] = "GB",
        ["Canada"] = "CA",
        ["Germany"] = "DE",
        ["France"] = "FR",
        ["Italy"] = "IT",
        ["Spain"] = "ES",
        ["Portugal"] = "PT",
        ["Netherlands"] = "NL",
        ["Belgium"] = "BE",
        ["Australia"] = "AU",
        ["New Zealand"] = "NZ",
        ["Japan"] = "JP",
        ["Mexico"] = "MX",
        ["Argentina"] = "AR",
        ["Chile"] = "CL",
        ["Colombia"] = "CO",
        ["South Africa"] = "ZA",
        ["Sweden"] = "SE",
        ["Norway"] = "NO",
        ["Denmark"] = "DK",
        ["Finland"] = "FI",
        ["Poland"] = "PL",
        ["Austria"] = "AT",
        ["Switzerland"] = "CH",
        ["Ireland"] = "IE",
        ["Czech Republic"] = "CZ",
        ["Hungary"] = "HU",
        ["Greece"] = "GR",
        ["Turkey"] = "TR",
        ["Russia"] = "RU",
        ["India"] = "IN",
        ["China"] = "CN",
        ["South Korea"] = "KR",
        ["Indonesia"] = "ID",
        ["Malaysia"] = "MY",
        ["Singapore"] = "SG",
        ["Thailand"] = "TH",
        ["Philippines"] = "PH",
        ["United Arab Emirates"] = "AE",
        ["Saudi Arabia"] = "SA",
        ["Israel"] = "IL",
        ["Estonia"] = "EE",
        ["Latvia"] = "LV",
        ["Lithuania"] = "LT",
        ["Romania"] = "RO",
        ["Bulgaria"] = "BG",
        ["Croatia"] = "HR",
        ["Slovakia"] = "SK",
        ["Slovenia"] = "SI",
        ["Ukraine"] = "UA",
    };

    public static string ToEmoji(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName)) return Fallback;
        if (!CodesByCountryName.TryGetValue(countryName, out var code)) return Fallback;

        // Each ASCII letter A-Z maps to a Unicode "Regional Indicator Symbol Letter" by offsetting
        // from U+1F1E6 ('A'); the emoji is the two-codepoint pair for the country's alpha-2 code.
        const int RegionalIndicatorBase = 0x1F1E6;
        var first = char.ConvertFromUtf32(RegionalIndicatorBase + (code[0] - 'A'));
        var second = char.ConvertFromUtf32(RegionalIndicatorBase + (code[1] - 'A'));
        return first + second;
    }
}
