namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Track rubber shown by the Weather widget (the driver's own rule -- Kapps has no such field). iRacing
/// publishes "carry over" for a session that inherits the previous session's rubber (live 24/09/2026:
/// Practice "high usage", Lone Qualify "carry over", Race "carry over"), so "carry over" is resolved by
/// walking back through the earlier sessions of the event until one has a real state. Nothing to walk
/// back to: "carry over" itself (what iRacing says). Pure, unit-tested.
/// </summary>
public static class RubberState
{
    public const string CarryOver = "carry over";

    /// <param name="stateBySessionNum">SessionNum -> SessionTrackRubberState, every session of the event.</param>
    public static string? Resolve(IReadOnlyDictionary<int, string?> stateBySessionNum, int currentSessionNum)
    {
        if (!stateBySessionNum.TryGetValue(currentSessionNum, out var current) || string.IsNullOrWhiteSpace(current)) return null;
        if (!IsCarryOver(current)) return current;
        for (int n = currentSessionNum - 1; n >= 0; n--)
        {
            if (!stateBySessionNum.TryGetValue(n, out var earlier) || string.IsNullOrWhiteSpace(earlier)) continue;
            if (!IsCarryOver(earlier)) return earlier;
        }
        return current;
    }

    public static bool IsCarryOver(string? state) =>
        state is not null && string.Equals(state.Trim(), CarryOver, StringComparison.OrdinalIgnoreCase);
}
