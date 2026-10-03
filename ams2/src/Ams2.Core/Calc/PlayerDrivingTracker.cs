namespace Ams2.Core.Calc;

/// <summary>
/// Regra UNICA de visibilidade dos widgets: "o jogador assumiu o carro". Verdadeiro quando
///  - GameState == 2 (jogando: contagem pre-corrida e pilotando; menu 1, carregando 3, pausa/classificacao 4 e replay 5/6 = falso);
///  - o carro do jogador nao esta parado na garagem (PitState InGarage);
///  - a camera esta no carro do jogador: o indice visto ao entrar na sessao fica guardado como o "proprio"; outro indice = falso.
/// Debounce: aparece na hora; some so depois de <see cref="HideDelaySeconds"/> contínuos de falso (evita piscar em quadros de transicao).
/// O indice proprio e esquecido ao ir para menu (1), carregando (3) ou desconectar (<see cref="Reset"/>).
/// </summary>
public sealed class PlayerDrivingTracker
{
    public const double HideDelaySeconds = 0.15;

    int _own = -1;
    bool _shown;
    double _falseSince = double.NaN;

    public bool Current => _shown;

    public void Reset() { _own = -1; _shown = false; _falseSince = double.NaN; }

    /// <summary>Regra instantanea, sem debounce.</summary>
    public bool Evaluate(SessionSnapshot s)
    {
        if (s.GameState is 1 or 3) _own = -1;
        if (!s.InSession || s.Player is not { } pl) return false;
        if (_own < 0) _own = pl.Index;
        if (pl.Index != _own) return false;
        var car = s.PlayerCar;
        return car is not null && car.PitState != PitState.InGarage;
    }

    public bool Update(double now, SessionSnapshot s)
    {
        if (Evaluate(s)) { _shown = true; _falseSince = double.NaN; return true; }
        if (!_shown) return false;
        if (double.IsNaN(_falseSince) || now < _falseSince) _falseSince = now;
        if (now - _falseSince >= HideDelaySeconds) { _shown = false; _falseSince = double.NaN; }
        return _shown;
    }
}
