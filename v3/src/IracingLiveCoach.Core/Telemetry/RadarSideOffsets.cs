namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Data for the radar's side bars. For each side iRacing reports occupied (CarLeftRight), the signed
/// longitudinal offset (m, + = ahead) of the nearest car placed on that side by
/// <see cref="RadarSideAssigner"/>; null = side clear, or reported but no car resolved there (then the
/// bar is drawn full length, the pre-existing behaviour). Pure and unit-tested.
/// </summary>
public static class RadarSideOffsets
{
    public static (double? Left, double? Right) Nearest(IEnumerable<RadarBlip> blips, bool leftOccupied, bool rightOccupied)
    {
        double? Pick(RadarSide side) => blips.Where(b => b.Side == side)
            .OrderBy(b => Math.Abs(b.DistanceMeters)).Select(b => (double?)b.DistanceMeters).FirstOrDefault();
        return (leftOccupied ? Pick(RadarSide.Left) : null, rightOccupied ? Pick(RadarSide.Right) : null);
    }
}
