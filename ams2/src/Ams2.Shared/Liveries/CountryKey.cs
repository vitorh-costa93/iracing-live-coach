namespace Ams2.Shared.Liveries;

/// <summary>
/// Pais do piloto como o jogo escreve em CustomAIDrivers (sigla de 3 letras, ex. "FIN", ou nome em ingles) -> chave do PNG de bandeira
/// ("fi", "gb-eng") em Assets\Flags do overlay (os mesmos arquivos do V3). Pais desconhecido = null: a legenda fica sem bandeira, nunca com chute.
/// </summary>
public static class CountryKey
{
    // Siglas IOC/ISO-3/FIFA mais usadas no automobilismo -> ISO-3166 alfa-2 (UK: bandeiras das nacoes).
    static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARG"] = "ar", ["AUS"] = "au", ["AUT"] = "at", ["BEL"] = "be", ["BRA"] = "br", ["BUL"] = "bg", ["BGR"] = "bg", ["CAN"] = "ca",
        ["CHI"] = "cl", ["CHL"] = "cl", ["CHN"] = "cn", ["COL"] = "co", ["CRO"] = "hr", ["HRV"] = "hr", ["CZE"] = "cz", ["DEN"] = "dk", ["DNK"] = "dk",
        ["ECU"] = "ec", ["EST"] = "ee", ["FIN"] = "fi", ["FRA"] = "fr", ["GER"] = "de", ["DEU"] = "de", ["GBR"] = "gb", ["UK"] = "gb",
        ["ENG"] = "gb-eng", ["SCO"] = "gb-sct", ["WAL"] = "gb-wls", ["NIR"] = "gb-nir", ["GRE"] = "gr", ["GRC"] = "gr", ["HKG"] = "hk",
        ["HUN"] = "hu", ["IND"] = "in", ["INA"] = "id", ["IDN"] = "id", ["IRL"] = "ie", ["IRE"] = "ie", ["ISR"] = "il", ["ITA"] = "it",
        ["JPN"] = "jp", ["KOR"] = "kr", ["LAT"] = "lv", ["LVA"] = "lv", ["LTU"] = "lt", ["LUX"] = "lu", ["MAS"] = "my", ["MYS"] = "my",
        ["MEX"] = "mx", ["MON"] = "mc", ["MCO"] = "mc", ["NED"] = "nl", ["NLD"] = "nl", ["NZL"] = "nz", ["NOR"] = "no", ["PER"] = "pe",
        ["POL"] = "pl", ["POR"] = "pt", ["PRT"] = "pt", ["ROU"] = "ro", ["ROM"] = "ro", ["RSA"] = "za", ["ZAF"] = "za", ["RUS"] = "ru",
        ["SIN"] = "sg", ["SGP"] = "sg", ["SVK"] = "sk", ["SLO"] = "si", ["SVN"] = "si", ["ESP"] = "es", ["SWE"] = "se", ["SUI"] = "ch",
        ["CHE"] = "ch", ["THA"] = "th", ["TUR"] = "tr", ["UKR"] = "ua", ["UAE"] = "ae", ["ARE"] = "ae", ["USA"] = "us", ["URU"] = "uy",
        ["URY"] = "uy", ["VEN"] = "ve", ["SRB"] = "rs", ["PAR"] = "py", ["PRY"] = "py", ["BOL"] = "bo", ["MAR"] = "ma", ["QAT"] = "qa",
        ["KSA"] = "sa", ["SAU"] = "sa", ["BHR"] = "bh", ["BLR"] = "by", ["AZE"] = "az", ["KAZ"] = "kz", ["PHI"] = "ph", ["PHL"] = "ph",
        ["TWN"] = "tw", ["TPE"] = "tw", ["CRC"] = "cr", ["CUB"] = "cu", ["DOM"] = "do", ["PAN"] = "pa", ["GUA"] = "gt", ["ISL"] = "is",
    };

    // Nomes em ingles (e variantes comuns) -> chave.
    static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Argentina"] = "ar", ["Australia"] = "au", ["Austria"] = "at", ["Belgium"] = "be", ["Brazil"] = "br", ["Brasil"] = "br", ["Bulgaria"] = "bg",
        ["Canada"] = "ca", ["Chile"] = "cl", ["China"] = "cn", ["Colombia"] = "co", ["Croatia"] = "hr", ["Czech Republic"] = "cz", ["Czechia"] = "cz",
        ["Denmark"] = "dk", ["Ecuador"] = "ec", ["Estonia"] = "ee", ["Finland"] = "fi", ["France"] = "fr", ["Germany"] = "de",
        ["Great Britain"] = "gb", ["United Kingdom"] = "gb", ["England"] = "gb-eng", ["Scotland"] = "gb-sct", ["Wales"] = "gb-wls",
        ["Northern Ireland"] = "gb-nir", ["Greece"] = "gr", ["Hong Kong"] = "hk", ["Hungary"] = "hu", ["India"] = "in", ["Indonesia"] = "id",
        ["Ireland"] = "ie", ["Israel"] = "il", ["Italy"] = "it", ["Japan"] = "jp", ["South Korea"] = "kr", ["Korea"] = "kr", ["Latvia"] = "lv",
        ["Lithuania"] = "lt", ["Luxembourg"] = "lu", ["Malaysia"] = "my", ["Mexico"] = "mx", ["Monaco"] = "mc", ["Netherlands"] = "nl",
        ["The Netherlands"] = "nl", ["Holland"] = "nl", ["New Zealand"] = "nz", ["Norway"] = "no", ["Peru"] = "pe", ["Poland"] = "pl",
        ["Portugal"] = "pt", ["Romania"] = "ro", ["South Africa"] = "za", ["Russia"] = "ru", ["Singapore"] = "sg", ["Slovakia"] = "sk",
        ["Slovenia"] = "si", ["Spain"] = "es", ["Sweden"] = "se", ["Switzerland"] = "ch", ["Thailand"] = "th", ["Turkey"] = "tr",
        ["Ukraine"] = "ua", ["United Arab Emirates"] = "ae", ["United States"] = "us", ["USA"] = "us", ["Uruguay"] = "uy", ["Venezuela"] = "ve",
        ["Serbia"] = "rs", ["Paraguay"] = "py", ["Bolivia"] = "bo", ["Morocco"] = "ma", ["Qatar"] = "qa", ["Saudi Arabia"] = "sa", ["Bahrain"] = "bh",
        ["Belarus"] = "by", ["Azerbaijan"] = "az", ["Kazakhstan"] = "kz", ["Philippines"] = "ph", ["Taiwan"] = "tw", ["Costa Rica"] = "cr",
        ["Cuba"] = "cu", ["Dominican Republic"] = "do", ["Panama"] = "pa", ["Guatemala"] = "gt", ["Iceland"] = "is",
    };

    /// <summary>Chave do PNG de bandeira (minuscula, ex. "br", "gb-eng") ou null se o pais for vazio/desconhecido.</summary>
    public static string? Of(string? country)
    {
        string c = (country ?? "").Trim();
        if (c.Length == 0) return null;
        if (ByCode.TryGetValue(c, out var k) || ByName.TryGetValue(c, out k)) return k;
        // ISO-3166 alfa-2 ja pronto ("FI", "br"): a existencia do PNG decide na hora de desenhar.
        if (c.Length == 2 && char.IsLetter(c[0]) && char.IsLetter(c[1])) return c.ToLowerInvariant();
        return null;
    }
}
