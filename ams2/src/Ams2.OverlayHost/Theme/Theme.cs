using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Theme;

/// <summary>Fonte de um papel tipográfico do tema. Tamanho em unidades de design (escalado na renderização).</summary>
public sealed record FontToken(string Family, int Weight, float Size);

/// <summary>Sombra projetada do texto: deslocamento em unidades de design e cor (com a opacidade).</summary>
public sealed record ShadowToken(float OffsetX, float OffsetY, Color4 Color);

/// <summary>Parada de um gradiente horizontal (posição 0..1).</summary>
public readonly record struct BarStop(float Position, Color4 Color);

/// <summary>
/// Tema = pacote de tokens (fontes, cores, cantos, bordas, sombra). Os widgets nunca usam valores literais:
/// pedem tudo ao tema, então trocar de tema muda o visual sem tocar nos widgets.
/// </summary>
public sealed record Theme(
    string Id,
    string DisplayName,
    // Fontes
    FontToken Title,     // título do painel (RELATIVE)
    FontToken Label,     // rótulos de seção (AHEAD / BEHIND)
    FontToken Text,      // nomes
    FontToken Numbers,   // números e valores (família de números do tema)
    // Cores
    Color4 PanelFill,
    Color4 PanelBorder,
    Color4 TitleColor,
    Color4 LabelColor,
    Color4 TextColor,
    Color4 NumberColor,  // posições
    Color4 ValueColor,   // gaps e tempos
    Color4 PlayerColor,
    Color4 AccentFill,   // caixa amarela do ícone
    Color4 AccentInk,    // tinta sobre a caixa amarela
    BarStop[] TitleBar,
    // Geometria
    float CornerRadius,
    float BorderWidth,
    // Sombra dos valores/textos
    ShadowToken TextShadow,
    ShadowToken ValueShadow);

public static class Themes
{
    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>
    /// Transmissão de F1 1998–2001: painel cinza-esverdeado escuro, títulos em Reddit Sans 800, números em
    /// F1 Broadcast 98 Values amarelos com sombra preta (~2 px direita/baixo, ~62%, medida na captura de 23 px de dígito).
    /// </summary>
    public static readonly Theme F1_1998 = new(
        Id: "f1-1998",
        DisplayName: "F1 1998-2001",
        Title: new FontToken("Reddit Sans", 800, 29f),
        Label: new FontToken("Reddit Sans", 800, 25f),
        Text: new FontToken("Reddit Sans", 800, 28f),
        Numbers: new FontToken("F1 Broadcast 98 Values", 400, 28f),
        PanelFill: Rgb(49, 54, 49, 0.90f),
        PanelBorder: Rgb(61, 66, 62, 0.9f),
        TitleColor: Rgb(232, 233, 232),
        LabelColor: Rgb(96, 204, 200),
        TextColor: Rgb(236, 236, 236),
        NumberColor: Rgb(228, 210, 84),
        ValueColor: Rgb(224, 214, 138),
        PlayerColor: Rgb(255, 222, 70),
        AccentFill: Rgb(226, 206, 56),
        AccentInk: Rgb(24, 24, 20),
        TitleBar:
        [
            new(0.00f, Rgb(50, 49, 36)),
            new(0.25f, Rgb(134, 111, 39)),
            new(0.45f, Rgb(190, 152, 46)),
            new(0.65f, Rgb(219, 199, 91)),
            new(0.85f, Rgb(235, 227, 173)),
            new(1.00f, Rgb(230, 228, 216)),
        ],
        CornerRadius: 0f,
        BorderWidth: 1f,
        TextShadow: new ShadowToken(2f, 2f, Rgb(0, 0, 0, 0.62f)),
        ValueShadow: new ShadowToken(2f, 2f, Rgb(0, 0, 0, 0.62f)));

    public static IReadOnlyList<Theme> All { get; } = [F1_1998];

    public static Theme Get(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? F1_1998;
}
