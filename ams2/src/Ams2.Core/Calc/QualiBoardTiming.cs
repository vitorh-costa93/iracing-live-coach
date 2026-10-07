namespace Ams2.Core.Calc;

public enum QualiBoardStage { Hidden, Tower, Caption, Lap }

/// <summary>Qualification presentation; uses the observed lap clock, never fabricates a start.</summary>
public static class QualiBoardTiming
{
    public const double PageSeconds = 7, TitleSeconds = .6, RowSeconds = .45;

    public static QualiBoardStage Stage(QualiLapState? q, double now, double resultHold = 3)
    {
        if (q is null || q.CarIndex < 0) return QualiBoardStage.Hidden;
        if (!q.InPit && q.OutLap) return QualiBoardStage.Tower;
        if (!q.InPit && q.Elapsed is { } elapsed && double.IsFinite(elapsed) && elapsed >= 0)
            return QualiBoardStage.Lap;
        return q.LastResult is { } r && now >= r.At && now - r.At < resultHold
            ? QualiBoardStage.Lap : QualiBoardStage.Hidden;
    }

    public static float RowAlpha(double age, int row)
        => (float)Math.Clamp((age - TitleSeconds - row * RowSeconds) / .15, 0, 1);

    public static bool ReferenceVisible(double? reference, double? elapsed, int mark)
        => reference is { } rt && elapsed is { } age && double.IsFinite(rt) && double.IsFinite(age) &&
            age >= rt - 5 && (mark == 3 || age < rt);

    /// <summary>Independent sector minima are not a best-lap split unless their full sum matches that lap.</summary>
    public static double? ReferenceTime(QualiLapState q, int sector, bool personal, QualiSplit? crossing = null)
    {
        double? lap = personal ? q.PersonalBestLap : q.LeaderBestLap;
        if (sector == 3) return lap;
        if (sector is < 1 or > 2) return null;
        if (crossing is { } sp && sp.Sector == sector && (personal ? sp.DeltaPersonal : sp.DeltaLeader) is { } delta)
            return sp.Elapsed - delta;
        var sectors = personal ? q.PersonalBestSectors : q.OverallBestSectors;
        if (lap is not { } best || sectors.Count < 3 || sectors.Any(s => s is null || !double.IsFinite(s.Value) || s <= 0)) return null;
        if (Math.Abs(sectors.Sum(s => s!.Value) - best) > .002) return null;
        return sectors.Take(sector).Sum(s => s!.Value);
    }
}

/// <summary>One classification pass per pit exit, followed by the qualification caption until the flying lap.</summary>
public sealed class QualiOutLapPresentation
{
    int _car = -1;
    int _lap = -1;
    int _generation;
    int _pitExit;
    string? _track;
    double _at = double.NaN, _previousNow = double.NaN, _duration;
    bool _wasOut;
    public double Age { get; private set; }

    public void Reset() { _car = _lap = -1; _track = null; _at = _previousNow = double.NaN; _wasOut = false; Age = 0; }

    public QualiBoardStage Update(QualiLapState? q, double now, int rows, int pageCapacity = 8, string? track = null)
    {
        if (q is null || q.CarIndex < 0) { Reset(); return QualiBoardStage.Hidden; }
        if (_car != q.CarIndex || now < _previousNow || q.Lap < _lap || q.SessionGeneration != _generation || q.PitExitGeneration != _pitExit || !string.Equals(track, _track, StringComparison.Ordinal)) Reset();
        _car = q.CarIndex; _lap = q.Lap; _generation = q.SessionGeneration; _pitExit = q.PitExitGeneration; _track = track; _previousNow = now;
        bool outLap = !q.InPit && q.OutLap;
        if (outLap && !_wasOut)
        {
            _at = now;
            _duration = QualiBoardTiming.PageSeconds * Math.Max(1, (int)Math.Ceiling(rows / (double)Math.Max(1, pageCapacity)));
        }
        _wasOut = outLap;
        if (!outLap) return QualiBoardTiming.Stage(q, now);
        Age = Math.Max(0, now - _at);
        return Age < _duration ? QualiBoardStage.Tower : QualiBoardStage.Caption;
    }
}
