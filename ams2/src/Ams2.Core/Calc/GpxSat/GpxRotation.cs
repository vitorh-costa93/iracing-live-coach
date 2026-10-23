namespace Ams2.Core.Calc.GpxSat;

/// <summary>Pagina da rotacao do slot main; <see cref="Slides"/> = quadros que ela ocupa (standings = uma por pagina de carros; 0 = sem dado, pulada).</summary>
public readonly record struct GpxRotationPage(string Id, int Slides = 1);

/// <summary>Quadro atual da rotacao: pagina e indice do quadro dentro dela.</summary>
public readonly record struct GpxRotationSlot(string Id, int Slide);

/// <summary>Rotacao do slot main do GPxSAT: cada quadro dura <c>pageSeconds</c>, percorrendo so as paginas com dado.</summary>
public static class GpxRotation
{
    public static int PagesOf(int count, int perPage) => count <= 0 ? 0 : (count + Math.Max(1, perPage) - 1) / Math.Max(1, perPage);

    /// <summary>
    /// Janela dos standings de corrida aberta quando o lider fecha a volta: pagina corrente (uma passagem por pagina, <paramref name="secondsPerPage"/> cada,
    /// estatica dentro dela) ou null fora da janela / sem evento / sem dado.
    /// </summary>
    public static int? EventPage(double? openT, double now, double secondsPerPage, int pages)
    {
        if (openT is not { } t || pages <= 0 || now < t) return null;
        int page = (int)Math.Floor((now - t) / Math.Max(0.5, secondsPerPage));
        return page < pages ? page : null;
    }

    public static GpxRotationSlot? Current(IReadOnlyList<GpxRotationPage> pages, double now, double pageSeconds)
    {
        int total = 0;
        foreach (var p in pages) total += Math.Max(0, p.Slides);
        if (total == 0) return null;
        int index = (int)(Math.Floor(Math.Max(0, now) / Math.Max(0.5, pageSeconds)) % total);
        foreach (var p in pages)
        {
            if (p.Slides <= 0) continue;
            if (index < p.Slides) return new(p.Id, index);
            index -= p.Slides;
        }
        return null;
    }
}
