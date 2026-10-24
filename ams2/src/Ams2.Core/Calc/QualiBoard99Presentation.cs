namespace Ams2.Core.Calc;

public enum QualiBoard99Stage { Hidden, OutLap, Running, Approach, Split, Finish }

/// <summary>1999 qualification lower board; never includes classification or a tower.</summary>
public static class QualiBoard99Presentation
{
    public const double SplitHold = 3, ResultHold = 6;
    public sealed record View(QualiBoard99Stage Stage, double? Time, int Mark = 0,
        double? ReferenceTime = null, double? Delta = null, CarSnapshot? ReferenceCar = null,
        double? ReferenceSpeedMps = null, double? PlayerSpeedMps = null, int Position = 0, bool Invalid = false);

    public static View Resolve(QualiLapState? q, double now, bool personal = false, double resultHold = ResultHold, double splitHold = SplitHold)
    {
        if (q is null || q.CarIndex < 0 || !double.IsFinite(now)) return new(QualiBoard99Stage.Hidden, null);
        if (!q.InPit && q.OutLap) return new(QualiBoard99Stage.OutLap, null);
        if (q.LastResult is { WasOutLap: false } r && now >= r.At && now - r.At < resultHold)
            return new(QualiBoard99Stage.Finish, r.LapTime, 3, Delta: personal ? r.DeltaPersonal : r.GapToFirst,
                PlayerSpeedMps: r.SpeedMps, Position: r.Position, Invalid: r.Invalid);
        if (q.InPit || q.Elapsed is not { } elapsed || !double.IsFinite(elapsed) || elapsed < 0)
            return new(QualiBoard99Stage.Hidden, null);
        var split = q.LastSplit is { } sp && now >= sp.At && now - sp.At < splitHold ? sp : null;
        int mark = split?.Sector ?? Math.Clamp(q.Sector + 1, 1, 3);
        var reference = split is not null ? personal ? split.PersonalReference : split.LeaderReference
            : personal ? q.PersonalReference : q.LeaderReference;
        double? time = reference?.Times.ElementAtOrDefault(mark - 1)
            ?? QualiBoardTiming.ReferenceTime(q, mark, personal, split);
        bool approaching = QualiBoardTiming.ReferenceVisible(time, elapsed, mark);
        if (split is null && !approaching) return new(QualiBoard99Stage.Running, elapsed);
        return new(split is null ? QualiBoard99Stage.Approach : QualiBoard99Stage.Split,
            split?.Elapsed ?? elapsed, mark, time, split is null ? null : personal ? split.DeltaPersonal : split.DeltaLeader,
            reference?.Car, reference?.SpeedsMps.ElementAtOrDefault(mark - 1), split?.SpeedMps);
    }
}
