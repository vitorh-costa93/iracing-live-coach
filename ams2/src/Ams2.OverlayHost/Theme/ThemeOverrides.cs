using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Theme;

/// <summary>
/// Personalizacao de texto por widget aplicada sobre o tema: peso da fonte e cores (textos/titulos, rotulos, valores). Os widgets
/// continuam pedindo tokens ao tema, entao valem para todos sem tocar no desenho. Feito uma vez por Configure, nunca por quadro.
/// </summary>
public static class ThemeOverrides
{
    public static Theme Apply(Theme t, WidgetSettings s)
    {
        var r = t with { Title = t.Title with { Element = "title" }, Label = t.Label with { Element = "label" },
            Text = t.Text with { Element = "name" }, Numbers = t.Numbers with { Element = "value" } };
        if (s.FontWeight is { } fw)
        {
            // A familia de numeros do 1998 (F1 Broadcast 98) so tem um peso: nao recebe peso sintetico.
            bool numbersToo = !t.Numbers.Family.StartsWith("F1 Broadcast", StringComparison.Ordinal);
            r = r with
            {
                Title = r.Title with { Weight = fw }, Label = r.Label with { Weight = fw }, Text = r.Text with { Weight = fw },
                Numbers = numbersToo ? r.Numbers with { Weight = fw } : r.Numbers,
            };
        }
        if (Color(s.TextColor) is { } tc) r = r with { TextColor = tc, TitleColor = tc, NameCellInk = tc };   // NameCellInk: nomes sobre as celulas claras do 2004
        if (Color(s.LabelColor) is { } lc) r = r with { LabelColor = lc };
        if (Color(s.ValueColor) is { } vc) r = r with { ValueColor = vc, NumberColor = vc, ReadoutColor = vc };
        return r;
    }

    public static FontToken ResolveFont(FontToken font, WidgetSettings settings)
    {
        ElementFontSettings? element = null;
        if (font.Element is { } role) settings.ElementFonts?.TryGetValue(role, out element);
        float scale = (settings.TextScale ?? 1) * (element?.Scale ?? 1);
        // The traced broadcast digits have a single fixed outline. Explicit weight customization
        // opts into the bundled multi-weight numeric family; untouched profiles keep the traced face.
        string family = (element?.Weight ?? settings.FontWeight) is not null && font.Family.StartsWith("F1 Broadcast", StringComparison.Ordinal)
            ? "Barlow Semi Condensed" : font.Family;
        return font with { Size = font.Size * scale, Tracking = font.Tracking * scale,
            Weight = element?.Weight ?? settings.FontWeight ?? font.Weight, Family = family };
    }

    static Color4? Color(string? hex) => ColorHex.Parse(hex) is { } c ? new Color4(c.R, c.G, c.B, 1f) : null;
}
