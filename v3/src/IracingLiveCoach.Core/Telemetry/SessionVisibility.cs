namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Coarse session category used for per-session-type widget visibility (spec §12:
/// "visibilidade por tipo de sessão").</summary>
public enum SessionKind { Practice, Qualify, Race }

public static class SessionKinds
{
    /// <summary>Buckets iRacing's SessionType text ("PRACTICE", "OPEN QUALIFY", "LONE QUALIFY",
    /// "RACE", "WARMUP", "OFFLINE TESTING", ...). Returns null for empty/unrecognized text: an
    /// unknown session is never a reason to hide a widget.</summary>
    public static SessionKind? Classify(string? sessionTypeText)
    {
        if (string.IsNullOrWhiteSpace(sessionTypeText)) return null;
        var t = sessionTypeText.Trim().ToUpperInvariant();
        if (t.Contains("QUALIFY")) return SessionKind.Qualify;
        if (t.Contains("RACE")) return SessionKind.Race;
        if (t.Contains("PRACTICE") || t.Contains("WARMUP") || t.Contains("TESTING")) return SessionKind.Practice;
        return null;
    }
}

/// <summary>Per-widget list of session kinds in which the widget is HIDDEN. Absent widget or
/// empty list = shown in every session (the default). Kinds are stored as strings so the value
/// round-trips through JSON and the IPC wire format without sharing an enum across processes.</summary>
public sealed record SessionVisibilityConfig(Dictionary<string, List<string>> HiddenIn)
{
    public static SessionVisibilityConfig Default => new(new Dictionary<string, List<string>>());

    public bool IsVisible(string widgetKey, SessionKind? kind)
    {
        if (kind is null) return true;
        return !(HiddenIn.TryGetValue(widgetKey, out var hidden) && hidden.Contains(kind.Value.ToString()));
    }
}
