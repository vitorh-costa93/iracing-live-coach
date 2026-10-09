using System.Globalization;
using System.Text;

namespace Ams2.Shared.Victory;

public enum VictoryTheme { Default, Senna, Barrichello, Massa }

/// <summary>
/// Escolhe o tema da vitoria pelo NOME do piloto (o nome de exibicao do carro em uso). Funcao pura.
/// Normaliza sem acento e em minusculas e quebra em palavras (qualquer nao-letra separa: "R. Barrichello", "FELIPE MASSA").
/// Casa pelo SOBRENOME: "senna" e "barrichello" (tambem dentro de uma palavra, ex.: "AyrtonSenna"); "massa" so como palavra inteira
/// (para nao pegar "Massaro"). Primeiro nome sozinho ("Ayrton", "Rubens", "Felipe") NAO casa: e comum demais para ser confiavel.
/// Prioridade (um nome com dois sobrenomes): Senna, Barrichello, Massa. Qualquer outro nome = Padrao.
/// </summary>
public static class VictoryThemeSelector
{
    public static VictoryTheme Select(string? name)
    {
        var words = Words(Normalize(name));
        if (words.Any(w => w.Contains("senna", StringComparison.Ordinal))) return VictoryTheme.Senna;
        if (words.Any(w => w.Contains("barrichello", StringComparison.Ordinal))) return VictoryTheme.Barrichello;
        if (words.Any(w => w == "massa")) return VictoryTheme.Massa;
        return VictoryTheme.Default;
    }

    /// <summary>Sem acento (decomposicao + remocao de marcas) e minusculas.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    static List<string> Words(string s)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetter(ch)) cur.Append(ch);
            else if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }
}
