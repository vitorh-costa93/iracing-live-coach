namespace IracingLiveCoach.Core.Telemetry;

/// <summary>iRacing's published Strength-of-Field formula, shared by the whole-field SOF and the
/// per-class SOF each Standings class header shows.</summary>
public static class Sof
{
    private const double Br1 = 1600.0 / 0.69314718055994530942;

    /// <summary>Null when there is no positive iRating to work from (never a fabricated 0).</summary>
    public static double? Compute(IEnumerable<int> iRatings)
    {
        var valid = iRatings.Where(r => r > 0).ToList();
        if (valid.Count == 0) return null;
        double sumExp = valid.Sum(r => Math.Exp(-r / Br1));
        return sumExp > 0 ? Br1 * Math.Log(valid.Count / sumExp) : null;
    }
}
