// src/IracingLiveCoach.Core/LapTimeFormatting.cs
using System.Globalization;

namespace IracingLiveCoach.Core;

/// <summary>Shared m:ss.fff lap-time formatting -- moved here (from a private method duplicated
/// across RelativeWidgetViewModel and StandingsWidgetViewModel with two different formats) so both
/// widgets show the same format, and so this pure function gets real test coverage instead of
/// living inside untestable WPF view-model code.</summary>
public static class LapTimeFormatting
{
    public static string Format(double seconds)
    {
        var minutes = (int)(seconds / 60);
        var remainder = seconds - minutes * 60;
        return $"{minutes}:{remainder.ToString("00.000", CultureInfo.InvariantCulture)}";
    }
}
