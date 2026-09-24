namespace IracingLiveCoach.Core.Telemetry;

/// <summary>How many laps the race has and how many the player still has to drive.</summary>
/// <param name="TotalLaps">Laps the winner will complete (the "30" in "LAP 1/30").</param>
/// <param name="PlayerLapsRemaining">Fractional laps the player still drives until their own
/// chequered flag (includes what is left of the lap in progress) -- the figure fuel is planned on.</param>
/// <param name="IsEstimate">True for a time-limited race, where the total is projected from lap time.</param>
/// <param name="ProjectedTotalLaps">Time-limited race only: the UNROUNDED projection leaderProgress +
/// timeRemaining / lapTime -- what Kapps prints in its header ("R GT3 9/≈11.57"); null when the lap limit
/// decides the length.</param>
public sealed record RaceLapEstimate(int TotalLaps, double PlayerLapsRemaining, bool IsEstimate, double? ProjectedTotalLaps = null);

/// <summary>
/// Pure race-length projection shared by the header ("LAP 1/≈30") and the fuel calculator.
///
/// Lap-limited race: the leader finishes after <c>lapsLimit</c> laps. Time-limited race: the leader
/// finishes the lap in progress when the clock reaches zero, so their final lap count is
/// ceil(leaderProgress + timeRemaining / lapTime). A race with both limits ends at whichever comes
/// first. The player takes the flag at their first line crossing after the leader has finished, so a
/// lapped player drives fewer laps than the total.
/// </summary>
public static class RaceLapEstimator
{
    /// <summary>iRacing reports 32767 laps for a session with no lap limit.</summary>
    public const int UnlimitedLaps = 32767;

    /// <summary>Race distance covered by a car: CarIdxLapCompleted + CarIdxLapDistPct, never negative. On
    /// the grid iRacing reports LapCompleted = -1 with the car just short of the line (pct ~0.99), which is
    /// -0.01 laps, i.e. 0 -- not the ~1 lap that max(0, LapCompleted) + pct would give (verified live on the
    /// Watkins Glen grid, 24/09/2026). Null when the car is not in the world (pct &lt; 0).</summary>
    public static double? Progress(int lapCompleted, double lapDistPct) =>
        lapDistPct < 0 ? null : Math.Max(0, lapCompleted + lapDistPct);

    /// <summary>The race clock to project on. Before the green (SessionState &lt; Racing: get in car, warm-up,
    /// parade laps) SessionTimeRemain is the COUNTDOWN to the start -- verified live, Watkins Glen 20-min race
    /// 24/09/2026: SessionState 1, SessionTimeRemain 48 s, SessionTimeTotal 1200 s -- so the whole race
    /// length (SessionTimeTotal) is used instead. From the green on, SessionTimeRemain. Null/negative = none.</summary>
    public static double? RaceTimeRemaining(int sessionState, double? sessionTimeRemain, double? sessionTimeTotal)
    {
        const int racing = 4;
        if (sessionState is > 0 and < racing)
            return sessionTimeTotal is > 0 ? sessionTimeTotal : null;
        return sessionTimeRemain is >= 0 ? sessionTimeRemain : null;
    }

    /// <summary>The pole lap: best qualifying lap of the car starting P1 overall (the fastest lap on the grid
    /// when P1 has no time). Kapps projects a time-limited race on it until the leader has a race lap --
    /// verified live 24/09/2026, 45-min multiclass AI race: "≈34.53" = 2700 s / 78.1975 s (pole, #00) on the
    /// grid, during the parade AND at 44:19 after the green (leader 0.46 laps in: still 34.53, not 34.46).
    /// Also Watkins Glen: 1200 / 103.074 = 11.64. Null when no grid time is known.</summary>
    public static double? PoleLapTime(IReadOnlyDictionary<int, int> gridPositionByCar, IReadOnlyDictionary<int, double> bestLapByCar)
    {
        var pole = gridPositionByCar.Where(kv => kv.Value > 0).OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(-1);
        if (pole >= 0 && bestLapByCar.TryGetValue(pole, out var lap) && lap > 1) return lap;
        var valid = bestLapByCar.Values.Where(v => v > 1).ToList();
        return valid.Count > 0 ? valid.Min() : null;
    }

