namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Per-car list of completed lap times, built by watching CarIdxLapCompleted tick over (the SDK only
/// publishes the LAST lap of each car, never a history). Feeds the Standings "average gap" column.
///
/// A lap is kept only when it is a clean timed lap: the car never touched pit road during it (in/out
/// laps), it is not the first lap of a race (standing start), and the counter went up by exactly one
/// (a jump means we missed laps -- tow, session join -- so the time is not that lap's). <see cref="Reset"/>
/// on every session change. Pure and unit-tested.
/// </summary>
public sealed class LapHistory
{
    private sealed class CarLaps
    {
        public int LastLapCount = -1;
        public bool TouchedPit;
        public readonly List<double> Times = new();
        public double[] Snapshot = Array.Empty<double>();
    }

    private readonly Dictionary<int, CarLaps> _cars = new();

    public void Reset() => _cars.Clear();

    /// <param name="lapsCompleted">CarIdxLapCompleted.</param>
    /// <param name="lastLapTime">CarIdxLastLapTime (seconds, &lt;= 0 when none).</param>
    /// <param name="isRace">Race session: lap 1 (standing start) is not a representative lap.</param>
    public void Update(int carIdx, int lapsCompleted, double lastLapTime, bool onPitRoad, bool isRace)
    {
        if (lapsCompleted < 0) return;
        if (!_cars.TryGetValue(carIdx, out var car)) _cars[carIdx] = car = new CarLaps();
        if (onPitRoad) car.TouchedPit = true;

        if (car.LastLapCount < 0 || lapsCompleted < car.LastLapCount)
        {
            // First sighting (or the counter went backwards): start from here, nothing to record.
            car.LastLapCount = lapsCompleted;
            car.TouchedPit = onPitRoad;
            return;
        }
        if (lapsCompleted == car.LastLapCount) return;

        bool exactlyOne = lapsCompleted == car.LastLapCount + 1;
        bool clean = exactlyOne && !car.TouchedPit && lastLapTime > 0 && !(isRace && lapsCompleted == 1);
        car.LastLapCount = lapsCompleted;
        car.TouchedPit = onPitRoad;
        if (!clean) return;
        car.Times.Add(lastLapTime);
        car.Snapshot = car.Times.ToArray();
    }

    /// <summary>Immutable snapshot of the car's clean lap times (empty when none yet).</summary>
    public IReadOnlyList<double> Laps(int carIdx) => _cars.TryGetValue(carIdx, out var car) ? car.Snapshot : Array.Empty<double>();

    /// <summary>Mean of the <paramref name="window"/> fastest laps (every lap while there are fewer than that);
    /// null with fewer than <paramref name="minLaps"/> laps.</summary>
    public static double? AverageBest(IReadOnlyList<double>? laps, int window, int minLaps = 2)
    {
        if (laps is null || window < 1 || laps.Count < minLaps || laps.Count == 0) return null;
        return laps.OrderBy(t => t).Take(window).Average();
    }

    /// <summary>Their average minus the player's, in seconds: positive = the player is faster on average.
    /// Null when either side lacks enough laps.</summary>
    public static double? AverageGap(IReadOnlyList<double>? theirs, IReadOnlyList<double>? mine, int window)
        => AverageBest(theirs, window) is double t && AverageBest(mine, window) is double m ? t - m : null;
}
