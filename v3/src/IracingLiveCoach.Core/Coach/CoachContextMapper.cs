using System.Globalization;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Pure mapping from SessionInfo fields to a <see cref="CoachContext"/>.</summary>
public static class CoachContextMapper
{
    /// <summary>Null while the track or the car is not known yet. TrackId = TrackName (fallback "track{id}"),
    /// CarId = CarPath (fallback CarScreenName).</summary>
    public static CoachContext? FromSession(int trackId, string? trackName, string? trackLength,
        string? carPath, string? carScreenName, string? sessionType)
    {
        var track = !string.IsNullOrWhiteSpace(trackName) ? trackName!.Trim() : trackId > 0 ? $"track{trackId}" : null;
        var car = !string.IsNullOrWhiteSpace(carPath) ? carPath!.Trim()
            : !string.IsNullOrWhiteSpace(carScreenName) ? carScreenName!.Trim() : null;
        if (track == null || car == null) return null;
        return new CoachContext(track, car, ParseTrackLengthMeters(trackLength) ?? 0, sessionType?.Trim() ?? "");
    }

    /// <summary>"3.06 km" / "1.9 mi" / "3060 m" -> meters; null when unparseable.</summary>
    public static double? ParseTrackLengthMeters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        double unit = t.EndsWith("km", StringComparison.OrdinalIgnoreCase) ? 1000.0
            : t.EndsWith("mi", StringComparison.OrdinalIgnoreCase) ? 1609.344 : 1.0;
        var num = t.TrimEnd('k', 'm', 'i', 'K', 'M', 'I', ' ');
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v * unit : null;
    }
}
