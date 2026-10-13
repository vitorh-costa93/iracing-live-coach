using System.Globalization;
using System.Text;

namespace Ams2.Shared.Liveries;

/// <summary>
/// Pais dos pilotos adversarios pelo NOME que o jogo informa, contra o catalogo de pinturas (nome normalizado: sem acento, sem caixa).
/// Tenta o nome completo e, na falta, "inicial + sobrenome" (so quando nao ha conflito de paises). Sem correspondencia = "" (nao inventa).
/// Resultados (inclusive negativos) ficam em cache.
/// </summary>
public sealed class DriverCountryResolver
{
    readonly Dictionary<string, string> _full = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _short = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public DriverCountryResolver(IEnumerable<LiveryEntry> catalog)
    {
        var conflict = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in catalog)
        {
            string country = e.Country.Trim();
            if (country.Length == 0) continue;
            string full = Normalize(e.Driver);
            if (full.Length == 0) continue;
            _full.TryAdd(full, country);
            string? sk = ShortKey(full);
            if (sk is null) continue;
            if (_short.TryGetValue(sk, out var prev)) { if (!string.Equals(prev, country, StringComparison.OrdinalIgnoreCase)) conflict.Add(sk); }
            else _short[sk] = country;
        }
        foreach (var k in conflict) _short.Remove(k);
    }

    /// <summary>Pais (alpha-3 do catalogo) do piloto <paramref name="gameName"/>, ou "" se nao ha correspondencia.</summary>
    public string Resolve(string? gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return "";
        lock (_cache)
        {
            if (_cache.TryGetValue(gameName, out var hit)) return hit;
            string n = Normalize(gameName), r = "";
            if (!_full.TryGetValue(n, out r!))
            {
                string? sk = ShortKey(n);
                r = sk is not null && _short.TryGetValue(sk, out var s) ? s : "";
            }
            if (_cache.Count > 512) _cache.Clear();
            _cache[gameName] = r;
            return r;
        }
    }

    /// <summary>Minusculo, sem acentos, so letras/digitos separados por espaco unico.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>"kimi raikkonen" / "k raikkonen" -> "k raikkonen"; nome de uma palavra so: null.</summary>
    static string? ShortKey(string normalized)
    {
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : parts[0][..1] + " " + parts[^1];
    }
}
