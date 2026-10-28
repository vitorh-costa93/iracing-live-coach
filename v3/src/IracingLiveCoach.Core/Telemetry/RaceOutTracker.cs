namespace IracingLiveCoach.Core.Telemetry;

public readonly record struct OutCarSample(int CarIdx, int ClassId, int Laps, double DistPct, int Surface);
public readonly record struct LiveRaceEntry(int CarIdx, int ClassId, int Laps, double DistPct, bool Out);

/// <summary>
/// Race live order that keeps a car which left the world (CarIdxTrackSurface == NotInWorld, LapDistPct &lt; 0)
/// in the list "parked" at its last on-track progress: cars that already passed that point rank ahead of
/// it, cars that have not reached it yet rank behind, so the player only gains the position when really
/// passing the spot (same as <see cref="RaceRatingField"/>, which keeps the last progress). A car is OUT
/// only if it was seen in the world during the race (<c>recording</c> true, SessionState Racing) and now
/// reports NotInWorld; a car in the pit stall/pit road always has a valid distance and is never OUT.
/// A car that returns to the world is a normal entry again. Pure and unit-tested.
/// </summary>
public sealed class RaceOutTracker
{
    public const int NotInWorld = -1;
    private readonly Dictionary<int, (int Laps, double Pct)> _last = new();

    public void Reset() => _last.Clear();

    public bool TryGetProgress(int carIdx, out (int Laps, double Pct) progress) => _last.TryGetValue(carIdx, out progress);

    public List<LiveRaceEntry> Resolve(IEnumerable<OutCarSample> samples, bool recording)
    {
        var entries = new List<LiveRaceEntry>();
        foreach (var s in samples)
        {
            if (s.DistPct >= 0)
            {
                if (recording && s.Surface != NotInWorld) _last[s.CarIdx] = (s.Laps, s.DistPct);
                entries.Add(new LiveRaceEntry(s.CarIdx, s.ClassId, s.Laps, s.DistPct, false));
            }
            else if (s.Surface == NotInWorld && _last.TryGetValue(s.CarIdx, out var parked))
                entries.Add(new LiveRaceEntry(s.CarIdx, s.ClassId, parked.Laps, parked.Pct, true));
        }
        return entries;
    }
}
