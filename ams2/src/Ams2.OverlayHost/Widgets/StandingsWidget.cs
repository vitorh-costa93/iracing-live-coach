using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Classificação no layout 1998–2001 (torre): caixa amarela com a posição, sigla, selo de classe (A, B...) e gap para o
/// líder. Geometria em unidades de design = pixels do mockup 1536x1024.
/// </summary>
public sealed class StandingsWidget : IWidget
{
    public string Id => "standings";
    public int Rows { get; init; } = 8;
    public (float Width, float Height) DesignSize => (433, 12 + Rows * RowPitch + 2);

    const float RowTop = 12, RowPitch = 43;
    const float BoxX = 19, BoxW = 40, BoxH = 34;
    const float NameX = 75, BadgeCx = 200, ValueRight = 397;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        if (!m.Connected || m.Standings.Count == 0)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, NameX, RowTop, 340, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        // Letra da classe pela ordem de aparição na classificação (líder da classe mais rápida = A).
        var classes = m.Standings.Select(r => r.Car.ClassName).Distinct().ToList();
        var rows = m.Standings.Take(Rows).ToList();
        // Jogador fora do top N: substitui a última linha para ele sempre aparecer.
        var me = m.Standings.FirstOrDefault(r => r.IsPlayer);
        if (me is not null && !rows.Contains(me) && rows.Count > 0) rows[^1] = me;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float y = RowTop + i * RowPitch;
            c.FillRect(BoxX, y, BoxW, BoxH, t.AccentFill);
            c.Text(r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers, BoxX, y - 1, BoxW, BoxH, t.AccentInk, HAlign.Center);
            c.Text(RelativeWidget.Code(r.Car.Name), t.Text, NameX, y, 130, BoxH, r.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
            DrawBadge(c, t, BadgeCx, y + BoxH / 2, (char)('A' + Math.Min(classes.IndexOf(r.Car.ClassName), 25)));
            c.Text(FormatGap(r), t.Numbers, ValueRight - 160, y, 160, BoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
    }

    static void DrawBadge(ThemeCanvas c, Theme.Theme t, float cx, float cy, char letter)
    {
        c.FillEllipse(cx, cy, 20, 20, t.BadgeFill);
        c.Text(letter.ToString(), t.Text, cx - 20, cy - 17, 40, 34, t.BadgeInk, HAlign.Center);
    }

    public static string FormatGap(StandingRow r)
    {
        if (r.LapsBehind > 0) return "+" + r.LapsBehind.ToString(CultureInfo.InvariantCulture) + "L";
        if (r.GapToLeader is not { } g) return "--.---";
        return "+" + g.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
