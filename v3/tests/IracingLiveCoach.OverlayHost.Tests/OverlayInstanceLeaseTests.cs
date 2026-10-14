using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class OverlayInstanceLeaseTests
{
    [Fact]
    public void Another_host_is_rejected_until_first_host_closes()
    {
        string name = "V3-test-" + Guid.NewGuid();
        var first = OverlayInstanceLease.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(OverlayInstanceLease.TryAcquire(name));
        first!.Dispose();
        using var restarted = OverlayInstanceLease.TryAcquire(name);
        Assert.NotNull(restarted);
    }
}
