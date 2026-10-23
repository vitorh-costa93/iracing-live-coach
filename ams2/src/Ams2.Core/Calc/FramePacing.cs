namespace Ams2.Core.Calc;

/// <summary>Retangulo em pixels do device (direita/baixo exclusivos).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
    public bool Touches(PixelRect o) => Left <= o.Right && o.Left <= Right && Top <= o.Bottom && o.Top <= Bottom;
    public PixelRect Union(PixelRect o) => new(Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));
}

/// <summary>Ritmo por vblank com divisor: Present(N) com N = refresh/alvo (1..4, limite do DXGI).</summary>
public static class FramePacing
{
    /// <summary>Divisor do vblank para chegar perto de <paramref name="targetHz"/> (alvo &lt;= 0 = todo vblank).</summary>
    public static int Divisor(int refreshHz, double targetHz)
    {
        if (targetHz <= 0 || refreshHz <= 0) return 1;
        return (int)Math.Clamp(Math.Round(refreshHz / targetHz, MidpointRounding.AwayFromZero), 1, 4);
    }
}

/// <summary>
/// Regiao suja de um quadro para Present1: retangulos desenhados no quadro atual unidos aos do quadro anterior (o que sumiu tambem
/// precisa ser recomposto). Retangulos que se tocam viram um so; acima de <see cref="MaxRects"/>, um unico envelope.
/// </summary>
public sealed class DirtyRegion
{
    public const int MaxRects = 16;
    List<PixelRect> _cur = [], _prev = [];
    public int Width { get; set; } = int.MaxValue;
    public int Height { get; set; } = int.MaxValue;

    /// <summary>Comeca um quadro: o atual passa a ser o anterior.</summary>
    public void NewFrame()
    {
        (_prev, _cur) = (_cur, _prev);
        _cur.Clear();
    }

    /// <summary>Esquece o anterior (proxima apresentacao e cheia de qualquer forma).</summary>
    public void Reset() { _prev.Clear(); _cur.Clear(); }

    /// <summary>Marca um retangulo em px (float) com folga de <paramref name="pad"/> px; recortado ao device.</summary>
    public void Add(float left, float top, float right, float bottom, float pad = 2)
    {
        if (!(right > left) || !(bottom > top)) return;
        var r = new PixelRect(
            Math.Max(0, (int)MathF.Floor(left - pad)), Math.Max(0, (int)MathF.Floor(top - pad)),
            (int)Math.Min(Width, MathF.Ceiling(right + pad)), (int)Math.Min(Height, MathF.Ceiling(bottom + pad)));
        if (!r.IsEmpty) _cur.Add(r);
    }

    /// <summary>Uniao do quadro atual com o anterior, sem sobreposicao. Vazio = nada mudou de lugar (apresentar cheio).</summary>
    public void Collect(List<PixelRect> into)
    {
        into.Clear();
        foreach (var r in _prev) Insert(into, r);
        foreach (var r in _cur) Insert(into, r);
        if (into.Count <= MaxRects) return;
        var all = into[0];
        for (int i = 1; i < into.Count; i++) all = all.Union(into[i]);
        into.Clear(); into.Add(all);
    }

    static void Insert(List<PixelRect> list, PixelRect r)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].Touches(r)) continue;
            r = r.Union(list[i]);
            list.RemoveAt(i);
            i = -1;
        }
        list.Add(r);
    }
}