    /// <summary>Before the leader has completed a race lap: the whole session length on the pole lap, from
    /// zero progress (Kapps' "≈34.53"); the player's remaining laps are what is left after their progress.</summary>
    public static RaceLapEstimate? EstimateFromPole(int? lapsLimit, double sessionTimeTotalSeconds, double poleLapSeconds, double playerProgress)
    {
        var e = Estimate(lapsLimit, sessionTimeTotalSeconds, 0, 0, poleLapSeconds);
        return e is null ? null : e with { PlayerLapsRemaining = Math.Max(0, e.PlayerLapsRemaining - Math.Max(0, playerProgress)) };
    }

    /// <param name="lapsLimit">SessionLapsTotal (UnlimitedLaps or &lt;= 0 = no lap limit).</param>
    /// <param name="timeRemainingSeconds">SessionTimeRemain (null/negative = no time limit).</param>
    /// <param name="leaderProgress">Leader's completed laps + fraction of the current lap.</param>
    /// <param name="playerProgress">Player's completed laps + fraction of the current lap.</param>
    /// <param name="lapTimeSeconds">Representative race lap time; needed only for a time limit.</param>
    /// <param name="leaderLapTimeSeconds">The overall leader's lap time when it differs from the player's
    /// (multiclass: a faster class leads). Null = same pace as <paramref name="lapTimeSeconds"/>, which is
    /// the single-class case already verified against Kapps. Used to project when the leader finishes
    /// (time-limited) and how far the player gets in that time.</param>
    public static RaceLapEstimate? Estimate(int? lapsLimit, double? timeRemainingSeconds, double leaderProgress, double playerProgress, double? lapTimeSeconds, double? leaderLapTimeSeconds = null)
    {
        double? leaderLap = leaderLapTimeSeconds is > 1 ? leaderLapTimeSeconds : lapTimeSeconds;
        // How many of the player's laps fit in one of the leader's (1 = same pace).
        double paceRatio = leaderLap is double ll && lapTimeSeconds is double pl && pl > 1 && ll > 1 ? ll / pl : 1.0;
        leaderProgress = Math.Max(leaderProgress, playerProgress); // the player can never be ahead of the leader
        bool hasLapLimit = lapsLimit is > 0 and < UnlimitedLaps;

        double? byTime = null, projected = null;
        if (timeRemainingSeconds is double remaining && remaining >= 0 && leaderLap is double lapTime && lapTime > 1)
        {
            projected = leaderProgress + remaining / lapTime;
            byTime = Math.Ceiling(projected.Value - 1e-9);
        }

        double leaderFinal;
        bool estimate;
        if (hasLapLimit && byTime is double t)
        {
            estimate = t < lapsLimit!.Value;
            leaderFinal = Math.Min(lapsLimit.Value, t);
        }
        else if (hasLapLimit) { leaderFinal = lapsLimit!.Value; estimate = false; }
        else if (byTime is double onlyTime) { leaderFinal = onlyTime; estimate = true; }
        else return null;

        leaderFinal = Math.Max(leaderFinal, Math.Ceiling(leaderProgress - 1e-9)); // never "finished" before the current lap ends
        // The player takes the flag at their first line crossing after the leader has finished, so a
        // lapped car drives fewer laps than the total -- verified against Kapps' "Fuel at End" live.
        double playerAtLeaderFinish = playerProgress + (leaderFinal - leaderProgress) * paceRatio;
        double playerFinal = Math.Ceiling(playerAtLeaderFinish - 1e-9);
        return new RaceLapEstimate((int)leaderFinal, Math.Max(0, playerFinal - playerProgress), estimate, estimate ? projected : null);
    }
}
