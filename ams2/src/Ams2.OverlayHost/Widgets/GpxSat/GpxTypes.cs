using Ams2.Core.Calc.GpxSat;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>Alinhamento horizontal do texto na caixa (FONT.vt+0x18: 0 = esquerda, 1 = centro, 2 = direita; vertical sempre centrado).</summary>
public enum GpxAlign { Left = 0, Center = 1, Right = 2 }

/// <summary>Papel da fonte: normal (FH) ou FONT_BIG (numero das caixas de posicao 1.5FH x 2FH).</summary>
public enum GpxFontRole { Normal, Big, Small, Mono }

/// <summary>Imagem procedural no lugar da textura (os TGAs do GPxPatch nao sao usados nem copiados).</summary>
public enum GpxImage { None, Flag, TyreM, TyreB, CarNumber, Position, Winner, BigOne,
    /// <summary>Bandeira do pais do piloto (alpha-3 do jogo em <see cref="GpxBox.Text"/>), PNG de Assets\Flags; sem arquivo nao desenha nada.</summary>
    CountryFlag }

/// <summary>Cores ARGB de texto do GPxSAT.</summary>
public static class GpxInk
{
    public const uint Black = 0xff010101, White = 0xfffefefe, Session = 0xffd4d0c8;
}

/// <summary>
/// Uma caixa da tela, no espaco logico 1024x768: textura (nome em <see cref="GpxTextures"/>), texto opcional, alinhamento,
/// cor ARGB, recuo (NaN = recuo padrao do modo, FH/4) e o grupo do animador (linha ou coluna, 0..n). <see cref="Logo"/> = caixa do
/// logo opcional (procedural: textura + texto do proprio box), desenhada so na fase do logo.
/// </summary>
public sealed record GpxBox(GpxRect Rect, string Texture, string? Text = null, GpxAlign Align = GpxAlign.Left, uint Argb = GpxInk.Black,
    int Group = 0, float Indent = float.NaN, GpxFontRole Font = GpxFontRole.Normal, GpxImage Image = GpxImage.None, bool Logo = false)
{
    /// <summary>Alfa da textura 0..1 (Classic: caixas translucidas). O texto fica opaco.</summary>
    public float Alpha { get; init; } = 1f;
    public uint Stroke { get; init; }
    public bool Shadow { get; init; }
    public bool TopAligned { get; init; }
    public bool Horizontal { get; init; }
    public float Slant { get; init; }
    public string? Asset { get; init; }
    public GpxRect? SourceRect { get; init; }

}

/// <summary>
/// Tela que o modo pede para desenhar num "slot" (camada independente: p.ex. "top" para o time left e "main" para as telas de baixo).
/// <see cref="Key"/> = keyA do animador (mudou = anima do zero e arma o logo); <see cref="Page"/> = keyB (mudou = anima sem o logo).
/// Entre as telas do mesmo slot vence a de maior <see cref="Priority"/>. <see cref="Groups"/> = numero de linhas/colunas animadas.
/// </summary>
public sealed record GpxScreen(string Slot, string Key, IReadOnlyList<GpxBox> Boxes, GpxAnimKind Anim = GpxAnimKind.Rows, int Page = 0, int Priority = 0)
{
    public int Groups => Boxes.Count == 0 ? 0 : Boxes.Where(b => !b.Logo).Select(b => b.Group).DefaultIfEmpty(-1).Max() + 1;
}

/// <summary>Fonte de um papel: familias em ordem de preferencia (original do GPxPatch, se carregada em runtime, e a livre do repo).</summary>
public sealed record GpxFontSpec(string[] Families, int Weight, float SizeFh, int FallbackWeight = 600, float FallbackSizeFh = 0)
{
    /// <summary>Tamanho (em FH) quando cai na fonte livre (0 = o mesmo).</summary>
    public float FallbackSize => FallbackSizeFh > 0 ? FallbackSizeFh : SizeFh;
}

/// <summary>Parametros visuais de um modo: FH logico (a 768), fontes por papel e se anima.</summary>
public sealed record GpxModeSpec(float FH, GpxFontSpec Normal, GpxFontSpec Big, GpxFontSpec? Small = null)
{
    public GpxFontSpec For(GpxFontRole role) => role switch { GpxFontRole.Big => Big, GpxFontRole.Small => Small ?? Normal, GpxFontRole.Mono => new(["Courier New"], 400, 10/FH), _ => Normal };
}
