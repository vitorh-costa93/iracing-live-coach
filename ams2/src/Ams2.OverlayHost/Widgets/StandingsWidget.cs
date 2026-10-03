using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Classificação no layout 1998–2001 (torre): caixa amarela com a posição, sigla, selo de classe (A, B...) e gap para o
/// líder. Geometria em unidades de design = pixels do mockup 1536x1024.
/// </summary>
public sealed class StandingsWidget : IWidget
{
    public string Id => "standings";
    public int Rows { get; set; } = 8;
    WidgetSettings _cfg = new() { Id = "standings" };
    public void Configure(WidgetSettings s) { _cfg = s; Rows = s.Rows ?? 8; }
    public (float Width, float Height) DesignSize => (_b04 ? Width2004 : Layout().Width, 12 + Rows * Pitch + 2);
    bool _b04;
    public void UseTheme(Theme.Theme theme) => _b04 = theme.Style == ThemeStyle.Broadcast2000s;
    float Pitch => _b04 ? RowPitch2000s : RowPitch;

    const float RowTop = 12, RowPitch = 43, RowPitch2000s = 36;
    const float BoxX = 19, BoxW = 40, BoxH = 34;
    const float NameCellW = 104, BadgeW = 40, GapW = 170, GapOnlyW = 125, ColSpacing = 8, EdgeRight = 36;
    const float NameX = 75; // so para a mensagem de espera

    /// <summary>Posicoes das colunas visiveis, da esquerda para a direita, sem buracos (todas visiveis = layout do mockup).</summary>
    readonly record struct Cols(float PosX, float NameCellX, float BadgeCx, float GapRight, float Width);

    Cols Layout()
    {
        float x = BoxX;
        float posX = x, nameX = 0, badgeCx = 0, gapRight = 0;
        bool any = false;
        if (_cfg.ColumnVisible("pos")) { x += BoxW + ColSpacing; any = true; }
        if (_cfg.ColumnVisible("name")) { nameX = x; x += NameCellW + ColSpacing + 1; any = true; }
        if (_cfg.ColumnVisible("class")) { badgeCx = x + BadgeW / 2; x += BadgeW + ColSpacing; any = true; }
        if (_cfg.ColumnVisible("gap")) { x += any ? GapW : GapOnlyW; gapRight = x; any = true; }
        else x -= ColSpacing;
        if (!any) x = BoxX + 100;
        return new Cols(posX, nameX, badgeCx, gapRight, x + (_cfg.ColumnVisible("gap") ? EdgeRight : BoxX));
    }

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

        var L = Layout();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float y = RowTop + i * Pitch;
            if (t.Style == ThemeStyle.Broadcast2000s) { DrawRow2000s(c, t, r, L, y, classes); continue; }
            if (_cfg.ColumnVisible("pos"))
            {
                Chrome.AccentBox(c, L.PosX, y, BoxW, BoxH, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers);
            }
            if (_cfg.ColumnVisible("name"))
            {
                var ink = Chrome.NameCell(c, L.NameCellX, y, NameCellW, BoxH, r.IsPlayer ? t.PlayerColor : t.TextColor);
                c.Text(RelativeWidget.Code(r.Car.Name), t.Text, L.NameCellX + 8, y, 130, BoxH, ink, shadow: t.NameCellFill.A > 0f ? null : t.TextShadow);
            }
            if (_cfg.ColumnVisible("class")) DrawBadge(c, t, L.BadgeCx, y + BoxH / 2, (char)('A' + Math.Min(classes.IndexOf(r.Car.ClassName), 25)));
            if (_cfg.ColumnVisible("gap")) c.Text(FormatGap(r), t.Numbers, L.GapRight - 160, y, 160, BoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
    }

