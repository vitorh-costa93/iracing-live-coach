using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class CarServiceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ilc-store-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "pit-service.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static void Visit(PitLossModel m, double t0, double still, double fuelIn, double fuelOut)
    {
        m.Observe(0, false, 0.90, t0 - 1, 100, true, fuelIn, false);
        m.Observe(0, true, 0.95, t0, 100, true, fuelIn, false);
        m.Observe(0, true, 0.96, t0 + 1, 100, true, fuelIn, false);
        m.Observe(0, true, 0.97, t0 + 2, 100, true, fuelIn, false);
        for (int i = 1; i <= (int)still; i++)
            m.Observe(0, true, 0.97, t0 + 2 + i, 100, true, fuelIn + (fuelOut - fuelIn) * i / still, false);
        m.Observe(0, false, 0.05, t0 + still + 22, 100, true, fuelOut, false);
    }

    [Fact]
    public void Round_trip_and_key()
    {
        var s = new CarServiceStore(FilePath);
        s.Save("ferrari296gt3", new CarServiceProfile(new() { 2.5, 2.7 }, new() { 9.0 }));
        var p = new CarServiceStore(FilePath).Load("ferrari296gt3")!;
        Assert.Equal(new[] { 2.5, 2.7 }, p.FillRateLps);
        Assert.Equal(new[] { 9.0 }, p.TireSec);
        Assert.Null(s.Load("other"));
        Assert.Equal("ferrari296gt3", CarServiceStore.CarKey(" Ferrari296GT3 ", "x"));
        Assert.Equal("name", CarServiceStore.CarKey(null, "Name"));
        Assert.Null(CarServiceStore.CarKey("", " "));
    }

    [Fact]
    public void Corrupt_or_missing_file_is_ignored_and_overwritten()
    {
        var s = new CarServiceStore(FilePath);
        Assert.Null(s.Load("a"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Null(s.Load("a"));
        s.Save("a", new CarServiceProfile(new() { 3.0 }, new()));
        Assert.Equal(3.0, s.Load("a")!.FillRateLps[0]);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Implausible_values_in_file_are_dropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"version\":1,\"cars\":{\"a\":{\"fillRateLps\":[0.01,3.0,999],\"tireSec\":[0,8]}}}");
        var p = new CarServiceStore(FilePath).Load("a")!;
        Assert.Equal(new[] { 3.0 }, p.FillRateLps);
        Assert.Equal(new[] { 8.0 }, p.TireSec);
    }

    [Fact]
    public void Persisted_rate_is_used_before_first_stop_and_survives_track_reset()
    {
        var store = new CarServiceStore(FilePath);
        store.Save("car", new CarServiceProfile(new() { 5.0 }, new()));
        var m = new PitLossModel { Store = store };
        m.SetCar("car");
        Visit(m, 1, 10, -1, -1); // lane sample from a visit without fuel change
        m.Reset();               // track change: lane forgotten, rate kept
        Assert.Equal(5.0, m.StoredFillRateLps!.Value, 6);
        Visit(m, 100, 4, 10, 10); // learn a lane loss again (no refuel)
        var e = m.Estimate(30, false, 0, 100, double.NaN, double.NaN);
        Assert.Equal(6.0, e.ServiceSec, 6);
        Assert.True(e.Estimated);
        Assert.Contains("previous session", e.Note);
    }

    [Fact]
    public void Own_session_rate_beats_persisted_and_is_saved()
    {
        var store = new CarServiceStore(FilePath);
        store.Save("car", new CarServiceProfile(new() { 5.0 }, new()));
        var m = new PitLossModel { Store = store };
        m.SetCar("car");
        Visit(m, 100, 8, 10, 50); // 5 L/s -> same; use a different one:
        Visit(m, 300, 10, 10, 60); // 5 L/s too; check own wins with 4 L/s
        var m2 = new PitLossModel { Store = store };
        m2.SetCar("car");
        Visit(m2, 100, 10, 10, 50); // 4 L/s
        Assert.Equal(4.0, m2.LearnedFillRateLps!.Value, 6);
        var e = m2.Estimate(40, false, 0, 100, double.NaN, double.NaN);
        Assert.Equal(10.0, e.ServiceSec, 6);
        Assert.False(e.Estimated);
        var saved = store.Load("car")!.FillRateLps;
        Assert.Equal(4.0, saved[^1], 6);
        Assert.Equal(new CarServiceStore(FilePath).Load("car")!.FillRateLps, saved);
    }

    [Fact]
    public void Outlier_rate_is_discarded_and_not_persisted_and_samples_are_capped()
    {
        var store = new CarServiceStore(FilePath);
        var m = new PitLossModel { Store = store };
        m.SetCar("car");
        Visit(m, 100, 8, 10, 1000); // 123 L/s
        Visit(m, 300, 8, 10, 11);   // 0.125 L/s
        Assert.Null(m.LearnedFillRateLps);
        Assert.Null(store.Load("car"));
        for (int i = 0; i < 12; i++) Visit(m, 1000 + i * 100, 8, 10, 50);
        Assert.Equal(CarServiceStore.MaxSamples, store.Load("car")!.FillRateLps.Count);
    }

    [Fact]
    public void Without_data_falls_back_to_unknown_note()
    {
        var m = new PitLossModel { Store = new CarServiceStore(FilePath) };
        m.SetCar("car");
        Visit(m, 1, 0, -1, -1);
        Visit(m, 100, 1, -1, -1);
        var e = m.Estimate(30, false, 0, 100, double.NaN, double.NaN);
        Assert.Contains("refuel time unknown", e.Note);
    }
}
