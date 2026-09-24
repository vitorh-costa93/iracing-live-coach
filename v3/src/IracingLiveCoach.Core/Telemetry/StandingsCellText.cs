using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Direction of the positions-gained/lost marker.</summary>
public enum PositionTrend { None, Up, Down }

/// <summary>
/// Text of the Standings cells whose format is Kapps' own, kept out of the Direct2D widget so it is
/// unit-tested. Evidence (Kapps prints, Watkins Glen GT3 24/09/2026 and SF23 Interlagos):
///  * interval: class leader "INT"; seconds with ONE decimal in a race ("1.5", "0.4", "0.0") and THREE in
///    practice/qualifying and on the pre-green grid ("0.161", "0.004"); "1L" when the car ahead in class is a
///    lap or more up the road; unsigned. Missing = "—".
///  * lap column: best lap when ordered by best lap, else last lap; truncated, 3 / 1 decimals.
///  * positions gained/lost: chevron + number, green up / red down, nothing when unchanged ("▼ 25" for the
///    pole-sitter running 26th).
/// </summary>
public static class StandingsCellText
{
    public static string Interval(StandingsRow row)
    {
        int classPos = row.ClassPosition > 0 ? row.ClassPosition : row.Position;
        if (classPos == 1) return "INT";
        if (row.IntervalLaps is int laps and > 0) return $"{laps}L";
        if (row.IntervalSeconds is double s)
            return s.ToString(row.TimedOrder ? "0.000" : "0.0", CultureInfo.InvariantCulture);
        return "—";
    }

    public static (PositionTrend Trend, string Number) PositionChange(int? change) => change switch
    {
        > 0 and int up => (PositionTrend.Up, up.ToString(CultureInfo.InvariantCulture)),
        < 0 and int down => (PositionTrend.Down, (-down).ToString(CultureInfo.InvariantCulture)),
        _ => (PositionTrend.None, ""),
    };

    /// <summary>Kapps' lap column: the BEST lap when the order is by best lap (practice, qualifying, pre-green
    /// grid showing the qualifying laps), otherwise the last lap.</summary>
    public static double? LapColumn(StandingsRow row) => row.TimedOrder ? row.BestLapTime : row.LastLapTime;

    /// <summary>Lap column text as Kapps prints it: truncated, 3 decimals by best lap ("1:44.059"), 1 in a
    /// race ("1:48.3"); "—" when there is no lap.</summary>
    public static string LapText(StandingsRow row) =>
        LapColumn(row) is double lap && lap > 0 ? LapTimeFormatting.FormatTruncated(lap, row.TimedOrder ? 3 : 1) : "—";
}
