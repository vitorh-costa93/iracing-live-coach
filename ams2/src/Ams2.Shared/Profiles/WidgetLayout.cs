namespace Ams2.Shared.Profiles;

/// <summary>
/// Composicao padrao de cada tema, copiada da transmissao da epoca (tela de referencia 1920x1080): contador de voltas topo-centro,
/// mini-torre (posicao + sigla) no canto superior esquerdo, vencedor embaixo a esquerda, o widget "board" (torre da linha, gap de setor,
/// comparativo de voltas e legenda do piloto) embaixo ao centro no lugar de Relative e Driver Caption (que seguem no catalogo, desligados),
/// lista de pit stops e cronometro de box a meia altura; Weather/Tyres/Fuel (que a TV nao tem) numa coluna discreta a direita e Inputs no canto inferior direito.
/// Tambem guarda o tamanho de projeto de cada widget no perfil padrao do tema, para o teste de sobreposicao
/// (conferido contra os widgets reais por --dump-sizes no teste de integracao).
/// </summary>
public static class WidgetLayout
{
    public const int RefWidth = 1920, RefHeight = 1080;

    public sealed record Slot(int X, int Y, float Scale);

    /// <summary>Widgets de evento que ocupam o mesmo lugar na TV (nunca aparecem juntos): podem se sobrepor.</summary>
    public static readonly IReadOnlyList<string[]> ExclusiveGroups = [["drivercaption", "winner"]];

    static readonly Dictionary<string, Dictionary<string, Slot>> Slots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["f1-1998"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["standings"] = new(32, 24, 1.15f), ["lapcounter"] = new(867, 24, 1.4f),
            ["relative"] = new(640, 900, 0.8f), ["drivercaption"] = new(32, 926, 1.2f), ["winner"] = new(32, 926, 1.2f), ["board"] = new(660, 872, 1f),
            ["pitstops"] = new(32, 560, 0.9f), ["pittimer"] = new(744, 780, 1.2f),
            ["weather"] = new(1696, 24, 0.65f), ["tyres"] = new(1699, 130, 0.65f), ["fuel"] = new(1582, 267, 0.65f), ["inputs"] = new(1542, 957, 0.5f),
            ["radar"] = new(820, 700, 1f),
        },
        ["f1-2004"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["standings"] = new(32, 24, 1.4f), ["lapcounter"] = new(867, 24, 1.4f),
            ["relative"] = new(640, 905, 1.25f), ["drivercaption"] = new(32, 926, 1.3f), ["winner"] = new(32, 926, 1.3f), ["board"] = new(650, 866, 1.2f),
            ["pitstops"] = new(32, 560, 1f), ["pittimer"] = new(769, 790, 1.3f),
            ["weather"] = new(1696, 24, 0.65f), ["tyres"] = new(1699, 130, 0.65f), ["fuel"] = new(1582, 267, 0.65f), ["inputs"] = new(1700, 770, 0.6f),
            ["radar"] = new(820, 700, 1f),
        },
        ["f1-2010s"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["standings"] = new(32, 24, 0.95f), ["lapcounter"] = new(867, 24, 1.4f),
            ["relative"] = new(640, 900, 0.8f), ["drivercaption"] = new(32, 926, 1.2f), ["winner"] = new(32, 926, 1.2f), ["board"] = new(600, 845, 1.1f),
            ["pitstops"] = new(32, 560, 0.9f), ["pittimer"] = new(744, 780, 1.2f),
            ["weather"] = new(1696, 24, 0.65f), ["tyres"] = new(1699, 130, 0.65f), ["fuel"] = new(1582, 267, 0.65f), ["inputs"] = new(1542, 957, 0.5f),
            ["radar"] = new(820, 700, 1f),
        },
    };

    /// <summary>Tamanho de projeto (largura, altura) no perfil padrao do tema. Igual ao DesignSize do widget real.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, (float W, float H)>> DesignSizes =
        new Dictionary<string, IReadOnlyDictionary<string, (float, float)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["f1-1998"] = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase)
            {
                ["standings"] = (191, 372), ["relative"] = (820, 180), ["fuel"] = (470, 156), ["tyres"] = (290, 192), ["weather"] = (295, 144), ["inputs"] = (692, 197),
                ["lapcounter"] = (132, 40), ["drivercaption"] = (334, 94), ["pitstops"] = (716, 156), ["pittimer"] = (360, 42), ["winner"] = (500, 94), ["board"] = (840, 196), ["radar"] = (280, 72),
            },
            ["f1-2004"] = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase)
            {
                ["standings"] = (132, 316), ["relative"] = (546, 108), ["fuel"] = (470, 156), ["tyres"] = (290, 192), ["weather"] = (295, 128), ["inputs"] = (360, 490),
                ["lapcounter"] = (132, 40), ["drivercaption"] = (334, 94), ["pitstops"] = (716, 156), ["pittimer"] = (294, 42), ["winner"] = (448, 94), ["board"] = (590, 164), ["radar"] = (280, 72),
            },
            ["f1-2010s"] = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase)
            {
                ["standings"] = (240, 408), ["relative"] = (820, 180), ["fuel"] = (470, 156), ["tyres"] = (290, 192), ["weather"] = (295, 128), ["inputs"] = (692, 197),
                ["lapcounter"] = (132, 40), ["drivercaption"] = (334, 94), ["pitstops"] = (716, 156), ["pittimer"] = (294, 42), ["winner"] = (448, 94), ["board"] = (760, 210), ["radar"] = (280, 72),
            },
        };

    /// <summary>Posicao e escala padrao do widget no tema; temas/widgets desconhecidos caem no padrao do catalogo.</summary>
    public static Slot Get(string themeId, string widgetId)
    {
        if (Slots.TryGetValue(themeId, out var t) && t.TryGetValue(widgetId, out var s)) return s;
        var d = WidgetCatalog.Find(widgetId);
        return d is null ? new Slot(0, 0, 1f) : new Slot(d.DefaultX, d.DefaultY, d.DefaultScale);
    }

    /// <summary>Retangulo do widget em pixels da tela de referencia (tamanho de projeto x escala), ou null se nao houver tamanho conhecido.</summary>
    public static (double X, double Y, double W, double H)? Rect(string themeId, string widgetId)
    {
        if (!DesignSizes.TryGetValue(themeId, out var sizes) || !sizes.TryGetValue(widgetId, out var sz)) return null;
        var s = Get(themeId, widgetId);
        // Mesmo arredondamento da janela real (Ceiling do tamanho escalado).
        return (s.X, s.Y, Math.Ceiling(sz.W * s.Scale), Math.Ceiling(sz.H * s.Scale));
    }
}
