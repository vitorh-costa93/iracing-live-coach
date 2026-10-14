namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>One V3 host per Windows session, independent of its executable directory.</summary>
public sealed class OverlayInstanceLease : IDisposable
{
    readonly Mutex _mutex;
    OverlayInstanceLease(Mutex mutex) => _mutex = mutex;
    public static OverlayInstanceLease? TryAcquire(string name = @"Local\IracingLiveCoach.V3.OverlayHost")
    {
        var mutex = new Mutex(false, name, out bool created);
        if (created) return new(mutex);
        mutex.Dispose();
        return null;
    }
    public void Dispose() => _mutex.Dispose();
}
