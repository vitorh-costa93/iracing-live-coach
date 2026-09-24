using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Driver name display format (spec §12: "formato diferente de exibição de nome").
/// FirstLast was added later (item 10) -- Full=0/Abbreviated=1 keep their existing numeric values so
/// saved profiles never change meaning.</summary>
public enum NameDisplayFormat { Full, Abbreviated, FirstLast }

/// <summary>Reformats an already-resolved full driver name (as <see cref="TelemetryReader"/>'s own
/// row records already carry it, e.g. "Vitor Costa") for display -- kept separate from
/// <see cref="TelemetryReader"/>'s own name resolution so a live format change never needs to
/// re-read session info or touch the identity-caching logic built there, only how the cached full
/// name is drawn.</summary>
public static class NameDisplay
{
    public static string Format(string fullName, NameDisplayFormat format)
    {
        if (format == NameDisplayFormat.Full || string.IsNullOrWhiteSpace(fullName)) return fullName;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1) return fullName;
        string last = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[^1].ToLowerInvariant());
        if (format == NameDisplayFormat.FirstLast)
            return $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[0].ToLowerInvariant())} {last}";
        return $"{char.ToUpperInvariant(parts[0][0])}. {last}";
    }
}
