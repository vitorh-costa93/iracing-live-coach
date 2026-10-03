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
        if (s.FontWeight is null && s.TextColor is null && s.LabelColor is null && s.ValueColor is null) return t;
        var r = t;
        if (s.FontWeight is { } fw)
        {
            // A familia de numeros do 1998 (F1 Broadcast 98) so tem um peso: nao recebe peso sintetico.
            bool numbersToo = !t.Numbers.Family.StartsWith("F1 Broadcast", StringComparison.Ordinal);
            r = r with
            {
                Title = t.Title with { Weight = fw }, Label = t.Label with { Weight = fw }, Text = t.Text with { Weight = fw },
                Numbers = numbersToo ? t.Numbers with { Weight = fw } : t.Numbers,
            };
        }
        if (Color(s.TextColor) is { } tc) r = r with { TextColor = tc, TitleColor = tc, NameCellInk = tc };   // NameCellInk: nomes sobre as celulas claras do 2004
        if (Color(s.LabelColor) is { } lc) r = r with { LabelColor = lc };
        if (Color(s.ValueColor) is { } vc) r = r with { ValueColor = vc, NumberColor = vc, ReadoutColor = vc };
        return r;
    }

    static Color4? Color(string? hex) => ColorHex.Parse(hex) is { } c ? new Color4(c.R, c.G, c.B, 1f) : null;
}
