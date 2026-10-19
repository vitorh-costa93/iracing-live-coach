using System;
using System.Collections.Generic;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Priority/spacing rules for spoken messages:
/// never with SpeedMs &lt; 8, on pit road or off track; at least 4 s between messages;
/// corner tips only on the approach (~250 m before the corner, scaled by speed), one per corner;
/// general messages (summary/praise/briefing) expire if they cannot be spoken in time.</summary>
public sealed class CoachMessageScheduler
{
    public const double MinSpeedMs = 8.0;
    public const double MinSpacingSec = 4.0;
    public const double LeadTimeSec = 5.0;      // 250 m at 50 m/s
    public const double MinLeadM = 120.0;
    public const double MaxLeadM = 400.0;
    public const double TooLateM = 30.0;        // closer than this to the corner -> tip dropped
    public const double GeneralTtlSec = 30.0;

    private sealed record Positional(CoachMessage Msg, double CornerStartPct, long Seq);
    private sealed record General(CoachMessage Msg, double? ExpiresAt, long Seq);

    private readonly List<Positional> _tips = new();
    private readonly List<General> _general = new();
    private double _lastEmit = double.NegativeInfinity;
    private long _seq;

    public int PendingTips => _tips.Count;
    public int PendingGeneral => _general.Count;

    public void Clear()
    {
        _tips.Clear();
        _general.Clear();
        _lastEmit = double.NegativeInfinity;
    }

    /// <summary>Session clock went backwards (new session/replay): forget the spacing timer.</summary>
    public void ResetTiming() => _lastEmit = double.NegativeInfinity;

    /// <summary>Replaces the planned corner tips (one per corner) for the next lap.</summary>
    public void PlanTips(IEnumerable<(CoachMessage Msg, double CornerStartPct)> tips)
    {
        _tips.Clear();
        foreach (var t in tips) _tips.Add(new Positional(t.Msg, t.CornerStartPct, _seq++));
    }

    /// <summary>Queues a non-positional message; expires GeneralTtlSec after the first tick in which
    /// speaking was allowed (so a briefing queued in the pits waits for the out lap).</summary>
    public void Enqueue(CoachMessage msg) => _general.Add(new General(msg, null, _seq++));

    public static double LeadMeters(double speedMs) => Math.Clamp(speedMs * LeadTimeSec, MinLeadM, MaxLeadM);

    /// <summary>Forward distance in meters from the car to a lap position.</summary>
    public static double ForwardMeters(double fromPct, double toPct, double trackLengthM)
    {
        double d = toPct - fromPct;
        d -= Math.Floor(d);
        return d * trackLengthM;
    }

    /// <summary>Returns at most one message to speak now.</summary>
    public CoachMessage? Tick(CoachSample s, double trackLengthM)
    {
        if (_tips.Count == 0 && _general.Count == 0) return null; // common case: nothing pending, no work

        double now = s.SessionTime;
        bool canSpeak = s.SpeedMs >= MinSpeedMs && !s.OnPitRoad && s.IsOnTrack;
        for (int i = _general.Count - 1; i >= 0; i--)
        {
            var g = _general[i];
            if (canSpeak && g.ExpiresAt is null) _general[i] = g = g with { ExpiresAt = now + GeneralTtlSec };
            if (g.ExpiresAt < now) _general.RemoveAt(i);
        }

        // Best candidate: highest priority, then oldest (lowest Seq). Tips must be on the approach.
        double lead = LeadMeters(s.SpeedMs);
        int bestTip = -1;
        if (trackLengthM > 0)
        {
            for (int i = _tips.Count - 1; i >= 0; i--)
            {
                var t = _tips[i];
                double d = ForwardMeters(s.LapDistPct, t.CornerStartPct, trackLengthM);
                if (d < TooLateM) { _tips.RemoveAt(i); if (bestTip > i) bestTip--; continue; } // missed: wait for the next plan
                if (d <= lead && (bestTip < 0 || Better(t.Msg.Priority, t.Seq, _tips[bestTip].Msg.Priority, _tips[bestTip].Seq)))
                    bestTip = i;
            }
        }

        if (!canSpeak) return null;
        if (now - _lastEmit < MinSpacingSec) return null;

        int bestGen = -1;
        for (int i = 0; i < _general.Count; i++)
            if (bestGen < 0 || Better(_general[i].Msg.Priority, _general[i].Seq, _general[bestGen].Msg.Priority, _general[bestGen].Seq))
                bestGen = i;

        CoachMessage pick;
        if (bestTip >= 0 && (bestGen < 0 || Better(_tips[bestTip].Msg.Priority, _tips[bestTip].Seq,
                _general[bestGen].Msg.Priority, _general[bestGen].Seq)))
        {
            pick = _tips[bestTip].Msg;
            _tips.RemoveAt(bestTip);
        }
        else if (bestGen >= 0)
        {
            pick = _general[bestGen].Msg;
            _general.RemoveAt(bestGen);
        }
        else return null;

        _lastEmit = now;
        return pick;
    }

    private static bool Better(int priority, long seq, int otherPriority, long otherSeq) =>
        priority > otherPriority || (priority == otherPriority && seq < otherSeq);
}
