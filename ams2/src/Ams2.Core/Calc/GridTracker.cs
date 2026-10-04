namespace Ams2.Core.Calc;

/// <summary>
/// Grid de largada (posição de cada carro ao iniciar a corrida), para o modo GAINED/LOST da torre 2018 e o "STARTED".
/// Regras: só em sessão de corrida; enquanto nenhum carro largou (todos <see cref="RaceState.NotStarted"/>/Invalid) o grid acompanha as
/// posições (formação do grid); no primeiro quadro com a corrida em andamento ele congela. Se o overlay só viu a corrida já em andamento,
/// captura as posições do primeiro quadro desde que o líder ainda esteja na 1ª volta (LapsCompleted = 0); depois disso o grid fica
/// desconhecido (vazio). Troca de sessão/pista ou reinício (voltas do líder diminuem) zera.
/// </summary>
public sealed class GridTracker
{
    static readonly IReadOnlyDictionary<int, int> None = new Dictionary<int, int>();
    readonly Dictionary<int, int> _work = [];
    IReadOnlyDictionary<int, int> _grid = None;
    bool _frozen;
    SessionKind _kind = SessionKind.Invalid;
    string _track = "";
    int _leaderLaps;

    /// <summary>Índice do carro -> posição de largada. Vazio = desconhecido. Cópia imutável (troca só quando muda).</summary>
    public IReadOnlyDictionary<int, int> Grid => _grid;
    /// <summary>true quando o grid já está fixo (corrida em andamento).</summary>
    public bool Frozen => _frozen;

    public void Reset()
    {
        _work.Clear(); _grid = None; _frozen = false;
        _kind = SessionKind.Invalid; _track = ""; _leaderLaps = 0;
    }

    public IReadOnlyDictionary<int, int> Update(SessionSnapshot s)
    {
        var cars = s.Cars.Where(c => c.Position > 0).ToList();
        int leaderLaps = cars.Count == 0 ? 0 : cars.Max(c => c.LapsCompleted);
        if (s.Kind != _kind || s.Track != _track || leaderLaps < _leaderLaps) { Reset(); _kind = s.Kind; _track = s.Track; }
        _leaderLaps = leaderLaps;
        if (s.Kind != SessionKind.Race || _frozen || cars.Count == 0) return _grid;

        bool started = cars.Any(c => c.RaceState is not (RaceState.NotStarted or RaceState.Invalid));
        if (!started)
        {
            Capture(cars);
            return _grid;
        }
        // Largou: congela o grid visto antes da largada; sem ele, só vale capturar ainda na 1ª volta do líder.
        if (_work.Count == 0 && leaderLaps == 0) Capture(cars);
        _frozen = true;
        return _grid;
    }

    void Capture(List<CarSnapshot> cars)
    {
        bool changed = cars.Count != _work.Count;
        foreach (var c in cars)
            if (!_work.TryGetValue(c.Index, out var p) || p != c.Position) { changed = true; break; }
        if (!changed) return;
        _work.Clear();
        foreach (var c in cars) _work[c.Index] = c.Position;
        _grid = new Dictionary<int, int>(_work);
    }

    /// <summary>Posições ganhas (+) ou perdidas (-) desde a largada; null sem grid para o carro.</summary>
    public static int? Delta(IReadOnlyDictionary<int, int>? grid, CarSnapshot car)
        => grid is not null && grid.TryGetValue(car.Index, out var g) && car.Position > 0 ? g - car.Position : null;
}
