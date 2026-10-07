namespace Ams2.Core.Calc;

public sealed record RelativeRow(CarSnapshot Car, double DistanceMeters, double? GapSeconds, int LapDelta, bool IsPlayer)
{
    /// <summary>+1 = uma volta à frente do jogador, -1 = uma volta atrás.</summary>
    public bool Lapped => LapDelta < 0;
    public bool LapAhead => LapDelta > 0;
}

public static class RelativeBuilder
{
    /// <summary>
    /// Lista do Relative: até <paramref name="ahead"/> carros à frente (do mais distante ao mais próximo),
    /// o jogador, e até <paramref name="behind"/> atrás. Distância circular: o vizinho é o lado mais curto da pista.
    /// </summary>
    public static IReadOnlyList<RelativeRow> Build(SessionSnapshot s, GapTracker tracker, double now, int ahead = 4, int behind = 4)
    {
        var me = s.PlayerCar;
        if (me is null || s.TrackLength <= 0) return [];
        double len = s.TrackLength;
        double myTotal = me.TotalDistance(len);

        var aheadRows = new List<RelativeRow>();
        var behindRows = new List<RelativeRow>();
        foreach (var c in s.Cars)
        {
            if (c.Index == me.Index || c.InGarage || c.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified) continue;
            double delta = c.LapDistance - me.LapDistance;                // [-len, len]
            if (delta > len / 2) delta -= len; else if (delta < -len / 2) delta += len;
            int lapDelta = (int)Math.Round((c.TotalDistance(len) - myTotal - delta) / len);
            var row = new RelativeRow(c, delta, tracker.GapSeconds(now, me, c, delta), lapDelta, false);
            (delta >= 0 ? aheadRows : behindRows).Add(row);
        }

        var result = new List<RelativeRow>();
        result.AddRange(aheadRows.OrderBy(r => r.DistanceMeters).Take(ahead).Reverse());
        result.Add(new RelativeRow(me, 0, 0, 0, true));
        result.AddRange(behindRows.OrderByDescending(r => r.DistanceMeters).Take(behind));
        return result;
    }
}
