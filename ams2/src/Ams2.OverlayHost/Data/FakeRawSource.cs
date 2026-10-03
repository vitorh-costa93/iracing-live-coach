using System.Runtime.InteropServices;
using System.Text;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.OverlayHost.Data;

/// <summary>
/// Escritor falso em processo (modo --fake): simula uma corrida de 8 carros numa pista de 7004 m, sem o jogo
/// e sem tocar no mapa real $pcars2$. O estado é função do relógio, então é determinístico para o --png.
/// </summary>
public sealed class FakeRawSource(Func<double> clock, bool? board = null) : IRawMemorySource
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

    // Auxilios de teste visual (so --fake): AMS2_FAKE_PITS=1 faz alguns carros pararem nos boxes (jogador entra em t=16 s, parado ~3,4 s);
    // AMS2_FAKE_FINISH=1 encerra a corrida de 1 volta do lider em t=31 s.
    static readonly bool Pits = Environment.GetEnvironmentVariable("AMS2_FAKE_PITS") == "1";
    static readonly bool Finish = Environment.GetEnvironmentVariable("AMS2_FAKE_FINISH") == "1";
    static uint FakePit(int i, double t)
    {
        if (!Pits || i is 4 or 6 or 7) return 0;
        double s = t - (i == PlayerIndex ? 16 : 8 + i * 2.5);
        if (s < 0) return 0;
        if (s < 1) return 1;       // DrivingIntoPits
        if (s < 4.4) return 2;     // InPit
        return s < 5.4 ? 3u : 0u;  // DrivingOutOfPits
    }

    // AMS2_FAKE_BOARD=1 (so --fake): corrida rapida de 20 carros para o widget rotativo inferior (board): pista de 1400 m,
    // volta de ~20 s, lider cruza a linha em t~1,5 s e depois a cada ~20 s; P20 uma volta atras ("+1L"); o carro 12 para
    // no box de t=62 a 65 s (InPit, nao conta para a torre nem como vizinho); jogador = indice 5. Setores em tercos da volta.
    public const double BoardTrackLength = 1400;
    const double BoardSpeed = 70;
    public const int BoardPlayerIndex = 5;
    static readonly (string Name, string Car)[] BoardField =
    [
        ("Michael Schumacher", "Formula Classic Gen2 (B)"), ("Fernando Alonso", "Formula Classic Gen2 (M)"),
        ("Kimi Raikkonen", "Formula Classic Gen2 (M)"), ("Giancarlo Fisichella", "Formula Classic Gen2 (M)"),
        ("Jenson Button", "Formula Classic Gen2 (M)"), ("Player", "Formula Classic Gen2"),
        ("Rubens Barrichello", "Formula Classic Gen2 (M)"), ("Felipe Massa", "Formula Classic Gen2 (B)"),
        ("Juan Pablo Montoya", "Formula Classic Gen2 (M)"), ("Jarno Trulli", "Formula Classic Gen2 (M)"),
        ("Ralf Schumacher", "Formula Classic Gen2 (M)"), ("Mark Webber", "Formula Classic Gen2 (M)"),
        ("Nick Heidfeld", "Formula Classic Gen2 (M)"), ("Jacques Villeneuve", "Formula Classic Gen2 (M)"),
        ("David Coulthard", "Formula Classic Gen2 (M)"), ("Christian Klien", "Formula Classic Gen2 (M)"),
        ("Takuma Sato", "Formula Classic Gen2 (B)"), ("Vitantonio Liuzzi", "Formula Classic Gen2 (M)"),
        ("Tiago Monteiro", "Formula Classic Gen2 (B)"), ("Christijan Albers", "Formula Classic Gen2 (B)"),
    ];
    // Atraso de cada carro para o lider (s) no inicio (pelotao de 2 s para sobrar intervalo livre na volta de 20 s); o ultimo leva +1 volta.
    static readonly double[] BoardGaps = [0, 0.239, 0.33, 0.45, 0.58, 0.66, 0.79, 0.88, 0.97, 1.08, 1.17, 1.29, 1.38, 1.47, 1.55, 1.63, 1.74, 1.83, 1.92, 2.0];
    static readonly bool BoardEnv = Environment.GetEnvironmentVariable("AMS2_FAKE_BOARD") == "1";
    readonly bool _board = board ?? BoardEnv;

    uint _seq;

    public bool TryRead(out RawSharedMemory raw)
    {
        double t = clock();
        raw = default;
        raw.Version = Const.ExpectedVersion;
        raw.GameState = 2; raw.SessionState = 5; raw.RaceState = 2;
        raw.ViewedParticipantIndex = _board ? BoardPlayerIndex : PlayerIndex;
        raw.NumParticipants = _board ? BoardField.Length : Field.Length;
        raw.TrackLength = (float)(_board ? BoardTrackLength : TrackLength);
        raw.LapsInEvent = Finish ? 1u : _board ? 20u : 44u;
        raw.NumSectors = 3;
        raw.EventTimeRemaining = -1;
        Put(raw.TrackLocation, "Spa-Francorchamps");
        Put(raw.CarName, "Formula Classic Gen2");
        Put(raw.CarClassName, "F1");

        if (_board) FillBoardField(ref raw, t);
        else for (int i = 0; i < Field.Length; i++)
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
            raw.Speeds[i] = (float)speed;
            raw.RaceStates[i] = (uint)(Finish && i == 0 && t >= 31 ? 3 : 2);
            raw.PitModes[i] = FakePit(i, t);
            raw.FastestLapTimes[i] = 103.972f + i * 0.31f;
            raw.LastLapTimes[i] = 104.5f + i * 0.2f;
        }

        raw.FuelCapacity = 95;
        raw.FuelLevel = (float)Math.Max(0.05, 0.34 - t * 0.0004);
        raw.Speed = (float)Speed; raw.Rpm = 12400; raw.MaxRpm = 18000; raw.Gear = 4; raw.NumGears = 7;
        // Auxilio de teste visual: AMS2_FAKE_GEAR (-1 = R, 0 = N) e AMS2_FAKE_KPH sobrescrevem marcha/velocidade do jogador.
        if (int.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_GEAR"), out int fg)) raw.Gear = fg;
        if (double.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_KPH"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fk)) raw.Speed = (float)(fk / 3.6);
        if (double.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_RPM"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fr)) raw.Rpm = (float)fr;
        if (double.TryParse(Environment.GetEnvironmentVariable("AMS2_FAKE_MAXRPM"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fm)) raw.MaxRpm = (float)fm;
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

    /// <summary>Campo do modo board: velocidade constante por carro (cada carro perde ~0,004 s por volta para o da frente),
    /// posicoes pela distancia total, setores em tercos, LastLapTime = tempo exato da volta + variacao deterministica.</summary>
    static void FillBoardField(ref RawSharedMemory raw, double t)
    {
        int n = BoardField.Length;
        Span<double> total = stackalloc double[n];
        for (int i = 0; i < n; i++)
        {
            double v = BoardSpeed * (1 - 0.0002 * i);
            double te = i == 12 ? t - Math.Clamp(t - 62, 0, 3) : t;   // carro 12 parado 3 s no box
            total[i] = 2 * BoardTrackLength - 1.5 * BoardSpeed - BoardGaps[i] * BoardSpeed + v * te - (i == n - 1 ? BoardTrackLength : 0);
        }
        for (int i = 0; i < n; i++)
        {
            int pos = 1;
            for (int j = 0; j < n; j++) if (total[j] > total[i] || total[j] == total[i] && j < i) pos++;
            double v = BoardSpeed * (1 - 0.0002 * i);
            int laps = (int)Math.Floor(total[i] / BoardTrackLength);
            double d = total[i] - laps * BoardTrackLength;
            uint pit = i != 12 ? 0u : t is >= 61 and < 62 ? 1u : t is >= 62 and < 65 ? 2u : t is >= 65 and < 66 ? 3u : 0u;
            ref var p = ref raw.Participants[i];
            p.IsActive = 1;
            p.RacePosition = (uint)pos;
            p.LapsCompleted = (uint)laps;
            p.CurrentLap = (uint)laps + 1;
            p.CurrentLapDistance = (float)d;
            p.CurrentSector = Math.Min(2, (int)(d / BoardTrackLength * 3));
            Put(MemoryMarshal.CreateSpan(ref p.Name[0], 64), BoardField[i].Name);
            Put(MemoryMarshal.CreateSpan(ref raw.CarNames[i * 64], 64), BoardField[i].Car);
            Put(MemoryMarshal.CreateSpan(ref raw.CarClassNames[i * 64], 64), "F1");
            raw.Speeds[i] = pit == 2 ? 0f : (float)v;
            raw.RaceStates[i] = 2;
            raw.PitModes[i] = pit;
            double lapTime = BoardTrackLength / v + 0.12 * Math.Sin(laps * 1.7 + i);
            raw.LastLapTimes[i] = laps >= 1 ? (float)lapTime : -1f;
            raw.FastestLapTimes[i] = (float)(BoardTrackLength / v - 0.12);
        }
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
