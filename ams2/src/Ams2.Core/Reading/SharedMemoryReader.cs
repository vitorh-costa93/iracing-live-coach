using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Ams2.Core.Raw;

namespace Ams2.Core.Reading;

/// <summary>Fonte de bytes da estrutura crua. Permite trocar o mapa real por um escritor falso nos testes.</summary>
public interface IRawMemorySource : IDisposable
{
    /// <summary>false se o mapa não existe (jogo fechado).</summary>
    bool TryRead(out RawSharedMemory raw);
}

public sealed class MemoryMappedSource(string mapName = Const.MapName) : IRawMemorySource
{
    MemoryMappedFile? _mmf;
    MemoryMappedViewAccessor? _view;

    public bool TryRead(out RawSharedMemory raw)
    {
        raw = default;
        try
        {
            if (_view is null)
            {
                _mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
                _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                if (_view.Capacity < Marshal.SizeOf<RawSharedMemory>()) { Close(); return false; }
            }
            _view.Read(0, out raw);
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
