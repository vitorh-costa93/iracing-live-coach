using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Driver name display format (spec §12: "formato diferente de exibição de nome").</summary>
public enum NameDisplayFormat { Full, Abbreviated }

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
        return $"{char.ToUpperInvariant(parts[0][0])}. {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[^1].ToLowerInvariant())}";
    }
}
