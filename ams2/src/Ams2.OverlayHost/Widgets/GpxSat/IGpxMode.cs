namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>
/// Um modo do GPxSAT (Classic, Digital, Digital 2004, Digital 2010): parametros visuais e o despacho das telas. Um arquivo por modo em
/// <c>Modes/</c>. <see cref="Dispatch"/> e chamado a cada quadro e devolve as telas candidatas (uma ou mais por slot); o widget escolhe a
/// de maior prioridade de cada slot, anima com o <see cref="Ams2.Core.Calc.GpxSat.GpxAnimator"/> do slot e desenha.
/// </summary>
public interface IGpxMode
{
    /// <summary>Id curto ("classic", "digital", "digital2004", "digital2010").</summary>
    string Id { get; }
    /// <summary>FH e fontes do modo (gpxsat.ini [Font]).</summary>
    GpxModeSpec Spec { get; }
    /// <summary>Paginas do standings (gpxsat.ini CarsPerPage).</summary>
    int CarsPerPage { get; }
    /// <summary>false = placeholder (modo ainda nao implementado): o perfil padrao mantem os widgets antigos do tema.</summary>
    bool Implemented { get; }
    /// <summary>Telas desejadas agora. Lista vazia = nada a desenhar.</summary>
    IReadOnlyList<GpxScreen> Dispatch(GpxContext ctx);
}

/// <summary>Tema do overlay -> modo do GPxSAT.</summary>
public static class GpxModeRegistry
{
    /// <summary>Temas com o widget "gpxsat" e o modo de cada um.</summary>
    public static readonly IReadOnlyDictionary<string, string> ThemeModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["f1-1998"] = "classic",
        ["f1-1999-digital"] = "digital",
        ["f1-2004"] = "digital2004",
        ["f1-2012"] = "digital2010",
    };

    public static IGpxMode Create(string? themeId)
        => (themeId is not null && ThemeModes.TryGetValue(themeId, out var m) ? m : "digital2004") switch
        {
            "classic" => new Modes.ClassicMode(),
            "digital" => new Modes.DigitalMode(),
            "digital2010" => new Modes.Digital2010Mode(),
            _ => new Modes.Digital2004Mode(),
        };
}
