namespace Ams2.Core.Calc.GpxSat;

/// <summary>Retangulo no espaco logico 1024x768 do GPxSAT.</summary>
public readonly record struct GpxRect(float L, float T, float W, float H)
{
    public float R => L + W;
    public float B => T + H;
}

/// <summary>
/// Espaco logico do GPxSAT: tela 1024x768 (SCR_T = 0, SCR_B = 768, X0 = 0), toda medida derivada de W e da altura de linha
/// <see cref="FH"/> do modo (D2004: 17). <see cref="Fit"/> mapeia esse espaco na area do widget como o GPx faz na tela larga
/// (W = min(largura, altura*4/3), centrado: pillarbox) ou esticando os dois eixos.
/// </summary>
public sealed record GpxGeometry(float FH)
{
    public const float W = 1024, H = 768;

    /// <summary>L0 = X0 + W/5 (204.8): borda esquerda das telas inferiores.</summary>
    public float L0 => W / 5;
    /// <summary>Largura de coluna W/5.</summary>
    public float Col => W / 5;
    /// <summary>Recuo do texto FH&gt;&gt;2.</summary>
    public float Indent => (int)FH >> 2;
    /// <summary>Topo da linha "-nFH": SCR_B - n*FH.</summary>
    public float Row(float n) => H - n * FH;

    public GpxRect Box(float l, float t, float w, float h) => new(l, t, w, h);
    /// <summary>Caixa de uma linha (altura FH) no topo "-nFH".</summary>
    public GpxRect RowBox(float l, float n, float w) => new(l, Row(n), w, FH);

    /// <summary>Escala e deslocamento do espaco logico na area (px) do widget.</summary>
    public static (float Sx, float Sy, float Ox, float Oy) Fit(float targetW, float targetH, bool stretch)
    {
        if (targetW <= 0 || targetH <= 0) return (1, 1, 0, 0);
        if (stretch) return (targetW / W, targetH / H, 0, 0);
        float s = Math.Min(targetW / W, targetH / H);
        return (s, s, (targetW - W * s) / 2, (targetH - H * s) / 2);
    }
}
