using System.Globalization;
using System.Text.Json;

namespace Ams2.Core.Calc;

/// <summary>Uma largada cronometrada do jogador. Tempos em s desde o sinal verde (null = alvo não alcançado);
/// <c>At*</c> = relógio do provider no instante em que o alvo foi alcançado; <c>PrevBest*</c> = melhor guardado ANTES desta largada.</summary>
public sealed record LaunchResult(string Key, double? T100, double? T200, double At100, double At200, double? PrevBest100, double? PrevBest200)
{
    public double? Time(int target) => target == 100 ? T100 : T200;
    public double At(int target) => target == 100 ? At100 : At200;
    public double? PrevBest(int target) => target == 100 ? PrevBest100 : PrevBest200;
}

/// <summary>Estado para o widget: última largada (desta pista+carro) e o melhor guardado da pista+carro atual.</summary>
public sealed record LaunchState(LaunchResult? Last, double? StoredBest100, double? StoredBest200)
{
    public static readonly LaunchState Empty = new(null, null, null);
    public double? StoredBest(int target) => target == 100 ? StoredBest100 : StoredBest200;
}

/// <summary>
/// Melhores tempos de largada por pista+carro ("pista|variante|carro" -> {"100": s, "200": s}), em JSON
/// (%AppData%\ams2-live-coach\launch.json). Caminho null = só em memória (prévias, testes). Leitura e gravação nunca lançam.
/// </summary>
public sealed class LaunchStore
{
    readonly string? _path;
    readonly Dictionary<string, Dictionary<string, double>> _best;

    public LaunchStore(string? path)
    {
        _path = path;
        _best = Load(path);
    }

    public static LaunchStore InMemory() => new(null);
    public static string DefaultPath(string? root = null)
        => Path.Combine(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ams2-live-coach"), "launch.json");
    public static string Key(string track, string variation, string car) => $"{track}|{variation}|{car}";

    public string? FilePath => _path;

    public double? Best(string key, int target)
        => _best.TryGetValue(key, out var d) && d.TryGetValue(T(target), out var v) && v > 0 ? v : null;

    /// <summary>Registra um tempo; grava o arquivo se for o novo melhor. Retorna true se melhorou.</summary>
    public bool Offer(string key, int target, double seconds)
    {
        if (!(seconds > 0) || double.IsInfinity(seconds)) return false;
        if (Best(key, target) is { } b && b <= seconds) return false;
        if (!_best.TryGetValue(key, out var d)) _best[key] = d = new Dictionary<string, double>();
        d[T(target)] = Math.Round(seconds, 3);
        Save();
        return true;
    }

    static string T(int target) => target.ToString(CultureInfo.InvariantCulture);

    static Dictionary<string, Dictionary<string, double>> Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(path)) ?? [];
        }
        catch { /* arquivo corrompido/ilegível: começa vazio */ }
        return [];
    }

    void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_best, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch { /* sem permissão/disco: o melhor fica só em memória */ }
    }
}

/// <summary>
/// "RACE START 0-200km/h" do gráfico 2018: cronometra o jogador de 0 a 100 e a 200 km/h na largada da corrida.
/// Regras: só em sessão de corrida; arma enquanto o carro do jogador está no grid (RaceState NotStarted); no quadro em que ele passa a
/// Racing (sinal verde) começa a contar SE o último quadro no grid tinha o carro parado (&lt; <see cref="StillMps"/>); senão (largada lançada,
/// carro andando) descarta. Cada alvo é marcado no cruzamento interpolado entre dois quadros. A cronometragem termina ao chegar a 200 km/h,
/// ao sair do estado Racing ou após <see cref="Timeout"/> s. Os alvos alcançados viram o último resultado e são oferecidos ao
/// <see cref="LaunchStore"/> (o resultado guarda o melhor ANTERIOR). Troca de pista/carro zera o último resultado.
/// </summary>
public sealed class LaunchTracker(LaunchStore? store = null)
{
    public const double StillMps = 1.0, Timeout = 30;
    public static readonly int[] Targets = [100, 200];
    static double Mps(int kph) => kph / 3.6;

    readonly LaunchStore _store = store ?? LaunchStore.InMemory();
    public LaunchStore Store => _store;

    string _key = "";
    RaceState _prevState = RaceState.Invalid;
    double _prevSpeed, _prevT;
    bool _havePrev;
    // Cronometragem em curso
    bool _timing;
    double _t0;
    double? _t100, _t200;
    double _at100, _at200;
    double? _prev100, _prev200;

    public LaunchResult? Last { get; private set; }
    public LaunchState State { get; private set; } = LaunchState.Empty;

    public void Reset()
    {
        _key = ""; _prevState = RaceState.Invalid; _havePrev = false; _timing = false; Last = null; State = LaunchState.Empty;
    }

    public LaunchState Update(double now, SessionSnapshot s)
    {
        var car = s.PlayerCar;
        if (s.Player is null || car is null) { _havePrev = false; _timing = false; return State; }
        string key = LaunchStore.Key(s.Track, s.TrackVariation, car.CarName);
        if (key != _key) { _key = key; Last = null; _timing = false; _havePrev = false; }

        double v = Math.Max(0, s.Player.SpeedMps);
        var st = s.Kind == SessionKind.Race ? car.RaceState : RaceState.Invalid;

        if (_timing)
        {
            if (st != RaceState.Racing || now < _prevT) Finish();
            else
            {
                Cross(100, now, v, ref _t100, ref _at100);
                Cross(200, now, v, ref _t200, ref _at200);
                if (_t200 is not null || now - _t0 > Timeout) Finish();
                else Publish();
            }
        }
        else if (_havePrev && _prevState == RaceState.NotStarted && st == RaceState.Racing && _prevSpeed < StillMps)
        {
            // Sinal verde com o carro parado: começa a contar.
            _timing = true; _t0 = now; _t100 = _t200 = null;
            _prev100 = _store.Best(key, 100); _prev200 = _store.Best(key, 200);
        }

        _prevState = st; _prevSpeed = v; _prevT = now; _havePrev = true;
        State = new LaunchState(Last, _store.Best(key, 100), _store.Best(key, 200));
        return State;
    }

    void Cross(int target, double now, double v, ref double? t, ref double at)
    {
        double m = Mps(target);
        if (t is not null || v < m) return;
        double tc = _prevSpeed >= m || v <= _prevSpeed ? now : _prevT + (m - _prevSpeed) / (v - _prevSpeed) * (now - _prevT);
        t = Math.Max(0, tc - _t0);
        at = now;
        _store.Offer(_key, target, t.Value);
    }

    /// <summary>Publica o parcial (100 já alcançado) como último resultado, para o widget mostrar o alvo 100 logo que ele chega.</summary>
    void Publish()
    {
        if (_t100 is null && _t200 is null) return;
        Last = new LaunchResult(_key, _t100, _t200, _at100, _at200, _prev100, _prev200);
    }

    void Finish()
    {
        _timing = false;
        Publish();
    }
}
