namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Data for the radar's side bars. For each side iRacing reports occupied (CarLeftRight), the signed
/// longitudinal offset (m, + = ahead) of the nearest car placed on that side by
/// <see cref="RadarSideAssigner"/>; null = side clear, or reported but no car resolved there (then the
/// bar is drawn full length, the pre-existing behaviour). Pure and unit-tested.
/// </summary>
public static class RadarSideOffsets
{
    /// <summary>Kapps shows the radar ONLY while a car overlaps the player side by side -- iRacing's
    /// CarLeftRight 2..6 (car left/right/both/2 left/2 right) -- and then only the side bars: no panel, no
    /// cars ahead/behind. Observed live side by side 24/09/2026 (the bars appear exactly when our car turned
    /// red = overlap; cars 9-15 m ahead/behind with CarLeftRight 1 showed nothing in Kapps).</summary>
    public static bool IsVisible(int carLeftRight) => carLeftRight is >= 2 and <= 6;

    public static (double? Left, double? Right) Nearest(IEnumerable<RadarBlip> blips, bool leftOccupied, bool rightOccupied)
    {
        double? Pick(RadarSide side) => blips.Where(b => b.Side == side)
            .OrderBy(b => Math.Abs(b.DistanceMeters)).Select(b => (double?)b.DistanceMeters).FirstOrDefault();
        return (leftOccupied ? Pick(RadarSide.Left) : null, rightOccupied ? Pick(RadarSide.Right) : null);
    }

    /// <summary>Kapps-style side bar fill. The bar's track stands for the PLAYER's car (rear at 0, nose at 1);
    /// the fill is the stretch of it the side car covers, i.e. the overlap of [offset - L/2, offset + L/2]
    /// with [-L/2, +L/2] (L = car length, + = ahead = up). Evidence: Kapps print after the flag at Watkins
    /// Glen (24/09/2026, kapps_radar1.png), CarLeftRight = car left, car 1.8 m behind: amber fill over the
    /// lower ~65% of the track; this rule gives 0..0.625. Null offset (side reported, car unresolved) = full
    /// bar; no overlap left (|offset| &gt;= L) = a sliver at the nearer end so the reported side stays visible.</summary>
    public static (double From, double To) Fill(double? offsetMeters, double carLengthMeters = 4.8)
    {
        if (offsetMeters is not double d || carLengthMeters <= 0) return (0, 1);
        double half = carLengthMeters / 2;
        double lo = Math.Max(-half, d - half), hi = Math.Min(half, d + half);
        const double sliver = 0.08;
        if (hi - lo <= 0) return d > 0 ? (1 - sliver, 1) : (0, sliver);
        return ((lo + half) / carLengthMeters, (hi + half) / carLengthMeters);
    }
}
