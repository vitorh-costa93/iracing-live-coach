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

    // Numero do carro (do "#12" do nome da pintura) pelo mesmo nome normalizado; nome com numeros diferentes entre pinturas = ambiguo (fora).
    readonly Dictionary<string, string> _numbers = new(StringComparer.Ordinal);
    static readonly System.Text.RegularExpressions.Regex NumberTag = new(@"#\s*(\d{1,3})\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Numero do carro no nome da pintura ("1986 McLaren #1 - A. Prost" -> "1"); "" se a pintura nao traz.</summary>
    public static string NumberOfLivery(string? livery)
        => NumberTag.Match(livery ?? "") is { Success: true } m ? m.Groups[1].Value.TrimStart('0') is { Length: > 0 } n ? n : "0" : "";

    /// <summary>Numero do carro do piloto <paramref name="gameName"/> pelo catalogo ("" = desconhecido ou ambiguo; nao inventa).</summary>
    public string ResolveNumber(string? gameName)
    {
        string n = Normalize(gameName);
        return _numbers.TryGetValue(n, out var r) ? r : "";
    }

    public DriverCountryResolver(IEnumerable<LiveryEntry> catalog)
    {
        var conflict = new HashSet<string>(StringComparer.Ordinal);
        var numberConflict = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in catalog)
        {
            string num = NumberOfLivery(e.Livery), who = Normalize(e.Driver);
            if (num.Length > 0 && who.Length > 0)
            {
                if (_numbers.TryGetValue(who, out var prevNum)) { if (prevNum != num) numberConflict.Add(who); }
                else _numbers[who] = num;
            }
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
        foreach (var k in numberConflict) _numbers.Remove(k);
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
