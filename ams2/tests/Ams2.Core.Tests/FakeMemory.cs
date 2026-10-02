using System.Runtime.InteropServices;
using System.Text;
using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

/// <summary>Escritor falso: fonte de memória em processo, com helpers para montar a estrutura crua.</summary>
public sealed class FakeMemory : IRawMemorySource
{
    public RawSharedMemory Raw;
    public bool Connected = true;
    public Func<RawSharedMemory, RawSharedMemory>? OnRead; // permite simular escrita rasgada

    public FakeMemory() { Raw.Version = Const.ExpectedVersion; Raw.GameState = 2; Raw.SessionState = 5; Raw.TrackLength = 7000; }

    public bool TryRead(out RawSharedMemory raw)
    {
        raw = OnRead is null ? Raw : OnRead(Raw);
        return Connected;
    }
    public void Dispose() { }

    public static void Put(Span<byte> dest, string s)
    {
        dest.Clear();
        var b = Encoding.UTF8.GetBytes(s);
        b.AsSpan(0, Math.Min(b.Length, dest.Length - 1)).CopyTo(dest);
    }

    public void SetCar(int i, string name, string car, string cls, int pos, double dist, int lapsCompleted = 0, double speed = 50)
    {
        Raw.NumParticipants = Math.Max(Raw.NumParticipants, i + 1);
        ref var p = ref Raw.Participants[i];
        p.IsActive = 1; p.RacePosition = (uint)pos; p.CurrentLapDistance = (float)dist; p.LapsCompleted = (uint)lapsCompleted;
        p.CurrentLap = (uint)lapsCompleted + 1;
        Put(MemoryMarshal.CreateSpan(ref p.Name[0], 64), name);
        Put(MemoryMarshal.CreateSpan(ref Raw.CarNames[i * 64], 64), car);
        Put(MemoryMarshal.CreateSpan(ref Raw.CarClassNames[i * 64], 64), cls);
        Raw.Speeds[i] = (float)speed;
        Raw.RaceStates[i] = 2;
    }
}
