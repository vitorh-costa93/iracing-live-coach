using System.Runtime.InteropServices;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

// Dump real do AMS2 (v14, Interlagos, corrida pausada, 18 carros), conferido com o HUD em 02/10/2026.
public class RealDumpTests
{
    static RawSharedMemory Load()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "ams2-v14-interlagos.bin"));
        return MemoryMarshal.Read<RawSharedMemory>(bytes);
    }

    [Fact]
    public void Real_v14_dump_maps_to_values_seen_on_the_hud()
    {
        var raw = Load();
        Assert.Equal(14u, raw.Version);
        var s = SnapshotMapper.Map(in raw);

        Assert.Equal("Interlagos", s.Track);
        Assert.Equal(14, s.LapsInEvent);
        Assert.Equal(18, s.Cars.Count);
        Assert.Equal(18, s.PlayerCar!.Position);
        Assert.Equal(51.9, s.Player!.FuelLiters, 0);          // HUD: 51.9 L
        Assert.Equal(30.6, s.Weather.AmbientC, 0);            // HUD: 30 °C
        Assert.Equal(41.9, s.Weather.TrackC, 0);              // HUD: 41 °C
        Assert.InRange(s.Player.Wheels[0].AirPressure, 145, 149); // HUD: ~1.47 bar (kPa)
        Assert.InRange(s.Player.Wheels[2].AirPressure, 130, 134); // HUD: ~1.32 bar
        Assert.Equal("Slick Macio", s.Player.Wheels[0].Compound);
        var leader = s.Cars.Single(c => c.Position == 1);
        Assert.Equal("Markell Fenstermacher", leader.Name);
        Assert.Equal(76.464, leader.LastLapTime, 3);          // menu de pausa: 1:16.464
    }
}
