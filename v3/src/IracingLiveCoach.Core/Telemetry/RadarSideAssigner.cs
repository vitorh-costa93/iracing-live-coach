namespace IracingLiveCoach.Core.Telemetry;

public enum RadarSide { Center, Left, Right }

/// <summary>
/// iRacing gives every car's distance along the lap (so the VERTICAL position on a radar is exact)
/// but never a car's lateral position. The only lateral signal is the player's own CarLeftRight:
/// "a car on my left", "on my right", "both", "two on my left", "two on my right". This class turns
/// the two into lanes, the way Kapps' radar does: the cars overlapping the player (closest first)
/// fill the sides CarLeftRight reports, a car keeps the side it was given while it stays near (so it
/// does not jump lanes when it drops back or pulls ahead), and a car with no side yet is drawn
/// straight ahead/behind in the player's lane. Pure and unit-tested.
/// </summary>
public sealed class RadarSideAssigner
{
    /// <summary>Longitudinal gap (m) within which a car can be alongside -- a bit over a car length,
    /// because iRacing's own side flag triggers slightly before the bodies actually overlap.</summary>
    public const double OverlapMeters = 7.0;

    private readonly Dictionary<int, RadarSide> _sides = new();

    public IReadOnlyDictionary<int, RadarSide> Assign(IReadOnlyList<(int Idx, double DistanceMeters)> cars, int carLeftRight)
    {
        int leftSlots = carLeftRight switch { 2 or 4 => 1, 5 => 2, _ => 0 };
        int rightSlots = carLeftRight switch { 3 or 4 => 1, 6 => 2, _ => 0 };

        var present = cars.Select(c => c.Idx).ToHashSet();
        foreach (var gone in _sides.Keys.Where(k => !present.Contains(k)).ToList()) _sides.Remove(gone);

        var result = new Dictionary<int, RadarSide>();
        var overlapping = cars.Where(c => Math.Abs(c.DistanceMeters) <= OverlapMeters).OrderBy(c => Math.Abs(c.DistanceMeters)).ToList();

        // 1) Alongside cars keep the side they already had, while CarLeftRight still reports it.
        foreach (var car in overlapping)
        {
            if (!_sides.TryGetValue(car.Idx, out var previous)) continue;
            if (previous == RadarSide.Left && leftSlots > 0) { result[car.Idx] = RadarSide.Left; leftSlots--; }
            else if (previous == RadarSide.Right && rightSlots > 0) { result[car.Idx] = RadarSide.Right; rightSlots--; }
        }
        // 2) Remaining reported sides go to the closest cars without one.
        foreach (var car in overlapping.Where(c => !result.ContainsKey(c.Idx)))
        {
            if (leftSlots > 0 && (rightSlots == 0 || !_sides.TryGetValue(car.Idx, out var had) || had != RadarSide.Right)) { result[car.Idx] = RadarSide.Left; leftSlots--; }
            else if (rightSlots > 0) { result[car.Idx] = RadarSide.Right; rightSlots--; }
        }
        // 3) Everything else: the side it last had (dropping back / pulling away), or the player's lane.
        foreach (var car in cars.Where(c => !result.ContainsKey(c.Idx)))
        {
            bool alongsideButUnreported = Math.Abs(car.DistanceMeters) <= OverlapMeters;
            result[car.Idx] = !alongsideButUnreported && _sides.TryGetValue(car.Idx, out var kept) ? kept : RadarSide.Center;
        }

        foreach (var (idx, side) in result)
            if (side != RadarSide.Center) _sides[idx] = side;
            else _sides.Remove(idx);
        return result;
    }

    public void Reset() => _sides.Clear();
}
