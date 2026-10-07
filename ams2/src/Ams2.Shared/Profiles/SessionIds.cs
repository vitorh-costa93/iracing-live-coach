namespace Ams2.Shared.Profiles;

/// <summary>
/// Grupos de sessao em que um widget aparece (<see cref="WidgetSettings.Sessions"/>), gravados no perfil como texto.
/// O mapeamento a partir do tipo de sessao do jogo fica no Core (<c>Ams2.Core.SessionGroups</c>): Practice/Test/TimeAttack = practice,
/// Qualify = qualify, FormationLap/Race = race; sessao invalida ou sem dados = nao filtra.
/// </summary>
public static class SessionIds
{
    public const string Practice = "practice", Qualify = "qualify", Race = "race";

    /// <summary>Todos os grupos, na ordem canonica (Treino, Classificacao, Corrida).</summary>
    public static readonly IReadOnlyList<string> All = [Practice, Qualify, Race];

    /// <summary>Rotulos pt-BR do Control Center.</summary>
    public static string Label(string id) => id switch { Practice => "Treino", Qualify => "Classificação", Race => "Corrida", _ => id };

    /// <summary>Ids conhecidos, sem repeticao e na ordem canonica (case-insensitive); null ou nenhum conhecido = null.</summary>
    public static string[]? Canonical(IEnumerable<string>? ids)
    {
        if (ids is null) return null;
        var set = new HashSet<string>(ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var list = All.Where(set.Contains).ToArray();
        return list.Length == 0 ? null : list;
    }

    /// <summary>Mesmo conjunto (ordem e caixa ignoradas)?</summary>
    public static bool SameSet(IEnumerable<string> a, IEnumerable<string> b)
        => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
}
