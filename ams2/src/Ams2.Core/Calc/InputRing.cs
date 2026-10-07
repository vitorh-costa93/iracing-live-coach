namespace Ams2.Core.Calc;

/// <summary>Amostra dos pedais para o gráfico (T = relógio do provider, em segundos).</summary>
public readonly record struct InputSample(double T, float Throttle, float Brake, float Steering,
    float SpeedMps = float.NaN, float Rpm = float.NaN, float MaxRpm = float.NaN, int Gear = int.MinValue);

/// <summary>
/// Histórico das entradas: buffer circular de capacidade fixa (potência de 2), um escritor (thread do amostrador) e leitores
/// concorrentes (threads de render). Sem alocação por amostra nem por quadro: o leitor copia a janela pedida para um vetor seu.
/// O escritor grava o slot e só depois publica o contador; o leitor confere o contador após copiar e descarta o que pode
/// ter sido sobrescrito (margem de 2 slots), então nunca devolve uma amostra rasgada.
/// </summary>
public sealed class InputRing
{
    readonly InputSample[] _slots;
    readonly int _mask;
    readonly Func<double> _clock;
    long _head;   // total de amostras publicadas
    long _floor;  // amostras anteriores a este índice foram descartadas por Clear

    public InputRing(Func<double> clock, int capacity = 4096)
    {
        if (capacity < 16 || (capacity & (capacity - 1)) != 0) throw new ArgumentException("capacidade deve ser potencia de 2 >= 16", nameof(capacity));
        _slots = new InputSample[capacity];
        _mask = capacity - 1;
        _clock = clock;
    }

    public int Capacity => _slots.Length;
    /// <summary>Máximo de amostras que uma cópia pode devolver (o vetor de destino deve ter este tamanho).</summary>
    public int Usable => _slots.Length - 2;
    /// <summary>Total de amostras já gravadas.</summary>
    public long Count => Volatile.Read(ref _head);
    /// <summary>Instante atual na mesma base de tempo das amostras (o render usa este, não o do último passo do provider).</summary>
    public double Now => _clock();

    /// <summary>Ultima amostra publicada, sem alocar nem copiar todo o historico.</summary>
    public bool TryLatest(out InputSample sample)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            long h = Volatile.Read(ref _head), floor = Volatile.Read(ref _floor);
            if (h <= floor) break;
            var candidate = _slots[(int)((h - 1) & _mask)];
            long after = Volatile.Read(ref _head);
            if (h - 1 >= Math.Max(Volatile.Read(ref _floor), after - Usable))
            { sample = candidate; return true; }
        }
        sample = default; return false;
    }

    /// <summary>Só o escritor. Amostras devem chegar com T crescente.</summary>
    public void Add(in InputSample s)
    {
        long h = _head;
        _slots[(int)(h & _mask)] = s;
        Volatile.Write(ref _head, h + 1);
    }

    /// <summary>Último T gravado (-inf se vazio). Pensado para o escritor.</summary>
    public double LastT
    {
        get
        {
            long h = Volatile.Read(ref _head);
            return h > Volatile.Read(ref _floor) ? _slots[(int)((h - 1) & _mask)].T : double.NegativeInfinity;
        }
    }

    /// <summary>Só o escritor: esquece o histórico (relógio voltou, novo evento).</summary>
    public void Clear() => Volatile.Write(ref _floor, Volatile.Read(ref _head));

    /// <summary>
    /// Copia, em ordem de tempo, as amostras com T &gt;= <paramref name="fromT"/> para <paramref name="dest"/>.
    /// Devolve quantas copiou. Seguro com o escritor rodando.
    /// </summary>
    public int CopyFrom(double fromT, InputSample[] dest)
    {
        long h = Volatile.Read(ref _head);
        long lo = Math.Max(Volatile.Read(ref _floor), h - Usable);
        // busca binária do primeiro índice com T >= fromT em [lo, h)
        long a = lo, b = h;
        while (a < b)
        {
            long m = a + ((b - a) >> 1);
            if (_slots[(int)(m & _mask)].T < fromT) a = m + 1; else b = m;
        }
        long start = a;
        int n = (int)Math.Min(h - start, dest.Length);
        for (int i = 0; i < n; i++) dest[i] = _slots[(int)((start + i) & _mask)];
        // o escritor pode ter avançado durante a cópia: descarta o prefixo possivelmente sobrescrito
        long h2 = Volatile.Read(ref _head);
        long bad = Math.Max(0, Math.Max(h2 - Usable, Volatile.Read(ref _floor)) - start);
        if (bad > 0)
        {
            if (bad >= n) return 0;
            Array.Copy(dest, (int)bad, dest, 0, n - (int)bad);
            n -= (int)bad;
        }
        return n;
    }
}
