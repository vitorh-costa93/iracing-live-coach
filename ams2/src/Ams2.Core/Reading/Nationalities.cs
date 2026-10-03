namespace Ams2.Core.Reading;

/// <summary>
/// Pais do piloto como ISO 3166-1 alpha-2 minusculo (mesmo nome dos PNG de bandeira). "" = desconhecido (sem bandeira).
/// Achado empirico (02/10/2026, dump v14 e sessao ao vivo, 18 pilotos): o AMS2 NAO preenche <c>mNationalities[]</c>
/// (offset 20316) -- todos os IDs vem 0. Por isso a resolucao e em duas camadas: (1) tabela ID->ISO, que fica vazia
/// ate algum build do jogo passar a expor IDs (ID desconhecido = sem bandeira; nao ha enum oficial conferida);
/// (2) tabela por nome para os pilotos fixos da grade de IA do AMS2, deduzida dos nomes.
/// </summary>
public static class Nationalities
{
    /// <summary>ID bruto de <c>mNationalities[]</c> -> ISO. Vazia de proposito: o jogo manda 0 em todos os pilotos.</summary>
    static readonly Dictionary<uint, string> ById = [];

    static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rubens Barrichello"] = "br", ["Felipe Massa"] = "br", ["Vitor COSTA"] = "br",
        ["Gianni Fisco"] = "it", ["Jovanni Torio"] = "it",
        ["Matt Weaver"] = "au", ["Jake Villain"] = "au",
        ["Markell Fenstermacher"] = "de", ["Rulf Fenstermacher"] = "de", ["Nick Heinrich"] = "de",
        ["Tatsumi Sakai"] = "jp", ["Naresh Kaushalya"] = "in",
        ["Chris Arends"] = "nl", ["Pavel Fischer"] = "cz", ["Telmo Moreira"] = "pt",
        ["Dave Coulter"] = "gb", ["Tony Davis"] = "gb",
    };

    public static string Resolve(uint id, string driverName)
    {
        if (id != 0 && ById.TryGetValue(id, out var iso)) return iso;
        return ByName.TryGetValue(driverName.Trim(), out iso) ? iso : "";
    }

    /// <summary>Inclui IDs conhecidos (para testes ou quando um build do AMS2 passar a expor a enum).</summary>
    public static void RegisterId(uint id, string iso) => ById[id] = iso;
}
