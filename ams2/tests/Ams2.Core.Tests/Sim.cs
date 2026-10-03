namespace Ams2.Core.Tests;

/// <summary>
/// Simulador simples: carros em velocidade constante numa pista circular.
/// Opcional: <see cref="AutoPositions"/> (posição pela distância percorrida), <see cref="AutoLapTimes"/> (LastLapTime exato
/// no instante em que a volta fecha), <see cref="Speeds"/> mutável (pit stop = velocidade 0), nomes/carros e tipo de sessão.
/// O setor (0..2) sai sempre da distância na volta, em terços.
/// </summary>
public sealed class Sim
{
    public double Now;
    public int PlayerIndex;
    public Dictionary<int, PitState> Pit = [];
    public Dictionary<int, RaceState> Race = [];
    public Dictionary<int, double> LastLap = [];
    public SessionKind Kind = SessionKind.Race;
    public string Track = "T";
    public bool AutoPositions;
    public bool AutoLapTimes;
    public string[]? Names;
    public string[]? CarNames;
    public readonly double[] Speeds;
    readonly double _len;
    readonly double[] _total;
    readonly double[] _lapStart;

    public Sim(double trackLength, params (double startDist, double speed)[] cars)
    {
        _len = trackLength;
        _total = cars.Select(c => c.startDist).ToArray();
        Speeds = cars.Select(c => c.speed).ToArray();
        // Início "virtual" da volta em curso (como se o carro tivesse vindo sempre na mesma velocidade).
        _lapStart = cars.Select(c => c.speed > 0 ? -(c.startDist - Math.Floor(c.startDist / trackLength) * trackLength) / c.speed : 0).ToArray();
    }

    public double TotalOf(int i) => _total[i];

    public void Step(double dt)
    {
        for (int i = 0; i < _total.Length; i++)
        {
            double before = _total[i], after = before + Speeds[i] * dt;
            if (AutoLapTimes && Speeds[i] > 0 && Math.Floor(after / _len) > Math.Floor(before / _len))
            {
                double crossT = Now + (Math.Floor(after / _len) * _len - before) / Speeds[i];
                LastLap[i] = crossT - _lapStart[i];
                _lapStart[i] = crossT;
            }
            _total[i] = after;
        }
        Now += dt;
    }

    public SessionSnapshot Snapshot()
    {
        int n = _total.Length;
        var pos = new int[n];
        if (AutoPositions)
        {
            var order = Enumerable.Range(0, n).OrderByDescending(i => _total[i]).ToArray();
            for (int k = 0; k < n; k++) pos[order[k]] = k + 1;
        }
        else for (int i = 0; i < n; i++) pos[i] = i + 1;

        var list = new List<CarSnapshot>();
        for (int i = 0; i < n; i++)
        {
            int laps = (int)Math.Floor(_total[i] / _len);
            double d = _total[i] - laps * _len;
            string carName = CarNames?[i] ?? "car";
            list.Add(new CarSnapshot(i, Names?[i] ?? $"C{i}", carName, "cls", pos[i], pos[i], laps, laps + 1, d, Math.Clamp((int)(d / _len * 3), 0, 2),
                0, LastLap.GetValueOrDefault(i), Speeds[i], Pit.GetValueOrDefault(i), Race.GetValueOrDefault(i, RaceState.Racing), false, i == PlayerIndex,
                "", Ams2.Core.Reading.SnapshotMapper.SupplierFromCarName(carName)));
        }
        var wheels = Enumerable.Repeat(new WheelSnapshot(0, 0, 0, 0, ""), 4).ToList();
        var player = new PlayerSnapshot(PlayerIndex, new InputsSnapshot(0, 0, 0, 0, 0, 0, 0, 0), 3, 0, 0, Speeds[PlayerIndex], 50, 80, wheels, 0);
        return new SessionSnapshot(9, 0, true, Kind, Track, "", _len, 20, null, 0,
            new WeatherSnapshot(0, 0, 0, 0, 0, 0, 0, 0), list, player);
    }
}
