namespace Ams2.Core.Calc.GpxSat;

/// <summary>
/// Retrato congelado por evento (ESPECIFICACAO-TEMAS secao 2.3): o valor e calculado uma unica vez no instante do evento
/// (<c>eventT</c>, p.ex. <see cref="GpxSplitTracker.LeaderLapT"/> = lider cruzou a linha) e devolvido igual ate o proximo evento,
/// sem atualizar posicoes/gaps/voltas ao vivo. Sem evento (<c>eventT</c> null) o valor e calculado na hora (previa e inicio da sessao).
/// Uma entrada por chave (p.ex. "str", "stp", "tower:standings"): telas diferentes nao se sobrescrevem.
/// </summary>
public sealed class GpxEventSnapshot
{
    readonly Dictionary<string, (double At, object Value)> _frozen = new(StringComparer.Ordinal);

    /// <summary>Valor congelado de <paramref name="key"/> para o evento <paramref name="eventT"/> (calcula com <paramref name="build"/> so na primeira chamada do evento).</summary>
    public T Get<T>(string key, double? eventT, Func<T> build) where T : notnull
    {
        if (eventT is not { } t) return build();
        if (_frozen.TryGetValue(key, out var f) && f.At == t && f.Value is T v) return v;
        var value = build();
        _frozen[key] = (t, value);
        return value;
    }

    /// <summary>Ja existe retrato de <paramref name="key"/> para o evento <paramref name="eventT"/>?</summary>
    public bool Has(string key, double? eventT) => eventT is { } t && _frozen.TryGetValue(key, out var f) && f.At == t;

    public void Reset() => _frozen.Clear();
}
