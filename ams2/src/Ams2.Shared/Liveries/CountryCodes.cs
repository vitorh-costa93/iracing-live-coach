namespace Ams2.Shared.Liveries;

/// <summary>Pais do jogo (ISO alpha-3, ex. "FIN") -> ISO alpha-2 minusculo, que nomeia os PNGs de bandeira (ex. "fi"). Sem correspondencia = null (sem bandeira).</summary>
public static class CountryCodes
{
    static readonly Dictionary<string, string> A3ToA2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARG"] = "ar", ["AUS"] = "au", ["AUT"] = "at", ["AZE"] = "az", ["BEL"] = "be", ["BGR"] = "bg", ["BHR"] = "bh", ["BRA"] = "br",
        ["CAN"] = "ca", ["CHE"] = "ch", ["CHL"] = "cl", ["CHN"] = "cn", ["COL"] = "co", ["CZE"] = "cz", ["DEU"] = "de", ["DNK"] = "dk",
        ["ECU"] = "ec", ["ESP"] = "es", ["EST"] = "ee", ["FIN"] = "fi", ["FRA"] = "fr", ["GBR"] = "gb", ["GRC"] = "gr", ["HKG"] = "hk",
        ["HRV"] = "hr", ["HUN"] = "hu", ["IDN"] = "id", ["IND"] = "in", ["IRL"] = "ie", ["ISR"] = "il", ["ITA"] = "it", ["JPN"] = "jp",
        ["KOR"] = "kr", ["LTU"] = "lt", ["LUX"] = "lu", ["LVA"] = "lv", ["MCO"] = "mc", ["MEX"] = "mx", ["MYS"] = "my", ["NLD"] = "nl",
        ["NOR"] = "no", ["NZL"] = "nz", ["PER"] = "pe", ["POL"] = "pl", ["PRT"] = "pt", ["ROU"] = "ro", ["RUS"] = "ru", ["SAU"] = "sa",
        ["SGP"] = "sg", ["SVK"] = "sk", ["SVN"] = "si", ["SWE"] = "se", ["THA"] = "th", ["TUR"] = "tr", ["TWN"] = "tw", ["UKR"] = "ua",
        ["URY"] = "uy", ["USA"] = "us", ["VEN"] = "ve", ["ZAF"] = "za", ["ARE"] = "ae", ["PRY"] = "py", ["BOL"] = "bo", ["CRI"] = "cr",
        ["PAN"] = "pa", ["GTM"] = "gt", ["DOM"] = "do", ["PRI"] = "pr", ["QAT"] = "qa", ["BLR"] = "by", ["SRB"] = "rs", ["ISL"] = "is",
        // codigos nao-ISO comuns em listas de automobilismo
        ["GER"] = "de", ["NED"] = "nl", ["SUI"] = "ch", ["POR"] = "pt", ["SPA"] = "es", ["ENG"] = "gb-eng", ["UK"] = "gb", ["JAP"] = "jp",
    };

    // Codigos extras (IOC/FIFA, subdivisoes do Reino Unido) que a tabela acima nao cobre; TryAdd: a tabela principal manda.
    static readonly Dictionary<string, string> Extra = new(StringComparer.OrdinalIgnoreCase)
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

    static CountryCodes()
    {
        foreach (var kv in Extra) A3ToA2.TryAdd(kv.Key, kv.Value);
    }

    /// <summary>"FIN" -> "fi", "England" -> "gb-eng" (aceita alpha-2/chave ja pronta, ex. "fi", "gb-eng"); null se desconhecido.</summary>
    public static string? ToAlpha2(string? country)
    {
        string c = country?.Trim() ?? "";
        if (c.Length == 0) return null;
        if (A3ToA2.TryGetValue(c, out var a2) || ByName.TryGetValue(c, out a2)) return a2;
        string lower = c.ToLowerInvariant();
        return (c.Length == 2 && c.All(char.IsAsciiLetter) || lower.StartsWith("gb-", StringComparison.Ordinal)) && A3ToA2.ContainsValue(lower) ? lower : null;
    }

    /// <summary>Todos os alpha-2 conhecidos (para conferir os PNGs embarcados).</summary>
    public static IReadOnlyCollection<string> KnownAlpha2 => A3ToA2.Values.Distinct().ToList();
}
