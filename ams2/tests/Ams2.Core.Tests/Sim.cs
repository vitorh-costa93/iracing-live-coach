namespace Ams2.Core.Tests;

/// <summary>Simulador simples: carros em velocidade constante numa pista circular.</summary>
public sealed class Sim(double trackLength, params (double startDist, double speed)[] cars)
{
    public double Now;
    public int PlayerIndex;
    public Dictionary<int, PitState> Pit = [];
    public Dictionary<int, RaceState> Race = [];
    public Dictionary<int, double> LastLap = [];
    readonly double[] _total = cars.Select(c => c.startDist).ToArray();

    public void Step(double dt)
    {
        Now += dt;
        for (int i = 0; i < cars.Length; i++) _total[i] += cars[i].speed * dt;
    }

    public SessionSnapshot Snapshot()
    {
        var list = new List<CarSnapshot>();
        for (int i = 0; i < cars.Length; i++)
        {
            int laps = (int)Math.Floor(_total[i] / trackLength);
            list.Add(new CarSnapshot(i, $"C{i}", "car", "cls", i + 1, i + 1, laps, laps + 1, _total[i] - laps * trackLength, 0,
                0, LastLap.GetValueOrDefault(i), cars[i].speed, Pit.GetValueOrDefault(i), Race.GetValueOrDefault(i, RaceState.Racing), false, i == PlayerIndex));
        }
        var wheels = Enumerable.Repeat(new WheelSnapshot(0, 0, 0, 0, ""), 4).ToList();
        var player = new PlayerSnapshot(PlayerIndex, new InputsSnapshot(0, 0, 0, 0, 0, 0, 0, 0), 3, 0, 0, cars[PlayerIndex].speed, 50, 80, wheels, 0);
        return new SessionSnapshot(9, 0, true, SessionKind.Race, "T", "", trackLength, 20, null, 0,
            new WeatherSnapshot(0, 0, 0, 0, 0, 0, 0, 0), list, player);
    }
}
