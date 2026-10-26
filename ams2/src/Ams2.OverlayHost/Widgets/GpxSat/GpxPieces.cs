namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>
/// Peca da Exibicao (ShowCatalog "gpxsat") de cada tela despachada pelos modos, pela chave da tela ("modo:tipo:..."):
/// time, sky, pittimer, split, standings e, no Digital 2010, tower/progression/logo. Telas sem peca (micro-time, info) ficam sempre.
/// </summary>
public static class GpxPieces
{
    static readonly string[] Prefixes = ["classic:", "digital:", "d04:", "d10:", "preview:"];

    public static string? Of(GpxScreen sc, int? focusIndex)
    {
        string key = sc.Key;
        bool d10 = key.StartsWith("d10:", StringComparison.Ordinal);
        for (bool again = true; again;)
        {
            again = false;
            foreach (var p in Prefixes)
                if (key.StartsWith(p, StringComparison.Ordinal)) { key = key[p.Length..]; again = true; }
        }
        int i = key.IndexOf(':');
        string token = (i < 0 ? key : key[..i]).ToLowerInvariant();
        return token switch
        {
            "time" => "time",
            "fl" or "out" or "dt" or "win" or "ride" or "sky" or "winner" or "control" => "sky",
            // Digital 2010: a mesma tela "pit:" e o aviso de outro carro nos boxes (sky) e o cronometro do jogador.
            "pit" => d10 && focusIndex is { } f && key[(i + 1)..] == f.ToString(System.Globalization.CultureInfo.InvariantCulture) ? "pittimer" : "sky",
            "pitt" => "pittimer",
            "split" or "splitgreen" or "gapa" or "gapb" or "gap" or "trap" or "card" or "qualisplit" or "qualicard" => "split",
            "str" or "stq" or "stp" or "standings" or "quali" => "standings",
            "cap" => "caption",
            "tower" => "tower",
            "progression" => "progression",
            "f1" => "logo",
            _ => null,
        };
    }
}
