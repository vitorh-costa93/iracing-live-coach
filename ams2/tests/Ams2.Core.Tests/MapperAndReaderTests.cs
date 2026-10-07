using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class MapperAndReaderTests
{
    [Fact]
    public void Layout_matches_header_v9()
    {
        Assert.Equal(20576, Marshal.SizeOf<RawSharedMemory>());
        Assert.Equal(100, Marshal.SizeOf<ParticipantInfo>());
        Assert.Equal(28, (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.Participants)));
        Assert.Equal(7320, (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.SequenceNumber)));
        Assert.Equal(20572, (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.SnowDensity)));
    }

    [Fact]
    public void Maps_cars_player_fuel_and_class_positions()
    {
        var m = new FakeMemory();
        m.Raw.ViewedParticipantIndex = 0;
        m.SetCar(0, "Player", "Car A", "GT3", 3, 1000);
        m.SetCar(1, "Rival", "Car B", "LMP", 1, 1500);
        m.SetCar(2, "Other", "Car C", "GT3", 2, 900);
        m.Raw.FuelLevel = 0.5f; m.Raw.FuelCapacity = 80;
        FakeMemory.Put(MemoryMarshal.CreateSpan(ref m.Raw.TyreCompound[40], 40), "Soft");
        FakeMemory.Put(MemoryMarshal.CreateSpan(ref m.Raw.TrackLocation[0], 64), "Spa");

        var s = SnapshotMapper.Map(in m.Raw);

        Assert.Equal("Spa", s.Track);
        Assert.Equal(3, s.Cars.Count);
        Assert.Equal(40, s.Player!.FuelLiters, 3);
        Assert.Equal("Soft", s.Player.Wheels[1].Compound);
        Assert.Equal("Player", s.PlayerCar!.Name);
        Assert.Equal(2, s.Cars.Single(c => c.Name == "Player").ClassPosition); // 2º entre os GT3
        Assert.Equal(1, s.Cars.Single(c => c.Name == "Rival").ClassPosition);
    }

    [Fact]
    public void Inactive_participants_are_skipped_and_unknown_viewed_index_has_no_player()
    {
        var m = new FakeMemory();
        m.Raw.ViewedParticipantIndex = -1;
        m.SetCar(0, "A", "c", "x", 1, 0);
        m.SetCar(1, "B", "c", "x", 2, 0);
        m.Raw.Participants[1].IsActive = 0;
        var s = SnapshotMapper.Map(in m.Raw);
        Assert.Single(s.Cars);
        Assert.Null(s.Player);
    }

    [Fact]
    public void Reader_reports_disconnected_version_mismatch_and_ok()
    {
        var m = new FakeMemory { Connected = false };
        var r = new SharedMemoryReader(m);
        Assert.Equal(ReadStatus.Disconnected, r.Poll().Status);
        m.Connected = true;
        m.Raw.Version = 7;
        Assert.Equal(ReadStatus.VersionMismatch, r.Poll().Status);
        m.Raw.Version = 9;
        var ok = r.Poll();
        Assert.Equal(ReadStatus.Ok, ok.Status);
        Assert.NotNull(ok.Snapshot);
    }

    [Fact]
    public void Reader_retries_torn_read_then_succeeds()
    {
        var m = new FakeMemory();
        int calls = 0;
        m.OnRead = raw => { raw.SequenceNumber = calls++ < 2 ? 3u : 4u; return raw; };
        var res = new SharedMemoryReader(m).Poll();
        Assert.Equal(ReadStatus.Ok, res.Status);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Reader_gives_up_when_sequence_stays_odd()
    {
        var m = new FakeMemory { OnRead = raw => { raw.SequenceNumber = 5; return raw; } };
        Assert.Equal(ReadStatus.Torn, new SharedMemoryReader(m, 3).Poll().Status);
    }

    [Fact]
    public void MemoryMappedSource_roundtrip_through_real_named_map()
    {
        string name = "ams2test_" + Guid.NewGuid().ToString("N");
        int size = Marshal.SizeOf<RawSharedMemory>();
        using var mmf = MemoryMappedFile.CreateNew(name, size);
        using var view = mmf.CreateViewAccessor();
        var raw = new RawSharedMemory { Version = 9, NumParticipants = 1, ViewedParticipantIndex = 0, TrackLength = 5000, SequenceNumber = 2 };
        raw.Participants[0].IsActive = 1; raw.Participants[0].RacePosition = 1;
        FakeMemory.Put(MemoryMarshal.CreateSpan(ref raw.Participants[0].Name[0], 64), "Solo");
        view.Write(0, ref raw);

        using var reader = new SharedMemoryReader(new MemoryMappedSource(name));
        var res = reader.Poll();
        Assert.Equal(ReadStatus.Ok, res.Status);
        Assert.Equal("Solo", res.Snapshot!.Cars[0].Name);

        using var missing = new SharedMemoryReader(new MemoryMappedSource("ams2_nope_" + Guid.NewGuid().ToString("N")));
        Assert.Equal(ReadStatus.Disconnected, missing.Poll().Status);
    }
}
