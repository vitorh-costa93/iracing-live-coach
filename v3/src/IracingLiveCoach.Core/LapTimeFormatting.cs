// src/IracingLiveCoach.Core/LapTimeFormatting.cs
using System.Globalization;

namespace IracingLiveCoach.Core;

/// <summary>Shared m:ss.fff lap-time formatting -- moved here (from a private method duplicated
/// across RelativeWidgetViewModel and StandingsWidgetViewModel with two different formats) so both
/// widgets show the same format, and so this pure function gets real test coverage instead of
/// living inside untestable WPF view-model code.</summary>
public static class LapTimeFormatting
{
    /// <param name="decimalPlaces">Spec §12: "casas decimais por campo numérico" applies to lap
    /// times too -- defaults to the original 3 (m:ss.000) for every existing call site.</param>
    public static string Format(double seconds, int decimalPlaces = 3)
    {
        var minutes = (int)(seconds / 60);
        var remainder = seconds - minutes * 60;
        int decimals = Math.Clamp(decimalPlaces, 0, 6);
        string secondsFormat = decimals > 0 ? "00." + new string('0', decimals) : "00";
        return $"{minutes}:{remainder.ToString(secondsFormat, CultureInfo.InvariantCulture)}";
    }

    /// <summary>Kapps' lap-time format: TRUNCATED to <paramref name="decimalPlaces"/>, never rounded up --
    /// verified on Kapps prints (Watkins Glen 24/09/2026): 104.0597 -> "1:44.059" in qualifying, 108.366 ->
    /// "1:48.3" and 110.281 -> "1:50.2" in the race.</summary>
    public static string FormatTruncated(double seconds, int decimalPlaces)
    {
        int decimals = Math.Clamp(decimalPlaces, 0, 6);
        double scale = Math.Pow(10, decimals);
        double truncated = Math.Floor(seconds * scale + 1e-6) / scale;
        return Format(truncated, decimals);
    }
}
