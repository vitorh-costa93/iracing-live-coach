namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>Parada de gradiente vertical: posicao 0..1 de cima para baixo e cor ARGB.</summary>
public readonly record struct GpxStop(float Position, uint Argb);

/// <summary>
/// Texturas PROCEDURAIS das caixas do GPxSAT: gradientes verticais que reproduzem as cores dos botoes do GPxPatch (2 px de largura,
/// esticados no retangulo). Os TGAs de terceiros nao sao lidos nem copiados; so as cores (amostradas uma vez) viram paradas.
/// Modos novos registram as suas com <see cref="Define"/> (p.ex. "classic_blue" translucido).
/// </summary>
public static class GpxTextures
{
    public const string White = "white", Blue = "blue", Black = "black", Yellow = "yellow", YellowFlag = "yellow_flag",
        Green = "green", Red = "red", Positions = "positions", Leader = "leader";

    static readonly object Gate = new();
    static readonly Dictionary<string, GpxStop[]> All = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Botao padrao de <paramref name="rows"/> linhas: 2 linhas de brilho no topo, corpo e 3 de sombra embaixo.</summary>
    public static GpxStop[] Button(uint top0, uint top1, uint body, uint bot0, uint bot1, uint bot2, int rows = 32)
    {
        float r = rows;
        return
        [
            new(0.5f / r, top0), new(1.5f / r, top1), new(2.5f / r, body), new((rows - 3.5f) / r, body),
            new((rows - 2.5f) / r, bot0), new((rows - 1.5f) / r, bot1), new((rows - 0.5f) / r, bot2),
        ];
    }

    static GpxTextures()
    {
        All[White] = Button(0xffdbdce9, 0xfff0f1fc, 0xfffdfdfc, 0xffd2d4d9, 0xffa4a6af, 0xff898c9b);
        All[Blue] = Button(0xff878290, 0xff70697c, 0xff574f66, 0xff524b61, 0xff333043, 0xff1c1b2b);
        All[Green] = Button(0xff01b701, 0xff029a01, 0xff007f00, 0xff006c01, 0xff015501, 0xff014401);
        All[Red] = Button(0xfff43631, 0xffd2211d, 0xffc61f1c, 0xffc11f1c, 0xff971817, 0xff691412);
        All[Yellow] = Button(0xfffbc85e, 0xfffcb82c, 0xfffbaf12, 0xffe9a419, 0xffc99125, 0xffa87e32);
        All[YellowFlag] = Button(0xfff7f676, 0xfff6f44c, 0xfff5f436, 0xffe6e33b, 0xffc9c643, 0xffaca94c);
        All[Positions] = Button(0xff878290, 0xff5f576d, 0xff574f66, 0xff514a5f, 0xff333043, 0xff1c1b2b, rows: 64);
        All[Leader] = Button(0xfff43631, 0xffd2211d, 0xffc61f1c, 0xffc11f1c, 0xff971817, 0xff691412, rows: 64);
        All[Black] = [new(0.5f / 32, 0xff3a383b), new(1.5f / 32, 0xff3a383b), new(2.5f / 32, 0xff000000), new(31.5f / 32, 0xff000000)];
    }

    /// <summary>Registra (ou troca) uma textura.</summary>
    public static void Define(string name, GpxStop[] stops)
    {
        if (stops.Length == 0) throw new ArgumentException("Textura sem paradas.", nameof(stops));
        lock (Gate) All[name] = stops;
    }

    /// <summary>Paradas da textura; nome desconhecido = branco.</summary>
    public static GpxStop[] Get(string name)
    {
        lock (Gate) return All.TryGetValue(name, out var s) ? s : All[White];
    }

    public static bool Has(string name) { lock (Gate) return All.ContainsKey(name); }
}
