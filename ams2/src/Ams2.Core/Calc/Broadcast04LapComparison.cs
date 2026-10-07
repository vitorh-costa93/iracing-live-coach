namespace Ams2.Core.Calc;

/// <summary>Reference row below the live clock; its rank belongs to the comparison driver.</summary>
public sealed record Broadcast04LapComparison(double? ReferenceTime, double? Delta, int ReferencePosition, int? ResultPosition, bool Invalid)
{
    public bool IsFaster => !Invalid && Delta is { } delta && double.IsFinite(delta) && delta < 0;

    public static Broadcast04LapComparison? Resolve(QualiLapState q, QualiSplit? split, QualiLapResult? result, bool personal, int referencePosition)
    {
        if (result is not null)
            return new(null, personal ? result.DeltaPersonal : result.GapToFirst, referencePosition, result.Position, result.Invalid);
        if (split is not null)
            return new(null, personal ? split.DeltaPersonal : split.DeltaLeader, referencePosition, null, false);
        int mark = Math.Clamp(q.Sector + 1, 1, 3);
        var reference = QualiBoardTiming.ReferenceTime(q, mark, personal);
        return reference is { } time && double.IsFinite(time) && q.Elapsed is { } elapsed && elapsed >= time - 5 && elapsed < time
            ? new(time, null, referencePosition, null, false) : null;
    }
}
