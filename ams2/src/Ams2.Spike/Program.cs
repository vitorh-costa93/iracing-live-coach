using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Ams2.Core.Raw;

// Uso:
//   Ams2.Spike layout   -> imprime tamanho e offsets da estrutura (conferir com o header)
//   Ams2.Spike fake     -> cria o mapa $pcars2$ com dados simulados (sem o jogo)
//   Ams2.Spike [read]   -> lê $pcars2$ e imprime pilotos/velocidade/combustível (~4 Hz)
var mode = args.Length > 0 ? args[0] : "read";
return mode switch
{
    "layout" => Layout(),
    "fake" => Fake(),
    _ => Read(args),
};

static string Text(ReadOnlySpan<byte> bytes)
{
    int n = bytes.IndexOf((byte)0);
    return Encoding.UTF8.GetString(n < 0 ? bytes : bytes[..n]);
}

static void Put(Span<byte> dest, string s)
{
    dest.Clear();
    Encoding.UTF8.GetBytes(s, dest[..Math.Min(s.Length, dest.Length - 1)]);
}

static int Layout()
{
    Console.WriteLine($"sizeof(RawSharedMemory) = {Marshal.SizeOf<RawSharedMemory>()}");
    Console.WriteLine($"sizeof(ParticipantInfo) = {Marshal.SizeOf<ParticipantInfo>()}");
    foreach (var f in typeof(RawSharedMemory).GetFields())
        Console.WriteLine($"  {f.Name,-26} @ {Marshal.OffsetOf<RawSharedMemory>(f.Name)}");
    return 0;
}

static int Fake()
{
    int size = Marshal.SizeOf<RawSharedMemory>();
    using var mmf = MemoryMappedFile.CreateOrOpen(Const.MapName, size);
    using var view = mmf.CreateViewAccessor(0, size);
    var names = new[] { "Player", "Hakkinen", "Schumacher", "Coulthard", "Villeneuve", "Irvine", "Frentzen", "Alesi" };
    float t = 0;
    uint seq = 0;
    Console.WriteLine("Escritor falso ativo (Ctrl+C para sair).");
    while (true)
    {
        var m = new RawSharedMemory
        {
            Version = Const.ExpectedVersion, GameState = 2, SessionState = 5, RaceState = 2,
            ViewedParticipantIndex = 0, NumParticipants = names.Length, TrackLength = 7004, LapsInEvent = 44, NumSectors = 3,
            FuelLevel = Math.Max(0, 0.8f - t * 0.001f), FuelCapacity = 95, Speed = 70 + 10 * MathF.Sin(t),
            Rpm = 12000, MaxRpm = 18000, Gear = 5, Throttle = 0.8f, AmbientTemperature = 22, TrackTemperature = 31,
        };
        Put(m.CarName, "Fake Car");
        Put(m.TrackLocation, "Spa");
        for (int i = 0; i < names.Length; i++)
        {
            ref var p = ref m.Participants[i];
            p.IsActive = 1; p.RacePosition = (uint)(i + 1); p.CurrentLap = 3; p.CurrentSector = 1;
            p.CurrentLapDistance = (3000 + 80 * t - i * 40) % 7004;
            Put(p.Name, names[i]);
        }
        seq += 2; m.SequenceNumber = seq; // par = memória estável
        view.Write(0, ref m);
        t += 0.1f;
        Thread.Sleep(100);
    }
}

static int Read(string[] args)
{
    int size = Marshal.SizeOf<RawSharedMemory>();
    Console.WriteLine($"Aguardando o mapa '{Const.MapName}' (tamanho esperado {size} bytes)...");
    MemoryMappedFile mmf;
    while (true)
    {
        try { mmf = MemoryMappedFile.OpenExisting(Const.MapName, MemoryMappedFileRights.Read); break; }
        catch (FileNotFoundException) { Thread.Sleep(1000); }
    }
    using var _ = mmf;
    using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    Console.WriteLine($"Mapa aberto: capacidade {view.Capacity} bytes (esperado >= {size}).");

    while (true)
    {
        RawSharedMemory m = default;
        bool ok = false;
        for (int attempt = 0; attempt < 5 && !ok; attempt++)
        {
            view.Read(0, out m);
            ok = m.SequenceNumber % 2 == 0; // ímpar = escrita em andamento (leitura rasgada)
            if (!ok) Thread.Sleep(1);
        }
        if (m.Version != Const.ExpectedVersion)
            Console.WriteLine($"AVISO: versão {m.Version}, esperado {Const.ExpectedVersion}. Offsets podem estar errados.");

        if (!Console.IsOutputRedirected) Console.Clear();
        Console.WriteLine($"v{m.Version} build {m.BuildVersionNumber} seq {m.SequenceNumber} game {m.GameState} sessão {m.SessionState} corrida {m.RaceState}");
        Console.WriteLine($"Pista: {Text(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in m.TrackLocation[0], 64)))} " +
                          $"({m.TrackLength:F0} m)  Carro: {Text(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in m.CarName[0], 64)))}");
        Console.WriteLine($"Vel {m.Speed * 3.6f:F0} km/h  Marcha {m.Gear}  RPM {m.Rpm:F0}/{m.MaxRpm:F0}  Acel {m.Throttle:P0} Freio {m.Brake:P0}");
        Console.WriteLine($"Combustível {m.FuelLevel * m.FuelCapacity:F1} L ({m.FuelLevel:P0} de {m.FuelCapacity:F0} L)");
        Console.WriteLine($"Ar {m.AmbientTemperature:F0}°C  Pista {m.TrackTemperature:F0}°C  Chuva {m.RainDensity:P0}");
        Console.WriteLine($"Pneus temp {m.TyreTemp[0]:F0}/{m.TyreTemp[1]:F0}/{m.TyreTemp[2]:F0}/{m.TyreTemp[3]:F0}  desgaste {m.TyreWear[0]:P0}/{m.TyreWear[1]:P0}/{m.TyreWear[2]:P0}/{m.TyreWear[3]:P0}");
        Console.WriteLine($"Pilotos ({m.NumParticipants}), visto: {m.ViewedParticipantIndex}");
        int n = Math.Clamp(m.NumParticipants, 0, Const.MaxParticipants);
        for (int i = 0; i < n; i++)
        {
            ref readonly var p = ref m.Participants[i];
            if (p.IsActive == 0) continue;
            Console.WriteLine($" P{p.RacePosition,2} {Text(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in p.Name[0], 64))),-24} volta {p.CurrentLap,2} dist {p.CurrentLapDistance,7:F0} m  setor {p.CurrentSector}");
        }
        if (args.Contains("--once")) return 0;
        Thread.Sleep(250);
    }
}
