using System.Runtime.InteropServices;
using Ams2.Core.Calc;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class RadarTests
{
    const double Len = 5000;

    /// <summary>Cena: o jogador (indice 0) em (px, pz) com o yaw do jogo; cada carro de <paramref name="others"/> a (frente, direita) metros dele.
    /// Convencao confirmada em sessao real: rumo = yaw + pi, avanco = (sin h, cos h), direita = (cos h, -sin h).</summary>
    static Sim Scene(double yaw, params (double Fwd, double Right)[] others)
    {
        var cars = new (double, double)[others.Length + 1];
        cars[0] = (1000, 60);
        for (int i = 0; i < others.Length; i++) cars[i + 1] = (1000 + others[i].Fwd, 60);
        var sim = new Sim(Len, cars) { PlayerIndex = 0 };
        const double px = 120, pz = -340, py = 5;
        double h = yaw + Math.PI, fx = Math.Sin(h), fz = Math.Cos(h), rx = Math.Cos(h), rz = -Math.Sin(h);
        sim.Poses[0] = (px, py, pz, yaw);
        for (int i = 0; i < others.Length; i++)
            sim.Poses[i + 1] = (px + others[i].Fwd * fx + others[i].Right * rx, py, pz + others[i].Fwd * fz + others[i].Right * rz, yaw);
        return sim;
    }

    static RadarFrame Run(Sim sim, RadarOptions? o = null)
    {
        var t = new RadarTracker();
        if (o is not null) t.Options = o;
        return t.Update(sim.Snapshot(), 0);
    }

    [Fact]
    public void Car_on_the_left_and_on_the_right_are_alerts_with_the_correct_side()
    {
        var f = Run(Scene(1.327, (0.5, -3.5), (-2, 3.5)));
        Assert.True(f.Valid);
        Assert.Equal(2, f.Count);
        var left = f.Cars[0]; var right = f.Cars[1];
        Assert.Equal((RadarSide.Left, RadarZone.Alert), (left.Side, left.Zone));
        Assert.Equal((RadarSide.Right, RadarZone.Alert), (right.Side, right.Zone));
        Assert.Equal(-3.5, left.Right, 3); Assert.Equal(0.5, left.Forward, 3);
        Assert.True(f.AlertLeft && f.AlertRight);
        Assert.Equal(0.5, f.LeftOffset, 3); Assert.Equal(-2, f.RightOffset, 3);
    }

    [Fact]
    public void Car_behind_and_ahead_in_the_same_lane_are_warnings_not_side_alerts()
    {
        var f = Run(Scene(-2.88, (-6, 0.2), (10, -0.3), (14, 0)));
        Assert.Equal(3, f.Count);
        var behind = f.Cars[0]; var ahead = f.Cars[1]; var far = f.Cars[2];
        Assert.Equal((-6f, RadarSide.Center, RadarZone.Warn), (behind.Forward, behind.Side, behind.Zone));
        Assert.Equal((10f, RadarSide.Center, RadarZone.Warn), (ahead.Forward, ahead.Side, ahead.Zone));
        Assert.Equal(RadarZone.Far, far.Zone);
        Assert.False(f.AlertLeft || f.AlertRight);
        Assert.Contains(f.Cars.ToArray(), c => c.Forward < 0);   // atras = frente negativa
    }

    [Theory]
    [InlineData(0.0)] [InlineData(1.327)] [InlineData(-2.88)] [InlineData(Math.PI)] [InlineData(-Math.PI + 0.01)] [InlineData(2.5)]
    public void Result_does_not_depend_on_the_player_orientation(double yaw)
    {
        var f = Run(Scene(yaw, (4, -3), (-9, 2.5), (11, 0.4)));
        Assert.Equal(3, f.Count);
        // ordenado por |frente|: 4, -9, 11
        Assert.Equal(4, f.Cars[0].Forward, 2); Assert.Equal(-3, f.Cars[0].Right, 2);
        Assert.Equal(-9, f.Cars[1].Forward, 2); Assert.Equal(2.5, f.Cars[1].Right, 2);
        Assert.Equal(11, f.Cars[2].Forward, 2); Assert.Equal(0.4, f.Cars[2].Right, 2);
        Assert.Equal(RadarSide.Left, f.Cars[0].Side); Assert.Equal(RadarSide.Right, f.Cars[1].Side); Assert.Equal(RadarSide.Center, f.Cars[2].Side);
    }

    [Fact]
    public void Relative_heading_and_speed_come_from_yaw_difference_and_speeds()
    {
        var sim = Scene(0.5, (5, 3.5));
        sim.Speeds[0] = 60; sim.Speeds[1] = 70;
        var p = sim.Poses[1]; sim.Poses[1] = (p.X, p.Y, p.Z, 0.5 + 0.2);   // o outro carro virou 0,2 rad para o mesmo lado do yaw
        var c = Run(sim).Cars[0];
        Assert.Equal(0.2, c.RelHeading, 3);
        Assert.Equal(10, c.RelSpeed, 3);
        Assert.Equal(70 * Math.Cos(0.2) - 60, c.VForward, 2);
    }

    [Fact]
    public void Lapped_cars_count_because_the_radar_is_geometric()
    {
        // O retardatario esta 2 voltas atras mas ao lado do jogador: aparece e e Alert.
        var sim = new Sim(Len, (3 * Len + 1000, 60), (1000, 55)) { PlayerIndex = 0 };
        sim.Poses[0] = (0, 0, 0, Math.PI);          // rumo 2 pi = 0: avanco +z, direita +x
        sim.Poses[1] = (3.2, 0, -1, Math.PI);
        var f = Run(sim);
        Assert.Equal(1, f.Count);
        Assert.Equal((RadarSide.Right, RadarZone.Alert), (f.Cars[0].Side, f.Cars[0].Zone));
        Assert.Equal(3.2, f.Cars[0].Right, 3); Assert.Equal(-1, f.Cars[0].Forward, 3);
    }

    [Fact]
    public void Yaw_pi_means_driving_toward_plus_z_and_right_is_plus_x()
    {
        var sim = new Sim(Len, (1000, 60), (1010, 60), (1000, 60)) { PlayerIndex = 0 };
        sim.Poses[0] = (0, 0, 0, Math.PI);
        sim.Poses[1] = (0, 0, 10, Math.PI);         // 10 m para +z = a frente
        sim.Poses[2] = (3, 0, 0, Math.PI);          // 3 m para +x = a direita
        var f = Run(sim);
        var ahead = f.Cars.ToArray().Single(c => c.Index == 1); var side = f.Cars.ToArray().Single(c => c.Index == 2);
        Assert.Equal(10, ahead.Forward, 3); Assert.Equal(0, ahead.Right, 3);
        Assert.Equal(3, side.Right, 3); Assert.Equal(0, side.Forward, 3);
    }

    [Fact]
    public void Cars_in_the_pit_lane_are_ignored_unless_the_player_is_also_there()
    {
        var sim = Scene(0.3, (4, 3.5), (-5, -3.5));
        sim.Pit[1] = PitState.InPit;
        var f = Run(sim);
        Assert.Equal(1, f.Count); Assert.Equal(2, f.Cars[0].Index);

        sim.Pit[0] = PitState.DrivingOutOfPits;      // o jogador tambem esta nos boxes: o carro 1 volta a contar, o 2 (na pista) sai
        sim.Pit[1] = PitState.DrivingIntoPits;
        f = Run(sim);
        Assert.Equal(1, f.Count); Assert.Equal(1, f.Cars[0].Index);

        sim.Pit[0] = PitState.None; sim.Pit[1] = PitState.InGarage;   // garagem: nunca
        f = Run(sim);
        Assert.Equal(2, f.Cars[0].Index);
    }

    [Fact]
    public void Range_is_configurable_and_the_lateral_window_applies()
    {
        var sim = Scene(1.0, (20, 0), (-18, 2), (3, 9));
        var def = Run(sim);                                              // alcance 15 m, lateral 7,5 m: ninguem
        Assert.Equal(0, def.Count); Assert.True(def.Valid); Assert.False(def.AnyNear);
        var wide = Run(sim, new RadarOptions { RangeMeters = 25 });
        Assert.Equal(2, wide.Count);                                     // o de 9 m de lateral continua fora
        Assert.Equal(25, wide.RangeMeters);
        var clamp = new RadarTracker { Options = new RadarOptions { RangeMeters = 500 } };
        Assert.Equal(RadarOptions.MaxRange, clamp.Options.RangeMeters);
        clamp.Options = new RadarOptions { RangeMeters = 1, Sensitivity = double.NaN };
        Assert.Equal((RadarOptions.MinRange, 1.0), (clamp.Options.RangeMeters, clamp.Options.Sensitivity));
    }

    [Fact]
    public void Sensitivity_scales_the_alert_window()
    {
        var sim = Scene(0.0, (9, 3.5));
        Assert.Equal(RadarZone.Warn, Run(sim).Cars[0].Zone);                                              // 9 m: fora dos 7 m
        Assert.Equal(RadarZone.Alert, Run(sim, new RadarOptions { Sensitivity = 1.5 }).Cars[0].Zone);     // 7 x 1,5 = 10,5 m
        Assert.Equal(RadarZone.Far, Run(Scene(0.0, (13, 3.5)), new RadarOptions { Sensitivity = 0.5 }).Cars[0].Zone);   // 12 x 0,5 = 6 m
    }

    [Fact]
    public void Parallel_track_sections_and_bridges_are_not_neighbours()
    {
        // 3 m ao lado no mapa, mas 2,5 km de distancia na volta (pista paralela).
        var sim = new Sim(Len, (1000, 60), (3500, 60), (1005, 60), (1000, 60)) { PlayerIndex = 0 };
        sim.Poses[0] = (0, 0, 0, Math.PI);
        sim.Poses[1] = (3, 0, 0, Math.PI);
        sim.Poses[2] = (3, 6, 2, Math.PI);          // viaduto: 6 m acima
        sim.Poses[3] = (-3, 0.5, 0, Math.PI);       // este sim e vizinho (mesma altura, mesma volta)
        var f = Run(sim);
        Assert.Equal([3], f.Cars.ToArray().Select(c => c.Index).ToArray());
    }

    [Fact]
    public void No_data_outside_a_session_without_a_pose_or_when_alone()
    {
        var sim = Scene(0.4, (4, 3));
        var snap = sim.Snapshot();
        var t = new RadarTracker();
        Assert.False(t.Update(snap with { InSession = false }, 0).Valid);
        Assert.False(RadarFrame.Empty.Valid);
        Assert.False(t.Update(snap with { Player = null }, 0).Valid);

        sim.Poses.Remove(0);                         // sem pose do jogador
        Assert.False(Run(sim).Valid);

        var alone = new Sim(Len, (1000, 60)) { PlayerIndex = 0 };
        alone.Poses[0] = (1, 2, 3, 0.5);
        Assert.False(Run(alone).Valid);              // sozinho na pista: nada a mostrar

        var garage = new Sim(Len, (1000, 60), (1000, 0)) { PlayerIndex = 0 };
        garage.Poses[0] = (1, 2, 3, 0.5); garage.Poses[1] = (3, 2, 3, 0.5); garage.Pit[1] = PitState.InGarage;
        Assert.False(Run(garage).Valid);             // os outros estao na garagem
    }

    [Fact]
    public void Closest_cars_first_and_capped_to_the_frame_capacity()
    {
        var others = Enumerable.Range(0, 24).Select(i => (Fwd: -14.0 + i * 1.2, Right: i % 2 == 0 ? 3.0 : -3.0)).ToArray();
        var f = Run(Scene(0.7, others));
        Assert.Equal(RadarFrame.Capacity, f.Count);
        var abs = f.Cars.ToArray().Select(c => Math.Abs(c.Forward)).ToArray();
        Assert.Equal(abs.Order().ToArray(), abs);
        Assert.True(abs[^1] < 10);                   // os 16 mais proximos de 24 carros espalhados em 27,6 m
        var few = Run(Scene(0.7, others), new RadarOptions { MaxCars = 3 });
        Assert.Equal(3, few.Count);
    }

    [Fact]
    public void Update_allocates_nothing_and_published_frames_stay_immutable_for_the_ring()
    {
        var snap = Scene(1.1, (3, -3.2), (-6, 3.4), (11, 0.2)).Snapshot();
        var t = new RadarTracker();
        for (int i = 0; i < 20; i++) t.Update(snap, i);        // aquecimento (JIT)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++) t.Update(snap, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        var held = t.Update(snap, 100);
        var copy = held.Cars.ToArray();
        for (int i = 0; i < RadarTracker.RingSize - 1; i++) t.Update(snap, 101 + i);
        Assert.Equal(100, held.Time);
        Assert.Equal(copy, held.Cars.ToArray());
        Assert.Same(t.Current, t.Current);
        Assert.Equal(103, t.Current.Time);
    }

    // ---- Dump real do AMS2 ----

    static RawSharedMemory LoadDump()
        => MemoryMarshal.Read<RawSharedMemory>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "ams2-v14-interlagos.bin")));

    [Fact]
    public void Real_dump_yaw_plus_pi_is_the_direction_of_travel()
    {
        // Carros vizinhos na reta de Interlagos (20 a 40 m de distancia na volta): o vetor ate o proximo carro deve ter rumo = yaw + pi.
        var raw = LoadDump();
        var s = SnapshotMapper.Map(in raw);
        var byLap = s.Cars.Where(c => c.LapDistance > 1000 && c.LapDistance < 1300).OrderBy(c => c.LapDistance).ToList();
        Assert.True(byLap.Count >= 6);
        for (int i = 0; i + 1 < byLap.Count; i++)
        {
            var a = byLap[i]; var b = byLap[i + 1];
            double heading = Math.Atan2(b.PosX - a.PosX, b.PosZ - a.PosZ);
            double predicted = a.Yaw + Math.PI;
            Assert.True(Math.Abs(Math.IEEERemainder(heading - predicted, 2 * Math.PI)) < 0.12, $"carro {a.Index}: rumo {heading:F3} x yaw+pi {predicted:F3}");
        }
    }

    [Fact]
    public void Real_dump_car_27_m_ahead_is_seen_ahead_and_in_the_same_line()
    {
        var raw = LoadDump();
        raw.GameState = 2; raw.ViewedParticipantIndex = 6;                   // lapDist 1112,7; o carro 17 esta em 1139,5 (27 m a frente) e o 13 em 1172 (59 m)
        var s = SnapshotMapper.Map(in raw);
        var f = new RadarTracker { Options = new RadarOptions { RangeMeters = 40 } }.Update(s, 0);
        Assert.True(f.Valid);
        var c = f.Cars.ToArray().Single(x => x.Index == 17);
        Assert.InRange(c.Forward, 24, 30);
        Assert.InRange(Math.Abs(c.Right), 0, 3.5);
        Assert.Equal(RadarZone.Far, c.Zone);              // 27 m: fora das janelas de aviso
    }
}
