using Ams2.Core.Calc;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class InputRingTests
{
    static InputSample S(double t, float v = 0) => new(t, v, v / 2, -v);

    [Fact]
    public void Copies_window_in_time_order_from_the_requested_start()
    {
        var ring = new InputRing(() => 0, 64);
        for (int i = 0; i < 20; i++) ring.Add(S(i * 0.1, i));
        var dest = new InputSample[ring.Usable];
        int n = ring.CopyFrom(0.95, dest);
        Assert.Equal(10, n);                      // t = 1.0 .. 1.9
        Assert.Equal(1.0, dest[0].T, 6);
        Assert.Equal(1.9, dest[n - 1].T, 6);
        for (int i = 1; i < n; i++) Assert.True(dest[i].T > dest[i - 1].T);
    }

    [Fact]
    public void Wraps_and_keeps_only_the_newest_samples_without_allocating_a_new_buffer()
    {
        var ring = new InputRing(() => 0, 64);
        for (int i = 0; i < 1000; i++) ring.Add(S(i, i));
        var dest = new InputSample[ring.Usable];
        int n = ring.CopyFrom(double.NegativeInfinity, dest);
        Assert.Equal(ring.Usable, n);
        Assert.Equal(999, dest[n - 1].T);
        Assert.Equal(999 - n + 1, dest[0].T);
        Assert.Equal(1000, ring.Count);
    }

    [Fact]
    public void Clear_forgets_history_but_keeps_accepting_samples()
    {
        var ring = new InputRing(() => 0, 64);
        for (int i = 0; i < 10; i++) ring.Add(S(i));
        ring.Clear();
        var dest = new InputSample[ring.Usable];
        Assert.Equal(0, ring.CopyFrom(double.NegativeInfinity, dest));
        Assert.Equal(double.NegativeInfinity, ring.LastT);
        ring.Add(S(0.5));
        Assert.Equal(1, ring.CopyFrom(double.NegativeInfinity, dest));
        Assert.Equal(0.5, ring.LastT);
    }

    [Fact]
    public void Concurrent_reader_never_sees_a_torn_or_out_of_order_sample()
    {
        var ring = new InputRing(() => 0, 256);
        using var cts = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 400_000 && !cts.IsCancellationRequested; i++) ring.Add(new InputSample(i, i, i, i)); // todos os campos iguais a T
        });
        var dest = new InputSample[ring.Usable];
        int reads = 0;
        while (!writer.IsCompleted)
        {
            int n = ring.CopyFrom(double.NegativeInfinity, dest);
            reads++;
            for (int i = 0; i < n; i++)
            {
                Assert.Equal((float)dest[i].T, dest[i].Throttle);
                Assert.Equal((float)dest[i].T, dest[i].Steering);
                if (i > 0) Assert.Equal(dest[i - 1].T + 1, dest[i].T); // contiguo: nada pulado nem repetido
            }
        }
        writer.Wait();
        Assert.True(reads > 0);
    }

    [Fact]
    public void Rejects_non_power_of_two_capacity() => Assert.Throws<ArgumentException>(() => new InputRing(() => 0, 100));
}

public class InputSamplerTests
{
    sealed class Src : IRawMemorySource
    {
        public uint Seq; public float Thr; public bool Connected = true; public int Reads;
        public bool TryRead(out Ams2.Core.Raw.RawSharedMemory raw) { raw = default; return false; } // so o caminho minimo e usado
        public bool TryReadInputs(out RawInputs r)
        {
            Reads++;
            r = new RawInputs(Seq, Thr, 0, 0);
            return Connected && Seq % 2 == 0;
        }
        public void Dispose() { }
    }

    [Fact]
    public void Records_one_sample_per_game_write_at_the_moment_it_is_seen()
    {
        double now = 0;
        var src = new Src();
        var ring = new InputRing(() => now, 256);
        var stats = new RateStats();
        using var sampler = new InputSampler(src, ring, () => now, stats, maxHz: 1000);
        var dest = new InputSample[ring.Usable];

        src.Seq = 2; src.Thr = 0.1f; now = 0.010; Assert.True(sampler.PollOnce());
        now = 0.011; Assert.False(sampler.PollOnce());            // mesma escrita do jogo: nada novo
        src.Seq = 4; src.Thr = 0.2f; now = 0.016; Assert.True(sampler.PollOnce());
        src.Seq = 5; src.Thr = 0.9f; now = 0.020; Assert.False(sampler.PollOnce()); // escrita em andamento (impar): ignora
        src.Seq = 6; src.Thr = 0.3f; now = 0.022; Assert.True(sampler.PollOnce());

        int n = ring.CopyFrom(double.NegativeInfinity, dest);
        Assert.Equal(3, n);
        Assert.Equal([0.010, 0.016, 0.022], dest.Take(n).Select(s => s.T).ToArray());
        Assert.Equal([0.1f, 0.2f, 0.3f], dest.Take(n).Select(s => s.Throttle).ToArray());
        Assert.Equal(3, stats.Count);
    }

    [Fact]
    public void Caps_the_rate_without_even_reading_the_memory()
    {
        double now = 0;
        var src = new Src();
        var ring = new InputRing(() => now, 256);
        using var sampler = new InputSampler(src, ring, () => now, null, maxHz: 100); // 10 ms
        int taken = 0;
        for (int i = 1; i <= 1000; i++) { src.Seq += 2; now = i * 0.001; if (sampler.PollOnce()) taken++; } // sonda a 1 kHz por 1 s
        Assert.InRange(taken, 90, 101);
        Assert.True(src.Reads < 200);                                 // so le quando o intervalo minimo venceu
    }

    [Fact]
    public void Clock_going_backwards_clears_the_history()
    {
        double now = 5;
        var src = new Src { Seq = 2 };
        var ring = new InputRing(() => now, 256);
        using var sampler = new InputSampler(src, ring, () => now, null, 1000);
        Assert.True(sampler.PollOnce());
        now = 1; src.Seq = 4;
        Assert.True(sampler.PollOnce());
        var dest = new InputSample[ring.Usable];
        Assert.Equal(1, ring.CopyFrom(double.NegativeInfinity, dest));
        Assert.Equal(1, dest[0].T);
    }

    [Fact]
    public void Default_TryReadInputs_uses_the_full_struct_and_refuses_odd_sequence()
    {
        var mem = new FakeMemory();
        mem.Raw.Throttle = 0.4f; mem.Raw.Brake = 0.2f; mem.Raw.Steering = -0.5f; mem.Raw.SequenceNumber = 8;
        mem.Raw.Speed = 45; mem.Raw.Rpm = 8000; mem.Raw.MaxRpm = 10000; mem.Raw.Gear = 4;
        IRawMemorySource src = mem;
        Assert.True(src.TryReadInputs(out var r));
        Assert.Equal(new RawInputs(8, 0.4f, 0.2f, -0.5f, 45, 8000, 10000, 4), r);
        mem.Raw.SequenceNumber = 9;
        Assert.False(src.TryReadInputs(out _));
    }
}

public class RateStatsTests
{
    [Fact]
    public void Reports_rate_and_interval_percentiles()
    {
        var s = new RateStats(256);
        for (int i = 0; i < 101; i++) s.Mark(10 + i * 0.005); // 200 Hz exatos
        var r = s.Report();
        Assert.Equal(101, r.Frames);
        Assert.InRange(r.PerSecond, 199.9, 200.1);
        Assert.InRange(r.P50Ms, 4.99, 5.01);
        var warm = s.Report(fromT: 10.25);                      // descarta o aquecimento
        Assert.True(warm.Frames < r.Frames);
    }
}
