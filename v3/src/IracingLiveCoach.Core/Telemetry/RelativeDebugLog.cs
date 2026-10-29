using System.Globalization;
using System.Text;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One Relative row as logged (raw inputs and both gaps, before/after the monotonic floor).</summary>
public readonly record struct RelativeDebugRow(string Side, int Offset, int CarIdx, int ClassId, double ClassLapSec, bool SameClassAsPlayer,
    double Pct, double WrappedDelta, double? EstTimeRaw, double? PlayerEstTime, double PlayerPct, double? CurveAtPlayer,
    double? GapDirect, double? GapShown, bool OnPitRoad, int TrackSurface, int LapCompleted);

/// <summary>One Standings class-consecutive interval as logged.</summary>
public readonly record struct StandingsDebugRow(int CarIdx, int ClassId, int ClassPosition, double? Progress, double? EstTime,
    string Interval, string Branch);

/// <summary>Pure CSV formatting for the always-on Relative/Standings debug logs (analysis after the race).</summary>
public static class RelativeDebugFormat
{
    public const string RelativeHeader = "wallClock,sessionTime,sessionKind,sessionState,side,offsetShown,carIdx,classId,classLapSec,sameClassAsPlayer,pct,wrappedDelta,estTimeRaw,playerEstTime,playerPct,curveAtPlayerForThatClass,gapDirect,gapShown,onPitRoad,trackSurface,lapCompleted";
    public const string StandingsHeader = "wallClock,sessionTime,sessionKind,sessionState,carIdx,classId,classPosition,progress,estTime,interval,branch";

    public static string Num(double? v) => v is double d && double.IsFinite(d) ? d.ToString("0.####", CultureInfo.InvariantCulture) : "";

    public static string Stamp(DateTime local) => local.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static string RelativeLine(DateTime wall, double sessionTime, string kind, int state, RelativeDebugRow r) => string.Join(',',
        Stamp(wall), Num(sessionTime), kind, I(state), r.Side, I(r.Offset), I(r.CarIdx), I(r.ClassId), Num(r.ClassLapSec),
        r.SameClassAsPlayer ? "1" : "0", Num(r.Pct), Num(r.WrappedDelta), Num(r.EstTimeRaw), Num(r.PlayerEstTime), Num(r.PlayerPct),
        Num(r.CurveAtPlayer), Num(r.GapDirect), Num(r.GapShown), r.OnPitRoad ? "1" : "0", I(r.TrackSurface), I(r.LapCompleted));

    public static string StandingsLine(DateTime wall, double sessionTime, string kind, int state, StandingsDebugRow r) => string.Join(',',
        Stamp(wall), Num(sessionTime), kind, I(state), I(r.CarIdx), I(r.ClassId), I(r.ClassPosition), Num(r.Progress), Num(r.EstTime), r.Interval, r.Branch);

    /// <summary>"12.3" seconds, "2L" laps, "" none.</summary>
    public static string Interval(IntervalValue v) => v.Laps is int l ? l + "L" : Num(v.Seconds);
}

/// <summary>Time-based sampler (pure: the clock is passed in).</summary>
public sealed class DebugSampler
{
    private readonly TimeSpan _every;
    private DateTime _last = DateTime.MinValue;
    public DebugSampler(TimeSpan every) => _every = every;

    public bool Due(DateTime now)
    {
        if (now >= _last && now - _last < _every) return false;
        _last = now;
        return true;
    }
}

/// <summary>
/// Append-only CSV file, flushed on every write. When a file passes <c>maxFileBytes</c> it rotates to
/// "-2", "-3"...; after <c>maxFiles</c> files it stops writing. Any IO failure is swallowed (a debug log
/// must never break the reader).
/// </summary>
public sealed class DebugCsvWriter
{
    private readonly string _basePath, _header;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private int _fileNo;
    private long _bytes;
    private StreamWriter? _writer;
    private bool _dead;

    public DebugCsvWriter(string basePath, string header, long maxFileBytes = 25L * 1024 * 1024, int maxFiles = 2)
    {
        _basePath = basePath; _header = header; _maxFileBytes = maxFileBytes; _maxFiles = maxFiles;
    }

    public string CurrentPath => _fileNo <= 1 ? _basePath
        : Path.Combine(Path.GetDirectoryName(_basePath) ?? "", Path.GetFileNameWithoutExtension(_basePath) + "-" + _fileNo + Path.GetExtension(_basePath));

    public static string DefaultPath(string prefix, DateTime local) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach",
        prefix + "-" + local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");

    public void Append(IEnumerable<string> lines)
    {
        if (_dead) return;
        try
        {
            foreach (var line in lines)
            {
                if (_writer is null || _bytes >= _maxFileBytes)
                {
                    _writer?.Dispose(); _writer = null;
                    if (_fileNo >= _maxFiles) { _dead = true; return; }
                    _fileNo++;
                    var path = CurrentPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                    _bytes = new FileInfo(path).Length;
                    if (_bytes == 0) { _writer.WriteLine(_header); _bytes += _header.Length + 2; }
                }
                _writer.WriteLine(line);
                _bytes += line.Length + 2;
            }
            _writer?.Flush();
        }
        catch
        {
            _dead = true;
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