    // Mini-torre 2004-2008 (transmissao): [pos][sigla][bandeira][pneu][classe][gap], celulas coladas. O lider mostra "Lap N" em celula preta.
    const float X04 = 4, Pos04 = 40, Name04 = 82, Flag04 = 46, Tyre04 = 30, Class04 = 40, Gap04 = 112, Lead04 = 100;
    float Width2004
    {
        get
        {
            float x = X04;
            if (_cfg.ColumnVisible("pos")) x += Pos04;
            if (_cfg.ColumnVisible("name")) x += Name04;
            if (_cfg.ColumnVisible("flag")) x += Flag04;
            if (_cfg.ColumnVisible("tyre")) x += Tyre04;
            if (_cfg.ColumnVisible("class")) x += Class04;
            x += _cfg.ColumnVisible("gap") ? Gap04 : Lead04;
            return x + 6;
        }
    }

    /// <summary>Linha flutuante 2004–2008: caixa de posição (líder vermelho), sigla em célula branca, bandeira, pneu, selo de classe e célula preta, coladas.</summary>
    void DrawRow2000s(ThemeCanvas c, Theme.Theme t, StandingRow r, Cols L, float y, List<string> classes)
    {
        float h = BoxH, x = X04;
        if (_cfg.ColumnVisible("pos")) { Chrome.PositionBox(c, x, y, Pos04, h, r.Car.Position, t.Numbers); x += Pos04; }
        if (_cfg.ColumnVisible("name")) { Chrome.WhiteCell(c, x, y, Name04, h, RelativeWidget.Code(r.Car.Name), t.Text, ink: r.IsPlayer ? Chrome.PlayerInk : null); x += Name04; }
        if (_cfg.ColumnVisible("flag"))
        {
            Chrome.Box(c, x, y, Flag04, h, "", t.Text, Chrome.CellKind.White);
            c.Flag(r.Car.Nationality, x + 8, y + 4, Flag04 - 16, h - 8);
            x += Flag04;
        }
        if (_cfg.ColumnVisible("tyre"))
        {
            if (!Chrome.TyreBox(c, x, y, Tyre04, h, r.Car.TyreSupplier, t.Text with { Size = 20 })) Chrome.Box(c, x, y, Tyre04, h, "", t.Text, Chrome.CellKind.Navy);
            x += Tyre04;
        }
        if (_cfg.ColumnVisible("class"))
        {
            Chrome.Box(c, x, y, Class04, h, ((char)('A' + Math.Min(classes.IndexOf(r.Car.ClassName), 25))).ToString(), t.Text, Chrome.CellKind.Navy, HAlign.Center, 0);
            x += Class04;
        }
        if (_cfg.ColumnVisible("gap")) Chrome.BlackCell(c, x, y, Gap04, h, LeaderLap(r) ?? FormatGap(r), t.Numbers);
        else if (LeaderLap(r) is { } lap) Chrome.BlackCell(c, x, y, Lead04, h, lap, t.Numbers);
    }

    /// <summary>2004–2008: a célula do líder mostra a volta atual ("Lap 26"), como na transmissão.</summary>
    static string? LeaderLap(StandingRow r) => r.Car.Position == 1 && r.Car.CurrentLap > 0 ? "Lap " + r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture) : null;

    static void DrawBadge(ThemeCanvas c, Theme.Theme t, float cx, float cy, char letter)
    {
        if (t.Style == ThemeStyle.Broadcast98) c.FillEllipse(cx, cy, 20, 20, t.BadgeFill);
        else c.FillRoundRect(cx - 20, cy - 14, 40, 28, t.BoxRadius, t.BadgeFill);
        c.Text(letter.ToString(), t.Text, cx - 20, cy - 17, 40, 34, t.BadgeInk, HAlign.Center);
    }

    public static string FormatGap(StandingRow r)
    {
        if (r.LapsBehind > 0) return "+" + r.LapsBehind.ToString(CultureInfo.InvariantCulture) + "L";
        if (r.GapToLeader is not { } g) return "--.---";
        return "+" + g.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
