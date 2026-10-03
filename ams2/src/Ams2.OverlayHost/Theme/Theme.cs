using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Theme;

/// <summary>Fonte de um papel tipográfico do tema. Tamanho em unidades de design (escalado na renderização).</summary>
public sealed record FontToken(string Family, int Weight, float Size, float Tracking = 0f);

/// <summary>Sombra projetada do texto: deslocamento em unidades de design e cor (com a opacidade).</summary>
public sealed record ShadowToken(float OffsetX, float OffsetY, Color4 Color);

/// <summary>Parada de um gradiente horizontal (posição 0..1).</summary>
public readonly record struct BarStop(float Position, Color4 Color);

/// <summary>
/// Tema = pacote de tokens (fontes, cores, cantos, bordas, sombra). Os widgets nunca usam valores literais:
/// pedem tudo ao tema, então trocar de tema muda o visual sem tocar nos widgets.
/// </summary>
/// <summary>Família visual do tema: só escolhe como o chrome (cabeçalho, caixas, mostradores) é desenhado, nunca o que o widget mostra.</summary>
public enum ThemeStyle { Broadcast98, Broadcast2000s, Modern2010s }

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
    // Gráfico de pedais, divisórias e selo de classe
    Color4 ThrottleColor,
    Color4 BrakeColor,
    Color4 SteeringColor,
    Color4 GraphAxis,
    Color4 Divider,
    Color4 BadgeFill,
    Color4 BadgeInk,
    // Geometria
    float CornerRadius,
    float BorderWidth,
    // Sombra dos valores/textos
    ShadowToken TextShadow,
    ShadowToken ValueShadow,
    // Estilo (padrões = comportamento do tema 1998)
    ThemeStyle Style = ThemeStyle.Broadcast98,
    Color4 ReadoutColor = default,      // valores de Fuel/Tyres/Weather (no 1998 é a cor de rótulo)
    Color4 NameCellFill = default,      // célula atrás do nome (alfa 0 = sem célula)
    Color4 NameCellInk = default,
    Color4 AccentBar = default,         // faixa vermelha (sublinhado 2004, barra inclinada 2010s)
    float BoxRadius = 0f,               // canto das caixas de posição
    float TitleBarHeight = 15f,
    Color4 ValueCellFill = default);   // célula preta atrás de valores (2004–2008; alfa 0 = sem célula)

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
        Title: new FontToken("Reddit Sans", 800, 30f),
        Label: new FontToken("Reddit Sans", 800, 25f),
        Text: new FontToken("Reddit Sans", 800, 30f),
        Numbers: new FontToken("F1 Broadcast 98 Values", 400, 32f, 3f),
        PanelFill: Rgb(28, 35, 38, 0.72f),
        PanelBorder: Rgb(0, 0, 0, 0f),
        TitleColor: Rgb(255, 255, 255),
        LabelColor: Rgb(39, 185, 196),
        TextColor: Rgb(255, 255, 255),
        NumberColor: Rgb(242, 224, 42),
        ValueColor: Rgb(242, 224, 42),
        PlayerColor: Rgb(255, 222, 70),
        AccentFill: Rgb(242, 224, 42),
        AccentInk: Rgb(8, 8, 6),
        TitleBar:
        [
            // degradê da barra de tempo dividido: preto -> amarelo -> branco
            new(0.00f, Rgb(16, 16, 10)),
            new(0.50f, Rgb(242, 224, 42)),
            new(1.00f, Rgb(250, 248, 236)),
        ],
        ThrottleColor: Rgb(74, 196, 62),
        BrakeColor: Rgb(214, 36, 40),
        SteeringColor: Rgb(240, 240, 236),
        GraphAxis: Rgb(226, 228, 224),
        Divider: Rgb(176, 178, 170, 0.75f),
        BadgeFill: Rgb(39, 185, 196),
        BadgeInk: Rgb(6, 24, 28),
        CornerRadius: 0f,
        BorderWidth: 0f,
        TextShadow: new ShadowToken(2f, 2f, Rgb(0, 0, 0, 0.62f)),
        ValueShadow: new ShadowToken(2f, 2f, Rgb(0, 0, 0, 0.62f)),
        ReadoutColor: Rgb(39, 185, 196));

    /// <summary>
    /// Transmissão de F1 2004–2008: linhas flutuantes sem painel (véu preto quase invisível, sem borda): caixa vermelha de posição,
    /// célula branca com o nome, célula preta com o valor e legendas em caixa branca. O Inputs vira um velocímetro analógico com pedais.
    /// </summary>
    public static readonly Theme F1_2004 = new(
        Id: "f1-2004",
        DisplayName: "F1 2004-2008",
        Title: new FontToken("Open Sans", 700, 26f),
        Label: new FontToken("Open Sans", 700, 22f),
        Text: new FontToken("Open Sans", 700, 25f),
        Numbers: new FontToken("Open Sans", 700, 25f),
        PanelFill: Rgb(0, 0, 0, 0.08f),
        PanelBorder: Rgb(0, 0, 0, 0f),
        TitleColor: Rgb(255, 255, 255),
        LabelColor: Rgb(255, 255, 255),
        TextColor: Rgb(255, 255, 255),
        NumberColor: Rgb(255, 255, 255),
        ValueColor: Rgb(255, 255, 255),
        PlayerColor: Rgb(255, 214, 64),
        AccentFill: Rgb(200, 18, 32),
        AccentInk: Rgb(255, 255, 255),
        TitleBar:
        [
            new(0.00f, Rgb(214, 24, 40)),
            new(0.60f, Rgb(214, 24, 40, 0.8f)),
            new(1.00f, Rgb(214, 24, 40, 0f)),
        ],
        ThrottleColor: Rgb(32, 190, 56),
        BrakeColor: Rgb(222, 28, 36),
        SteeringColor: Rgb(255, 255, 255),
        GraphAxis: Rgb(232, 232, 232),
        Divider: Rgb(200, 200, 200, 0.6f),
        BadgeFill: Rgb(46, 46, 54),
        BadgeInk: Rgb(255, 255, 255),
        CornerRadius: 0f,
        BorderWidth: 0f,
        TextShadow: new ShadowToken(1f, 1f, Rgb(0, 0, 0, 0.45f)),
        ValueShadow: new ShadowToken(1f, 1f, Rgb(0, 0, 0, 0.45f)),
        Style: ThemeStyle.Broadcast2000s,
        ReadoutColor: Rgb(255, 255, 255),
        NameCellFill: Rgb(246, 246, 246),
        NameCellInk: Rgb(20, 20, 24),
        AccentBar: Rgb(214, 24, 40),
        BoxRadius: 0f,
        TitleBarHeight: 4f,
        ValueCellFill: Rgb(6, 6, 8, 0.97f));

    /// <summary>
    /// F1 2010s: painéis escuros arredondados, Barlow Semi Condensed, caixas de posição amarelas com número preto,
    /// barra inclinada vermelha antes do título (mockup v5/2010s). Sem sombra de texto.
    /// </summary>
    public static readonly Theme F1_2010s = new(
        Id: "f1-2010s",
        DisplayName: "F1 2010s",
        Title: new FontToken("Barlow Semi Condensed", 600, 27f, 0.5f),
        Label: new FontToken("Barlow Semi Condensed", 400, 23f, 0.4f),
        Text: new FontToken("Barlow Semi Condensed", 600, 27f, 0.4f),
        Numbers: new FontToken("Barlow Semi Condensed", 600, 27f),
        PanelFill: Rgb(12, 20, 35, 0.94f),
        PanelBorder: Rgb(255, 255, 255, 0.07f),
        TitleColor: Rgb(255, 255, 255),
        LabelColor: Rgb(152, 163, 182),
        TextColor: Rgb(246, 248, 252),
        NumberColor: Rgb(246, 248, 252),
        ValueColor: Rgb(246, 248, 252),
        PlayerColor: Rgb(255, 214, 0),
        AccentFill: Rgb(255, 214, 0),
        AccentInk: Rgb(12, 14, 20),
        TitleBar:
        [
            new(0.00f, Rgb(255, 255, 255, 0.22f)),
            new(1.00f, Rgb(255, 255, 255, 0f)),
        ],
        ThrottleColor: Rgb(46, 204, 74),
        BrakeColor: Rgb(236, 44, 44),
        SteeringColor: Rgb(255, 255, 255),
        GraphAxis: Rgb(255, 255, 255, 0.55f),
        Divider: Rgb(255, 255, 255, 0.14f),
        BadgeFill: Rgb(110, 34, 150),
        BadgeInk: Rgb(255, 255, 255),
        CornerRadius: 9f,
        BorderWidth: 1f,
        TextShadow: new ShadowToken(0f, 0f, Rgb(0, 0, 0, 0f)),
        ValueShadow: new ShadowToken(0f, 0f, Rgb(0, 0, 0, 0f)),
        Style: ThemeStyle.Modern2010s,
        ReadoutColor: Rgb(246, 248, 252),
        AccentBar: Rgb(228, 10, 0),
        BoxRadius: 3f,
        TitleBarHeight: 2f);

    public static IReadOnlyList<Theme> All { get; } = [F1_1998, F1_2004, F1_2010s];

    public static Theme Get(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? F1_1998;
}
