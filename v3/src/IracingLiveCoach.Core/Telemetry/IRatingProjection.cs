namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One classified driver for the iRating projection: an opaque key (CarIdx), the driver's
/// iRating and the position the projection should assume (1 = winner) inside the field passed in.</summary>
public readonly record struct IRatingEntry(int Key, int IRating, int Position);

/// <summary>
/// Projected iRating change if the session ended in the current order -- the community-standard
/// reproduction of iRacing's Elo-like update (the same one the popular iRating calculators and overlays
/// use), NOT an iRacing SDK field:
///
///   BR1 = 1600 / ln 2
///   chance(a, b) = (1 - e^(-a/BR1)) e^(-b/BR1) / ((1 - e^(-b/BR1)) e^(-a/BR1) + (1 - e^(-a/BR1)) e^(-b/BR1))
///   expected_i   = Σ_j chance(r_i, r_j) - 0.5            (j over the whole field, itself included)
///   fudge_i      = ((N - nonStarters/2) / 2 - pos_i) / 100
///   Δ_i          = (N - pos_i - expected_i - fudge_i) * 200 / starters
///
/// Live nobody is a non-starter, so N = starters. A multiclass race is rated per class: the caller
/// passes one class at a time. Drivers with iRating &lt;= 1 (AI / unrated) are excluded from the field
/// (and get no value), and the remaining drivers are re-ranked 1..N by the position given.
/// Pure and unit-tested.
/// </summary>
public static class IRatingProjection
{
    private static readonly double Br1 = 1600.0 / Math.Log(2.0);

    public static double Chance(double a, double b)
    {
        double ea = Math.Exp(-a / Br1), eb = Math.Exp(-b / Br1);
        double den = (1 - eb) * ea + (1 - ea) * eb;
        return den > 0 ? (1 - ea) * eb / den : 0.5;
    }

    public static Dictionary<int, double> Compute(IEnumerable<IRatingEntry> field)
    {
        var rated = field.Where(e => e.IRating > 1 && e.Position > 0).OrderBy(e => e.Position).ToList();
        var result = new Dictionary<int, double>();
        int n = rated.Count;
        if (n < 2) return result; // nobody to be compared with: no projection, never a fabricated 0
        for (int i = 0; i < n; i++)
        {
            int position = i + 1;
            double expected = rated.Sum(o => Chance(rated[i].IRating, o.IRating)) - 0.5;
            double fudge = (n / 2.0 - position) / 100.0;
            result[rated[i].Key] = (n - position - expected - fudge) * 200.0 / n;
        }
        return result;
    }

    /// <summary>Session gate, per the driver's rule: never in practice (or any unknown session); in
    /// qualifying only once the player has a valid lap, and then only for cars that have one; in a race
    /// always.</summary>
    public static bool ShowFor(SessionKind? kind, bool playerHasValidLap, bool carHasValidLap) => kind switch
    {
        SessionKind.Race => true,
        SessionKind.Qualify => playerHasValidLap && carHasValidLap,
        _ => false,
    };
}
