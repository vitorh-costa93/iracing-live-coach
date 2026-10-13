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
        ["GER"] = "de", ["NED"] = "nl", ["SUI"] = "ch", ["POR"] = "pt", ["SPA"] = "es", ["ENG"] = "gb", ["UK"] = "gb", ["JAP"] = "jp",
    };

    /// <summary>"FIN" -> "fi" (aceita alpha-2 ja pronto, ex. "fi"); null se desconhecido.</summary>
    public static string? ToAlpha2(string? country)
    {
        string c = country?.Trim() ?? "";
        if (c.Length == 0) return null;
        if (A3ToA2.TryGetValue(c, out var a2)) return a2;
        return c.Length == 2 && c.All(char.IsAsciiLetter) && A3ToA2.ContainsValue(c.ToLowerInvariant()) ? c.ToLowerInvariant() : null;
    }

    /// <summary>Todos os alpha-2 conhecidos (para conferir os PNGs embarcados).</summary>
    public static IReadOnlyCollection<string> KnownAlpha2 => A3ToA2.Values.Distinct().ToList();
}
