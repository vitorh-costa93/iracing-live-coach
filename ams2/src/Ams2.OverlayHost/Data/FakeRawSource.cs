using System.Runtime.InteropServices;
using System.Text;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.OverlayHost.Data;

/// <summary>
/// Escritor falso em processo (modo --fake): simula uma corrida de 8 carros numa pista de 7004 m, sem o jogo
/// e sem tocar no mapa real $pcars2$. O estado é função do relógio, então é determinístico para o --png.
/// </summary>
public sealed class FakeRawSource(Func<double> clock) : IRawMemorySource
{
    public const double TrackLength = 7004;
    const double Speed = 60; // m/s

    // (nome, gap em segundos em relação ao jogador: + à frente, - atrás)
    static readonly (string Name, double Gap)[] Field =
    [
        ("Michael Schumacher", 2.976), ("David Coulthard", 1.342), ("Mika Hakkinen", 0.842), ("Player", 0),
        ("Rubens Barrichello", -1.102), ("Fernando Alonso", -2.481), ("Jarno Trulli", -3.947), ("Ralf Schumacher", -10.337),
    ];
    public const int PlayerIndex = 3;
    // IDs ficticios (o AMS2 real manda 0): registrados so para o modo --fake mostrar bandeiras.
    static readonly string[] FakeIso = ["de", "gb", "fi", "br", "br", "es", "it", "de"];
    static FakeRawSource() { for (int i = 0; i < FakeIso.Length; i++) Ams2.Core.Reading.Nationalities.RegisterId((uint)(900 + i), FakeIso[i]); }

    uint _seq;

    public bool TryRead(out RawSharedMemory raw)
    {
        double t = clock();
        raw = default;
        raw.Version = Const.ExpectedVersion;
        raw.GameState = 2; raw.SessionState = 5; raw.RaceState = 2;
        raw.ViewedParticipantIndex = PlayerIndex;
        raw.NumParticipants = Field.Length;
        raw.TrackLength = (float)TrackLength;
        raw.LapsInEvent = 44;
        raw.NumSectors = 3;
        raw.EventTimeRemaining = -1;
        Put(raw.TrackLocation, "Spa-Francorchamps");
        Put(raw.CarName, "Formula Classic Gen2");
        Put(raw.CarClassName, "F1");

        for (int i = 0; i < Field.Length; i++)
        {
            double wobble = 0.4 / 0.3;
            double total = 5000 + Field[i].Gap * Speed + Speed * t + wobble * (Math.Cos(i) - Math.Cos(0.3 * t + i));
            double speed = Speed + 0.4 * Math.Sin(0.3 * t + i);
            int laps = (int)Math.Floor(total / TrackLength);
            ref var p = ref raw.Participants[i];
            p.IsActive = 1;
            p.RacePosition = (uint)(i + 1);
            p.LapsCompleted = (uint)laps;
            p.CurrentLap = (uint)laps + 1;
            p.CurrentLapDistance = (float)(total - laps * TrackLength);
            p.CurrentSector = (int)((total - laps * TrackLength) / TrackLength * 3);
            Put(MemoryMarshal.CreateSpan(ref p.Name[0], 64), Field[i].Name);
            Put(MemoryMarshal.CreateSpan(ref raw.CarNames[i * 64], 64), i == PlayerIndex ? "Formula Classic Gen2" : "Formula Classic Gen2 (" + (i % 3 == 0 ? "B" : "M") + ")");
            Put(MemoryMarshal.CreateSpan(ref raw.CarClassNames[i * 64], 64), "F1");
            raw.Nationalities[i] = (uint)(900 + i);
            raw.Speeds[i] = (float)speed;
            raw.RaceStates[i] = 2;
            raw.FastestLapTimes[i] = 103.972f + i * 0.31f;
            raw.LastLapTimes[i] = 104.5f + i * 0.2f;
        }

        raw.FuelCapacity = 95;
        raw.FuelLevel = (float)Math.Max(0.05, 0.34 - t * 0.0004);
        raw.Speed = (float)Speed; raw.Rpm = 12400; raw.MaxRpm = 18000; raw.Gear = 4; raw.NumGears = 7;
        // Auxilio de teste visual: AMS2_FAKE_GEAR (-1 = R, 0 = N) e AMS2_FAKE_KPH sobrescrevem marcha/velocidade do jogador.
        if (int.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_GEAR"), out int fg)) raw.Gear = fg;
        if (double.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_KPH"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fk)) raw.Speed = (float)(fk / 3.6);
        double cyc = (t % 10) / 10 * Math.PI * 2;
        raw.Throttle = (float)Math.Clamp(0.5 + 0.9 * Math.Sin(cyc * 2), 0, 1);
        raw.Brake = (float)Math.Clamp(-0.2 - 1.2 * Math.Sin(cyc * 2 + 0.6), 0, 1);
        raw.Steering = (float)(0.55 * Math.Sin(cyc * 3.1) * Math.Cos(cyc * 0.8));
        raw.AmbientTemperature = 22; raw.TrackTemperature = 27;
        for (int w = 0; w < 4; w++) { raw.TyreTemp[w] = 92 + (w >> 1) * 2; raw.TyreWear[w] = 0.12f + (w >> 1) * 0.02f; }
        Put(MemoryMarshal.CreateSpan(ref raw.TyreCompound[0], 40), "Soft");

        _seq += 2; // par = memória estável
        raw.SequenceNumber = _seq;
        return true;
    }

    static void Put<T>(in T buf, string s) where T : struct => Put(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref System.Runtime.CompilerServices.Unsafe.AsRef(in buf), 1)), s);

    static void Put(Span<byte> dest, string s)
    {
        dest.Clear();
        var b = Encoding.UTF8.GetBytes(s);
        b.AsSpan(0, Math.Min(b.Length, dest.Length - 1)).CopyTo(dest);
    }

    public void Dispose() { }
}
