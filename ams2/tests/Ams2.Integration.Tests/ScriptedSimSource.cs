using Ams2.Core.Raw;
using Ams2.Core.Reading;

namespace Ams2.Integration.Tests;

/// <summary>Decorador do escritor falso: o roteiro altera a memoria bruta (pit do jogador, bandeirada do lider...) no relogio simulado.</summary>
public sealed class ScriptedSource(IRawMemorySource inner, Func<double> clock, ScriptedSource.Edit script) : IRawMemorySource
{
    public delegate void Edit(double t, ref RawSharedMemory raw);

    public bool TryRead(out RawSharedMemory raw)
    {
        bool ok = inner.TryRead(out raw);
        if (ok) script(clock(), ref raw);
        return ok;
    }

    public void Dispose() => inner.Dispose();
}
