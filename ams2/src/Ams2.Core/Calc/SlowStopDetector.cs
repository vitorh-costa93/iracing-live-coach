namespace Ams2.Core.Calc;

/// <summary>Parada lenta do jogador: tempo parado, referencia usada (media das paradas anteriores ou o limite) e tempo perdido.</summary>
public sealed record SlowStop(double StopSeconds, double Reference, double Lost, bool FromHistory);

/// <summary>
/// Barra "SOBRENOME SLOW STOP -x.xs" do grafico de TV 2018 (Race Control). Regra (so a ultima parada concluida do jogador):
/// <list type="bullet">
/// <item>e lenta quando o tempo parado passa de <c>limit</c> (opcao slowStopLimit, "parada considerada lenta se passar desse tempo");</item>
/// <item>o valor mostrado e o tempo perdido = parada - referencia, onde a referencia e a media das ate <see cref="HistoryWindow"/> paradas
///   anteriores do proprio jogador na sessao (o ritmo normal de box dele com este carro); sem historico, a referencia e o proprio limite;</item>
/// <item>com historico, uma parada acima do limite mas nao mais lenta que a media (p.ex. todas as paradas com reabastecimento longo) nao e lenta.</item>
/// </list>
/// Abaixo de 0,05 s perdidos (mostraria "-0.0s") nao conta.
/// </summary>
public static class SlowStopDetector
{
    public const int HistoryWindow = 3;
    const double MinLost = 0.05;

    /// <summary>Avalia a ultima parada de <paramref name="stops"/> (tempos parados em ordem); null = sem parada ou parada normal.</summary>
    public static SlowStop? Evaluate(IReadOnlyList<double>? stops, double limit)
    {
        if (stops is null || stops.Count == 0) return null;
        double last = stops[^1];
        if (!double.IsFinite(last) || last <= limit) return null;
        int n = Math.Min(HistoryWindow, stops.Count - 1);
        double sum = 0;
        for (int i = stops.Count - 1 - n; i < stops.Count - 1; i++) sum += stops[i];
        double reference = n > 0 ? sum / n : limit;
        double lost = last - reference;
        return lost < MinLost ? null : new SlowStop(last, reference, lost, n > 0);
    }

    /// <summary>Tempo perdido no formato da TV: "-11.1s".</summary>
    public static string Format(double lost) => "-" + Math.Round(Math.Max(0, lost), 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
}
