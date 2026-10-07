using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Ams2.Core.Calc;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class RenderPlayerTelemetryTests
{
    static PlayerSnapshot Player => new(0, new(0.2, 0.3, 0, 0, 0, 0, 0, 0), 2, 5000, 9000, 20, 0, 0, [], 0);

    [Fact]
    public void Instruments_use_new_samples_before_provider_snapshot_changes_and_fall_back_when_stale()
    {
        double now = 1;
        var ring = new InputRing(() => now);
        ring.Add(new(1, .8f, .4f, 0, 45, 8000, 10000, 4));
        var fresh = RenderPlayerTelemetry.Read(ring, Player);
        Assert.Equal(45, fresh.SpeedMps); Assert.Equal(8000, fresh.Rpm); Assert.Equal(4, fresh.Gear);
        Assert.Equal(.8, fresh.Throttle, 6);
        now = 1.2;
        Assert.Equal(20, RenderPlayerTelemetry.Read(ring, Player).SpeedMps);
        now = .5;
        Assert.Equal(5000, RenderPlayerTelemetry.Read(ring, Player).Rpm);
        ring.Clear(); Assert.False(ring.TryLatest(out _));
    }

    [Fact]
    public void Missing_instruments_keep_validated_snapshot_and_invalid_pedals_do_not_poison_display()
    {
        var ring = new InputRing(() => 1);
        ring.Add(new(1, .8f, .4f, 0));
        Assert.Equal(2, RenderPlayerTelemetry.Read(ring, Player).Gear);
        ring.Add(new(1, float.NaN, 2, 0, 45, 8000, 10000, 4));
        var values = RenderPlayerTelemetry.Read(ring, Player);
        Assert.Equal(.2, values.Throttle); Assert.Equal(1, values.Brake);
    }

    [Fact]
    public async Task Latest_stays_consistent_while_writer_wraps_the_ring()
    {
        var ring = new InputRing(() => 0, 32);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 200_000; i++) ring.Add(new(i, i, i, i, i, i, i, i));
        });
        do
        {
            if (!ring.TryLatest(out var s)) continue;
            Assert.Equal(s.T, s.SpeedMps); Assert.Equal(s.T, s.Rpm); Assert.Equal(s.T, s.Gear);
        } while (!writer.IsCompleted);
        await writer;
        Assert.True(ring.TryLatest(out var latest)); Assert.Equal(199_999, latest.Gear);
    }

    [Fact]
    public void Minimum_mapped_read_captures_instruments_under_the_same_sequence_lock()
    {
        if (!OperatingSystem.IsWindows()) return;
        string name = "ams2-instruments-" + Guid.NewGuid().ToString("N");
        using var map = MemoryMappedFile.CreateNew(name, Marshal.SizeOf<RawSharedMemory>());
        using var view = map.CreateViewAccessor();
        var raw = new RawSharedMemory { SequenceNumber = 2, Throttle = .8f, Brake = .4f, Steering = -.5f,
            Speed = 45, Rpm = 8000, MaxRpm = 10000, Gear = 4 };
        view.Write(0, ref raw);
        using var source = new MemoryMappedSource(name);
        var ring = new InputRing(() => 1);
        using var sampler = new InputSampler(source, ring, () => 1);
        Assert.True(sampler.PollOnce()); Assert.True(ring.TryLatest(out var s));
        Assert.Equal(45, s.SpeedMps); Assert.Equal(8000, s.Rpm); Assert.Equal(10000, s.MaxRpm); Assert.Equal(4, s.Gear);
        raw.SequenceNumber = 3; view.Write(0, ref raw);
        Assert.False(source.TryReadInputs(out _));
    }
}
