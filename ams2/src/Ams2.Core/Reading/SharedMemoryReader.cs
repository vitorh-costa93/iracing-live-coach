using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Ams2.Core.Raw;

namespace Ams2.Core.Reading;

/// <summary>Fonte de bytes da estrutura crua. Permite trocar o mapa real por um escritor falso nos testes.</summary>
public interface IRawMemorySource : IDisposable
{
    /// <summary>false se o mapa não existe (jogo fechado).</summary>
    bool TryRead(out RawSharedMemory raw);

    /// <summary>
    /// Leitura mínima para o amostrador de alta taxa: contador de sequência + acelerador/freio/volante, só de um estado estável
    /// (seq par e igual antes/depois). false = sem mapa ou leitura rasgada. O padrão lê a estrutura inteira.
    /// </summary>
    bool TryReadInputs(out RawInputs inputs)
    {
        if (TryRead(out var raw) && raw.SequenceNumber % 2 == 0)
        {
            inputs = new RawInputs(raw.SequenceNumber, raw.Throttle, raw.Brake, raw.Steering);
            return true;
        }
        inputs = default;
        return false;
    }
}

public readonly record struct RawInputs(uint Seq, float Throttle, float Brake, float Steering);

public sealed class MemoryMappedSource(string mapName = Const.MapName) : IRawMemorySource
{
    MemoryMappedFile? _mmf;
    MemoryMappedViewAccessor? _view;

    static readonly int SeqOffset = (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.SequenceNumber));
    static readonly int ThrottleOffset = (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.Throttle));
    static readonly int BrakeOffset = (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.Brake));
    static readonly int SteeringOffset = (int)Marshal.OffsetOf<RawSharedMemory>(nameof(RawSharedMemory.Steering));

    bool Open()
    {
        if (_view is not null) return true;
        _mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
        _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        if (_view.Capacity < Marshal.SizeOf<RawSharedMemory>()) { Close(); return false; }
        return true;
    }

    public bool TryRead(out RawSharedMemory raw)
    {
        raw = default;
        try
        {
            if (!Open()) return false;
            _view!.Read(0, out raw);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            Close();
            return false;
        }
    }

    /// <summary>Leitura mínima (4 campos) sob protocolo seqlock. Uma instância não é thread-safe: o amostrador usa a sua.</summary>
    public bool TryReadInputs(out RawInputs inputs)
    {
        inputs = default;
        try
        {
            if (!Open()) return false;
            uint s1 = _view!.ReadUInt32(SeqOffset);
            if ((s1 & 1) != 0) return false;
            float thr = _view.ReadSingle(ThrottleOffset), brk = _view.ReadSingle(BrakeOffset), str = _view.ReadSingle(SteeringOffset);
            if (_view.ReadUInt32(SeqOffset) != s1) return false;
            inputs = new RawInputs(s1, thr, brk, str);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            Close();
            return false;
        }
    }

    void Close() { _view?.Dispose(); _mmf?.Dispose(); _view = null; _mmf = null; }
    public void Dispose() => Close();
}

public enum ReadStatus { Disconnected, Torn, VersionMismatch, Ok }

public readonly record struct ReadResult(ReadStatus Status, SessionSnapshot? Snapshot, uint Version);

/// <summary>Lê o mapa com contador de sequência (ímpar = escrita em andamento) e valida a versão da estrutura.</summary>
public sealed class SharedMemoryReader(IRawMemorySource source, int maxTornRetries = 5) : IDisposable
{
    public ReadResult Poll()
    {
        RawSharedMemory raw = default;
        for (int attempt = 0; attempt <= maxTornRetries; attempt++)
        {
            if (!source.TryRead(out raw)) return new(ReadStatus.Disconnected, null, 0);
            if (raw.SequenceNumber % 2 == 0) break;
            if (attempt == maxTornRetries) return new(ReadStatus.Torn, null, raw.Version);
            Thread.SpinWait(2000);
        }
        if (raw.Version < Const.ExpectedVersion || raw.Version > Const.MaxSupportedVersion) return new(ReadStatus.VersionMismatch, null, raw.Version);
        return new(ReadStatus.Ok, SnapshotMapper.Map(in raw), raw.Version);
    }

    public void Dispose() => source.Dispose();
}
