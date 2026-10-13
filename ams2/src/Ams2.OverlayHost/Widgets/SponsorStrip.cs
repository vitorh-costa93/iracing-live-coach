using Ams2.OverlayHost.Theme;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Faixas de patrocinador da TV de 1993 (Olivetti e TAG Heuer): fundo azul royal com gradiente sutil, logo como imagem
/// (Assets\Sponsors) mantendo a proporcao e texto fino/bold em branco. Sem o arquivo do logo, cai no texto.
/// </summary>
public static class SponsorStrip
{
    static readonly Color4 Top = new(24 / 255f, 48 / 255f, 214 / 255f, 1f), Bottom = new(16 / 255f, 32 / 255f, 176 / 255f, 1f), White = new(1, 1, 1, 1);
    const float OlivettiRatio = 600f / 122f, ShieldRatio = 150f / 130f;

    static void Back(ThemeCanvas c, float x, float y, float w, float h)
        => c.VGradientRect(x, y, w, h, [new BarStop(0, Top), new BarStop(1, Bottom)]);

    /// <summary>"olivetti" branco grosso a esquerda e <paramref name="right"/> ("PHILOS", "computer") em fonte fina a direita.</summary>
    public static void Olivetti(ThemeCanvas c, float x, float y, float w, float h, string? right)
    {
        Back(c, x, y, w, h);
        float pad = h * .22f, logoH = h * .70f;
        float logoW = Math.Min(logoH * OlivettiRatio, w * .55f);
        if (!c.Sponsor("olivetti", x + pad, y + (h - logoH) / 2, logoW, logoH))
            c.Text("olivetti", c.Theme.Text with { Size = h * .62f, Weight = 700, Italic = true }, x + pad, y, w * .5f, h, White);
        if (!string.IsNullOrEmpty(right))
            c.Text(right, c.Theme.Text with { Size = h * .62f, Weight = 300, Tracking = 0 }, x + w * .45f, y, w * .55f - pad, h, White, HAlign.Right);
    }

    /// <summary>Escudo TAG Heuer a esquerda e "TAG HEUER" bold branco com espacamento entre letras.</summary>
    public static void TagHeuer(ThemeCanvas c, float x, float y, float w, float h)
    {
        Back(c, x, y, w, h);
        float pad = h * .16f, shieldH = h * .76f, shieldW = shieldH * ShieldRatio;
        float tx = x + pad;
        if (c.Sponsor("tagheuer_shield", x + pad, y + (h - shieldH) / 2, shieldW, shieldH)) tx += shieldW + pad * 1.4f;
        c.Text("TAG HEUER", c.Theme.Text with { Size = h * .66f, Weight = 700, Tracking = h * .07f }, tx, y, x + w - tx - pad, h, White, HAlign.Center);
    }
}
