using System.Collections.Generic;
using System.Text;

namespace IracingLiveCoach.Core;

/// <summary>Maps a driver's FlairName (the country display name iRacing publishes on
/// DriverInfo.Drivers[], e.g. "Brazil") to a flag emoji string that identifies the country, which
/// the overlay then resolves to a bundled flag image (Assets/Flags/&lt;code&gt;.png) -- the emoji is
/// only the key, never rendered as text. Standard countries use the two-codepoint regional-indicator
/// pair for their ISO-3166 alpha-2 code; England/Scotland/Wales/Northern Ireland use the emoji
/// subdivision tag sequences. Anything not in the table gets an explicit globe fallback -- never a
/// blank glyph and never a guessed flag for an unrecognized name.</summary>
public static class CountryFlags
{
    private const string Fallback = "\U0001F310";

    private static readonly Dictionary<string, string> CodesByCountryName = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Afghanistan"] = "AF",
        ["Albania"] = "AL",
        ["Algeria"] = "DZ",
        ["Andorra"] = "AD",
        ["Angola"] = "AO",
        ["Antigua and Barbuda"] = "AG",
        ["Argentina"] = "AR",
        ["Armenia"] = "AM",
        ["Australia"] = "AU",
        ["Austria"] = "AT",
        ["Azerbaijan"] = "AZ",
        ["Bahamas"] = "BS",
        ["Bahrain"] = "BH",
        ["Bangladesh"] = "BD",
        ["Barbados"] = "BB",
        ["Belarus"] = "BY",
        ["Belgium"] = "BE",
        ["Belize"] = "BZ",
        ["Benin"] = "BJ",
        ["Bhutan"] = "BT",
        ["Bolivia"] = "BO",
        ["Bolivia, Plurinational State of"] = "BO",
        ["Bosnia and Herzegovina"] = "BA",
        ["Bosnia & Herzegovina"] = "BA",
        ["Botswana"] = "BW",
        ["Brazil"] = "BR",
        ["Brunei"] = "BN",
        ["Brunei Darussalam"] = "BN",
        ["Bulgaria"] = "BG",
        ["Burkina Faso"] = "BF",
        ["Burundi"] = "BI",
        ["Cambodia"] = "KH",
        ["Cameroon"] = "CM",
        ["Canada"] = "CA",
        ["Cape Verde"] = "CV",
        ["Cabo Verde"] = "CV",
        ["Central African Republic"] = "CF",
        ["Chad"] = "TD",
        ["Chile"] = "CL",
        ["China"] = "CN",
        ["Colombia"] = "CO",
        ["Comoros"] = "KM",
        ["Congo"] = "CG",
        ["Republic of the Congo"] = "CG",
        ["Democratic Republic of the Congo"] = "CD",
        ["Congo, The Democratic Republic of the"] = "CD",
        ["Costa Rica"] = "CR",
        ["Ivory Coast"] = "CI",
        ["Cote d'Ivoire"] = "CI",
        ["Côte d'Ivoire"] = "CI",
        ["Croatia"] = "HR",
        ["Cuba"] = "CU",
        ["Cyprus"] = "CY",
        ["Czech Republic"] = "CZ",
        ["Czechia"] = "CZ",
        ["Denmark"] = "DK",
        ["Djibouti"] = "DJ",
        ["Dominica"] = "DM",
        ["Dominican Republic"] = "DO",
        ["Ecuador"] = "EC",
        ["Egypt"] = "EG",
        ["El Salvador"] = "SV",
        ["Equatorial Guinea"] = "GQ",
        ["Eritrea"] = "ER",
        ["Estonia"] = "EE",
        ["Eswatini"] = "SZ",
        ["Swaziland"] = "SZ",
        ["Ethiopia"] = "ET",
        ["Fiji"] = "FJ",
        ["Finland"] = "FI",
        ["France"] = "FR",
        ["Gabon"] = "GA",
        ["Gambia"] = "GM",
        ["Georgia"] = "GE",
        ["Germany"] = "DE",
        ["Ghana"] = "GH",
        ["Greece"] = "GR",
        ["Grenada"] = "GD",
        ["Guatemala"] = "GT",
        ["Guinea"] = "GN",
        ["Guinea-Bissau"] = "GW",
        ["Guyana"] = "GY",
        ["Haiti"] = "HT",
        ["Honduras"] = "HN",
        ["Hong Kong"] = "HK",
        ["Hungary"] = "HU",
        ["Iceland"] = "IS",
        ["India"] = "IN",
        ["Indonesia"] = "ID",
        ["Iran"] = "IR",
        ["Iran, Islamic Republic of"] = "IR",
        ["Iraq"] = "IQ",
        ["Ireland"] = "IE",
        ["Israel"] = "IL",
        ["Italy"] = "IT",
        ["Jamaica"] = "JM",
        ["Japan"] = "JP",
        ["Jordan"] = "JO",
        ["Kazakhstan"] = "KZ",
        ["Kenya"] = "KE",
        ["Kiribati"] = "KI",
        ["North Korea"] = "KP",
        ["Korea, Democratic People's Republic of"] = "KP",
        ["South Korea"] = "KR",
        ["Korea"] = "KR",
        ["Korea, Republic of"] = "KR",
        ["Republic of Korea"] = "KR",
        ["Kosovo"] = "XK",
        ["Kuwait"] = "KW",
        ["Kyrgyzstan"] = "KG",
        ["Laos"] = "LA",
        ["Lao People's Democratic Republic"] = "LA",
        ["Latvia"] = "LV",
        ["Lebanon"] = "LB",
        ["Lesotho"] = "LS",
        ["Liberia"] = "LR",
        ["Libya"] = "LY",
        ["Liechtenstein"] = "LI",
        ["Lithuania"] = "LT",
        ["Luxembourg"] = "LU",
        ["Macau"] = "MO",
        ["Macao"] = "MO",
        ["Madagascar"] = "MG",
        ["Malawi"] = "MW",
        ["Malaysia"] = "MY",
        ["Maldives"] = "MV",
        ["Mali"] = "ML",
        ["Malta"] = "MT",
        ["Marshall Islands"] = "MH",
        ["Mauritania"] = "MR",
        ["Mauritius"] = "MU",
        ["Mexico"] = "MX",
        ["Micronesia"] = "FM",
        ["Moldova"] = "MD",
        ["Moldova, Republic of"] = "MD",
        ["Monaco"] = "MC",
        ["Mongolia"] = "MN",
        ["Montenegro"] = "ME",
        ["Morocco"] = "MA",
        ["Mozambique"] = "MZ",
        ["Myanmar"] = "MM",
        ["Burma"] = "MM",
        ["Namibia"] = "NA",
        ["Nauru"] = "NR",
        ["Nepal"] = "NP",
        ["Netherlands"] = "NL",
        ["The Netherlands"] = "NL",
        ["Holland"] = "NL",
        ["New Zealand"] = "NZ",
        ["Nicaragua"] = "NI",
        ["Niger"] = "NE",
        ["Nigeria"] = "NG",
        ["North Macedonia"] = "MK",
        ["Macedonia"] = "MK",
        ["Norway"] = "NO",
        ["Oman"] = "OM",
        ["Pakistan"] = "PK",
        ["Palau"] = "PW",
        ["Palestine"] = "PS",
        ["Palestine, State of"] = "PS",
        ["Panama"] = "PA",
        ["Papua New Guinea"] = "PG",
        ["Paraguay"] = "PY",
        ["Peru"] = "PE",
        ["Philippines"] = "PH",
        ["Poland"] = "PL",
        ["Portugal"] = "PT",
        ["Puerto Rico"] = "PR",
        ["Qatar"] = "QA",
        ["Romania"] = "RO",
        ["Russia"] = "RU",
        ["Russian Federation"] = "RU",
        ["Rwanda"] = "RW",
        ["Saint Kitts and Nevis"] = "KN",
        ["Saint Lucia"] = "LC",
        ["Saint Vincent and the Grenadines"] = "VC",
        ["Samoa"] = "WS",
        ["San Marino"] = "SM",
        ["Sao Tome and Principe"] = "ST",
        ["Saudi Arabia"] = "SA",
        ["Senegal"] = "SN",
        ["Serbia"] = "RS",
        ["Seychelles"] = "SC",
        ["Sierra Leone"] = "SL",
        ["Singapore"] = "SG",
        ["Slovakia"] = "SK",
        ["Slovenia"] = "SI",
        ["Solomon Islands"] = "SB",
        ["Somalia"] = "SO",
        ["South Africa"] = "ZA",
        ["South Sudan"] = "SS",
        ["Spain"] = "ES",
        ["Sri Lanka"] = "LK",
        ["Sudan"] = "SD",
        ["Suriname"] = "SR",
        ["Sweden"] = "SE",
        ["Switzerland"] = "CH",
        ["Syria"] = "SY",
        ["Syrian Arab Republic"] = "SY",
        ["Taiwan"] = "TW",
        ["Taiwan, Province of China"] = "TW",
        ["Tajikistan"] = "TJ",
        ["Tanzania"] = "TZ",
        ["Tanzania, United Republic of"] = "TZ",
        ["Thailand"] = "TH",
        ["Timor-Leste"] = "TL",
        ["East Timor"] = "TL",
        ["Togo"] = "TG",
        ["Tonga"] = "TO",
        ["Trinidad and Tobago"] = "TT",
        ["Tunisia"] = "TN",
        ["Turkey"] = "TR",
        ["Türkiye"] = "TR",
        ["Turkiye"] = "TR",
        ["Turkmenistan"] = "TM",
        ["Tuvalu"] = "TV",
        ["Uganda"] = "UG",
        ["Ukraine"] = "UA",
        ["United Arab Emirates"] = "AE",
        ["United Kingdom"] = "GB",
        ["Great Britain"] = "GB",
        ["UK"] = "GB",
        ["United States"] = "US",
        ["United States of America"] = "US",
        ["USA"] = "US",
        ["Uruguay"] = "UY",
        ["Uzbekistan"] = "UZ",
        ["Vanuatu"] = "VU",
        ["Vatican City"] = "VA",
        ["Holy See"] = "VA",
        ["Venezuela"] = "VE",
        ["Venezuela, Bolivarian Republic of"] = "VE",
        ["Vietnam"] = "VN",
        ["Viet Nam"] = "VN",
        ["Yemen"] = "YE",
        ["Zambia"] = "ZM",
        ["Zimbabwe"] = "ZW",
        ["Gibraltar"] = "GI",
        ["Greenland"] = "GL",
        ["Guam"] = "GU",
        ["Cayman Islands"] = "KY",
        ["Bermuda"] = "BM",
        ["Faroe Islands"] = "FO",
        ["Isle of Man"] = "IM",
        ["Jersey"] = "JE",
        ["Guernsey"] = "GG",
        ["New Caledonia"] = "NC",
        ["French Polynesia"] = "PF",
        ["Aruba"] = "AW",
        ["British Virgin Islands"] = "VG",
        ["U.S. Virgin Islands"] = "VI",
        ["England"] = "GB-ENG",
        ["Scotland"] = "GB-SCT",
        ["Wales"] = "GB-WLS",
        ["Northern Ireland"] = "GB-NIR",
    };

    public static string ToEmoji(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName)) return Fallback;
        if (!CodesByCountryName.TryGetValue(countryName.Trim(), out var code)) return Fallback;

        // Subdivision flags: black flag + tag letters + cancel tag (e.g. GB-ENG).
        if (code.Contains('-'))
        {
            var sb = new StringBuilder(char.ConvertFromUtf32(0x1F3F4));
            foreach (char c in code.Replace("-", "").ToLowerInvariant())
                sb.Append(char.ConvertFromUtf32(0xE0000 + c));
            sb.Append(char.ConvertFromUtf32(0xE007F));
            return sb.ToString();
        }

        // Each ASCII letter A-Z maps to a Unicode "Regional Indicator Symbol Letter" by offsetting
        // from U+1F1E6 ('A'); the emoji is the two-codepoint pair for the country's alpha-2 code.
        const int RegionalIndicatorBase = 0x1F1E6;
        var first = char.ConvertFromUtf32(RegionalIndicatorBase + (code[0] - 'A'));
        var second = char.ConvertFromUtf32(RegionalIndicatorBase + (code[1] - 'A'));
        return first + second;
    }

    /// <summary>Inverse of <see cref="ToEmoji"/>: the lower-case asset key ("br", "gb-eng") for an
    /// emoji produced by it, or null for the fallback / anything unrecognised.</summary>
    public static string? ToAssetKey(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return null;
        var runes = new List<int>();
        for (int i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1)
            runes.Add(char.ConvertToUtf32(emoji, i));

        if (runes.Count == 2 && runes[0] is >= 0x1F1E6 and <= 0x1F1FF && runes[1] is >= 0x1F1E6 and <= 0x1F1FF)
            return new string(new[] { (char)('a' + runes[0] - 0x1F1E6), (char)('a' + runes[1] - 0x1F1E6) });

        if (runes.Count > 2 && runes[0] == 0x1F3F4 && runes[^1] == 0xE007F)
        {
            var tag = new StringBuilder();
            foreach (int r in runes.Skip(1).Take(runes.Count - 2))
                if (r is >= 0xE0061 and <= 0xE007A) tag.Append((char)(r - 0xE0000));
            string t = tag.ToString();
            if (t.Length > 2) return t[..2] + "-" + t[2..];
        }
        return null;
    }
}
