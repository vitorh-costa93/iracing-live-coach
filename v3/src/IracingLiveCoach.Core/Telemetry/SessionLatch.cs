namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Identifies one session of one sim run: telemetry SessionUniqueID + SessionNum (practice,
/// qualifying and race of the same event share the unique id but not the number; a new test drive /
/// AI event gets a new unique id).</summary>
public readonly record struct SessionKey(int UniqueId, int SessionNum);

/// <summary>
/// Captures a value the first time it is available in a session and keeps it fixed until the session
/// changes (no carry-over: a new session = a new capture). Used for the track rubber state, the driver's
/// own rule: "capturar no primeiro acesso a cada sessão e manter fixo". Thread-safe, because every
/// widget has its own TelemetryReader thread and they share one instance so they all show the same value.
/// </summary>
public sealed class SessionLatch<T> where T : class
{
    private readonly object _gate = new();
    private SessionKey? _key;
    private T? _value;

    /// <summary>Returns the value captured for <paramref name="key"/>; if none yet, stores
    /// <paramref name="current"/> (when non-null/non-empty) and returns it.</summary>
    public T? Get(SessionKey key, T? current)
    {
        lock (_gate)
        {
            if (_key != key) { _key = key; _value = null; }
            if (_value is null && current is not null && !(current is string s && string.IsNullOrWhiteSpace(s)))
                _value = current;
            return _value;
        }
    }

    public void Reset()
    {
        lock (_gate) { _key = null; _value = null; }
    }
}
