namespace Ams2.Core.Calc.GpxSat;

/// <summary>Fase do Qualy Board mesclado (ESPECIFICACAO-TEMAS 3.2/3.4): o que o slot principal mostra na classificacao.</summary>
public enum GpxQualiPhase { None, Table, OutLap, Running, Approach, Split, Finish }

/// <summary>Fase atual + pagina e instante de inicio da tabela (retrato) + dados da volta (<see cref="QualiBoard99Presentation.View"/>).</summary>
public sealed record GpxQualiStep(GpxQualiPhase Phase, int TablePage = 0, double TableStart = double.NaN, QualiBoard99Presentation.View? View = null);

/// <summary>Tempos do Qualy Board mesclado (Exibicao do GPxSAT): padroes dos prints.</summary>
public sealed record GpxQualiTimings(double Split = 3, double Result = 6, double Approach = 5, double Page = 7);

/// <summary>
/// Maquina de estados do Qualy Board mesclado por volta do carro em foco: volta de saida (tabela de melhores voltas estatica, so nela,
/// uma passagem por saida dos boxes; depois "FIRST LAP OUT OF PITS"), relogio, aproximacao (5 s), intermediaria (3 s) e resultado (6 s).
/// Reaproveita <see cref="QualiOutLapPresentation"/> e <see cref="QualiBoard99Presentation"/> (mesmas regras do Qualy Board 1999).
/// </summary>
public sealed class GpxQualiFlow
{
    readonly QualiOutLapPresentation _out = new();

    public void Reset() => _out.Reset();

    /// <summary>Estado em <paramref name="now"/>; <paramref name="rows"/>/<paramref name="pageCapacity"/> dimensionam a tabela da volta de saida.</summary>
    public GpxQualiStep Step(QualiLapState? q, int focusIndex, double now, int rows, int pageCapacity, string? track, GpxQualiTimings t)
    {
        if (q is null || q.CarIndex < 0 || q.CarIndex != focusIndex) { _out.Reset(); return new(GpxQualiPhase.None); }
        var stage = _out.Update(q, now, rows, pageCapacity, track, t.Page);
        if (stage == QualiBoardStage.Tower)
        {
            int pages = Math.Max(1, GpxRotation.PagesOf(rows, pageCapacity));
            return new(GpxQualiPhase.Table, Math.Clamp((int)(_out.Age / Math.Max(1, t.Page)), 0, pages - 1), _out.StartedAt);
        }
        if (stage == QualiBoardStage.Caption) return new(GpxQualiPhase.OutLap);
        var v = QualiBoard99Presentation.Resolve(q, now, false, t.Result, t.Split, t.Approach);
        var phase = v.Stage switch
        {
            QualiBoard99Stage.Running => GpxQualiPhase.Running, QualiBoard99Stage.Approach => GpxQualiPhase.Approach,
            QualiBoard99Stage.Split => GpxQualiPhase.Split, QualiBoard99Stage.Finish => GpxQualiPhase.Finish,
            QualiBoard99Stage.OutLap => GpxQualiPhase.OutLap, _ => GpxQualiPhase.None,
        };
        return new(phase, View: v);
    }
}
