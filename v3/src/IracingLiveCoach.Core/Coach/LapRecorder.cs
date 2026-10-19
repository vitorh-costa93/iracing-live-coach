using System;

namespace IracingLiveCoach.Core.Coach;

/// <summary>A finished lap resampled to <see cref="ReferenceTrace.BinCount"/> distance bins.</summary>
internal sealed class RecordedLap
{
    public int Lap { get; init; }
    public double LapTimeSec { get; init; }
    /// <summary>Started at the line and covered (almost) every bin.</summary>
    public bool Complete { get; init; }
    /// <summary>No pit road, off-track/tow or position jump during the lap.</summary>
    public bool Clean { get; init; }
    public double[] Speed { get; init; } = Array.Empty<double>();
    public double[] Brake { get; init; } = Array.Empty<double>();
    public double[] Throttle { get; init; } = Array.Empty<double>();
    public double[] Steering { get; init; } = Array.Empty<double>();
    public int[] Gear { get; init; } = Array.Empty<int>();
    public double[] Elapsed { get; init; } = Array.Empty<double>();
}

/// <summary>Accumulates samples into distance bins and emits a <see cref="RecordedLap"/> at each
/// start/finish crossing. Comparisons are always by distance, never by time.</summary>
internal sealed class LapRecorder
{
    private const int N = ReferenceTrace.BinCount;
    private const int MinCoveredBins = 900;

    private readonly double[] _speed = new double[N], _brake = new double[N], _throttle = new double[N],
        _steer = new double[N], _first = new double[N];
    private readonly int[] _count = new int[N], _gear = new int[N];

    private bool _started, _dirty, _hasPrev;
    private int _lapNumber;
    private double _lapStart, _prevPct, _prevTime;

    public void Reset()
    {
        _started = _dirty = _hasPrev = false;
        ClearBins();
    }

    /// <summary>Feed one sample; returns the lap that just finished when the line is crossed.</summary>
    public RecordedLap? Add(CoachSample s)
    {
        RecordedLap? done = null;
        double pct = s.LapDistPct - Math.Floor(s.LapDistPct);

        if (_hasPrev)
        {
            double dt = s.SessionTime - _prevTime;
            if (dt < 0 || dt > 2.0)
            {
                // Replay rewind / pause / session change: the current lap is not trustworthy.
                _started = false;
            }
            else if (_prevPct > 0.8 && pct < 0.2)
            {
                double before = 1.0 - _prevPct, span = before + pct;
                double tCross = span > 1e-9 ? _prevTime + before / span * dt : s.SessionTime;
                if (_started) done = Finish(tCross);
                StartLap(tCross, s.Lap);
            }
            else if (pct < _prevPct - 0.02 || pct > _prevPct + 0.05)
            {
                _dirty = true; // reset/tow/teleport
            }
        }

        if (s.OnPitRoad || !s.IsOnTrack) _dirty = true;

        if (_started)
        {
            int b = ReferenceTrace.BinOf(pct);
            if (_count[b] == 0) { _first[b] = s.SessionTime - _lapStart; _gear[b] = s.Gear; }
            _count[b]++;
            _speed[b] += s.SpeedMs;
            _brake[b] += s.Brake;
            _throttle[b] += s.Throttle;
            _steer[b] += s.SteeringRad;
        }

        _prevPct = pct;
        _prevTime = s.SessionTime;
        _hasPrev = true;
        return done;
    }

    private void StartLap(double t, int lap)
    {
        ClearBins();
        _started = true;
        _dirty = false;
        _lapStart = t;
        _lapNumber = lap;
    }

    private void ClearBins()
    {
        Array.Clear(_speed); Array.Clear(_brake); Array.Clear(_throttle); Array.Clear(_steer);
        Array.Clear(_first); Array.Clear(_count); Array.Clear(_gear);
    }

    private RecordedLap Finish(double tCross)
    {
        int covered = 0;
        for (int i = 0; i < N; i++) if (_count[i] > 0) covered++;

        var speed = new double[N]; var brake = new double[N]; var thr = new double[N];
        var steer = new double[N]; var elapsed = new double[N]; var gear = new int[N];
        for (int i = 0; i < N; i++)
        {
            if (_count[i] == 0) continue;
            speed[i] = _speed[i] / _count[i];
            brake[i] = _brake[i] / _count[i];
            thr[i] = _throttle[i] / _count[i];
            steer[i] = _steer[i] / _count[i];
            elapsed[i] = _first[i];
            gear[i] = _gear[i];
        }
        double lapTime = tCross - _lapStart;
        if (covered > 0 && covered < N) FillGaps(speed, brake, thr, steer, elapsed, gear, lapTime);
        for (int i = 1; i < N; i++) if (elapsed[i] < elapsed[i - 1]) elapsed[i] = elapsed[i - 1];

        return new RecordedLap
        {
            Lap = _lapNumber,
            LapTimeSec = lapTime,
            Complete = covered >= MinCoveredBins,
            Clean = !_dirty,
            Speed = speed, Brake = brake, Throttle = thr, Steering = steer, Gear = gear, Elapsed = elapsed,
        };
    }

    /// <summary>Linear interpolation over empty bins (circular for values, open for elapsed time).</summary>
    private void FillGaps(double[] speed, double[] brake, double[] thr, double[] steer, double[] elapsed,
        int[] gear, double lapTime)
    {
        for (int i = 0; i < N; i++)
        {
            if (_count[i] > 0) continue;
            int lo = i, hi = i, dl = 0, dh = 0;
            do { lo = (lo - 1 + N) % N; dl++; } while (_count[lo] == 0 && dl < N);
            do { hi = (hi + 1) % N; dh++; } while (_count[hi] == 0 && dh < N);
            double w = (double)dl / (dl + dh);
            speed[i] = speed[lo] + (speed[hi] - speed[lo]) * w;
            brake[i] = brake[lo] + (brake[hi] - brake[lo]) * w;
            thr[i] = thr[lo] + (thr[hi] - thr[lo]) * w;
            steer[i] = steer[lo] + (steer[hi] - steer[lo]) * w;
            gear[i] = w < 0.5 ? gear[lo] : gear[hi];
            double eLo = lo < i ? elapsed[lo] : 0.0;
            double eHi = hi > i ? elapsed[hi] : lapTime;
            elapsed[i] = eLo + (eHi - eLo) * w;
        }
    }
}
