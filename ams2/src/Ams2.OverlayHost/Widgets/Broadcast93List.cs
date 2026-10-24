using System.Globalization;
using Ams2.Core;
using Ams2.OverlayHost.Theme;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Lista discreta da TV de 1993 (Race Standings e Qualy Standings): painel cinza translúcido, uma linha por piloto com o NÚMERO DO CARRO
/// em amarelo (alinhado à direita numa coluna estreita) e o SOBRENOME em caixa alta com sombra; abaixo, faixa azul sólida com o texto
/// do patrocinador (só texto, sem logo). Geometria em unidades de design = pixels de 1920x1080 na escala 1.
/// </summary>
public static class Broadcast93List
{
    public const float Width = 320, RowH = 30, Top = 6, BandH = 30, NumRight = 52, NameX = 66, Pad = 10;
    public const int DefaultRows = 6, MinRows = 3, MaxRows = 20;
    public static readonly Color4 Blue = new(20 / 255f, 40 / 255f, 200 / 255f, 1f), White = new(1f, 1f, 1f, 1f);

    public static float Height(int rows, bool sponsor) => Top + rows * RowH + (sponsor ? BandH : 0);

    /// <summary>
    /// Índices (ordem de posição) das linhas da lista. Estática (padrão da TV): os <paramref name="rows"/> primeiros. Dinâmica (Exibição):
    /// a janela de <paramref name="around"/> pilotos centrada no jogador (inclui o jogador) e o topo no resto das linhas.
    /// </summary>
    public static IReadOnlyList<int> Pick(int count, int playerIndex, int rows, bool dynamic, int around)
    {
        rows = Math.Max(1, rows);
        if (!dynamic || playerIndex < 0 || playerIndex < rows) return Enumerable.Range(0, Math.Min(rows, Math.Max(0, count))).ToArray();
        int window = Math.Clamp(around, 1, rows);
        return Ams2.Core.Calc.StandingsSelector.Select(count, playerIndex, rows - window, window).Select(p => p.Index).Take(rows).ToArray();
    }

    /// <summary>Número do carro (real, da pintura) ou, sem ele, a posição: o AMS2 não informa o número pela memória compartilhada.</summary>
    public static string Number(CarSnapshot car, int position)
        => car.CarNumber.Length > 0 ? car.CarNumber : position.ToString(CultureInfo.InvariantCulture);

    public static string Surname(CarSnapshot car, IReadOnlyList<CarSnapshot> field, Ams2.Shared.Profiles.WidgetSettings cfg)
        => cfg.Name(car, BroadcastUi.ShortName(car, field)).ToUpperInvariant();

    /// <summary>Linha: número, sobrenome, texto opcional à direita (gap) e se é o jogador (cor própria).</summary>
    public readonly record struct Line(string Number, string Name, string? Time, bool IsPlayer);

    public static void Draw(ThemeCanvas c, IReadOnlyList<Line> lines, bool sponsor, bool tagHeuer = false)
    {
        var t = c.Theme;
        float listH = Top + lines.Count * RowH;
        c.Panel(0, 0, Width, listH);
        var numFont = t.Numbers with { Size = 25, Weight = 700 };
        var nameFont = t.Text with { Size = 25 };
        var timeFont = t.Numbers with { Size = 20, Weight = 700, Element = "gap" };
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            float y = Top + i * RowH;
            float nameW = Width - NameX - Pad - (l.Time is null ? 0 : 84);
            c.Text(l.Number, BroadcastUi.Fit(c, l.Number, numFont, NumRight - Pad), Pad, y, NumRight - Pad, RowH, t.ValueColor, HAlign.Right, t.ValueShadow);
            c.Text(l.Name, BroadcastUi.Fit(c, l.Name, nameFont, nameW), NameX, y, nameW, RowH, l.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
            if (l.Time is { } tm) c.Text(tm, BroadcastUi.Fit(c, tm, timeFont, 80), Width - Pad - 80, y, 80, RowH, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
        if (!sponsor) return;
        // Faixa dentro do painel (como na TV): olivetti + PHILOS na Qualy Standings; TAG Heuer (largura parcial) na Race Standings.
        float by = listH + 1, bh = BandH - 3;
        if (tagHeuer) SponsorStrip.TagHeuer(c, 6, by, Width * .74f, bh);
        else SponsorStrip.Olivetti(c, 6, by, Width - 12, bh, "PHILOS");
    }
}
