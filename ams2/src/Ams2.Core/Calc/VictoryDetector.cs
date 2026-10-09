namespace Ams2.Core.Calc;

/// <summary>
/// "O jogador acabou de vencer a corrida", sem tocar na memoria do jogo (puro e testado). Dispara UMA vez por corrida.
/// Mesma definicao de vitoria do <see cref="BroadcastTracker"/> (vencedor = carro Position 1 com RaceState Finished em sessao Race).
/// Regras:
///  - so Kind == Race, jogo rodando a sessao (InSession) e GameState == 2: replay (5/6), pausa/classificacao (4), menu (1) e carregando (3) nunca disparam;
///  - so dispara se o detector viu o jogador correndo (Racing/NotStarted) antes: conectar com a corrida ja encerrada (ou assistir o replay) fica mudo;
///  - Position == 1 e RaceState == Finished do carro do jogador; depois de disparar so rearma quando o jogador volta a Racing/NotStarted (reinicio);
///  - mudou o tipo de sessao/pista, menu (1) ou carregando (3), ou a maior contagem de voltas caiu (reinicio): reseta.
/// </summary>
public sealed class VictoryDetector
{
    SessionKind _kind;
    string _track = "";
    int _leaderLaps;
    bool _armed;

    public void Reset() { _kind = SessionKind.Invalid; _track = ""; _leaderLaps = 0; _armed = false; }

    /// <summary>Um quadro. Devolve true exatamente uma vez por vitoria.</summary>
    public bool Update(SessionSnapshot s)
    {
        int leaderLaps = s.Cars.Count == 0 ? 0 : s.Cars.Max(c => c.LapsCompleted);
        if (s.Kind != _kind || s.Track != _track || leaderLaps < _leaderLaps) { Reset(); _kind = s.Kind; _track = s.Track; }
        _leaderLaps = leaderLaps;
        if (s.GameState is 1 or 3) { Reset(); return false; }
        if (s.Kind != SessionKind.Race || !s.InSession || s.GameState != 2) return false;
        if (s.PlayerCar is not { } me) return false;

        if (me.RaceState is RaceState.Racing or RaceState.NotStarted) { _armed = true; return false; }
        if (me.RaceState == RaceState.Finished && _armed && me.Position == 1)
        {
            _armed = false;
            return true;
        }
        return false;
    }
}
