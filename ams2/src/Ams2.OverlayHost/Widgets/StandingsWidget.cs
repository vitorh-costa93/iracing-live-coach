using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Classificação (canto superior esquerdo): por padrão só posição + SIGLA de 3 letras (bandeira, selo de classe e gap são colunas
/// opcionais). Quantos pilotos: <c>TopCount</c> no topo + <c>NearCount</c> ao redor do jogador, como no iRacing/V3
/// (<see cref="Ams2.Core.Calc.StandingsSelector"/>), com "..." entre o topo e a janela quando há salto de posições; o jogador
/// aparece sempre. Geometria em unidades de design = pixels do mockup 1536x1024.
/// </summary>
public sealed class StandingsWidget : IWidget
{
    public string Id => "standings";
    /// <summary>Capacidade de linhas: topo + janela (no mínimo o jogador). O tamanho da janela não muda com a posição do jogador.</summary>
    public int Rows => _cfg.EffectiveTop + Math.Max(_cfg.EffectiveNear, 1);
    WidgetSettings _cfg = new() { Id = "standings" };
    public void Configure(WidgetSettings s) { _cfg = s; }
    /// <summary>Espaço reservado para o separador "..." (só existe se há topo; o painel só o ocupa quando há salto).</summary>
    float SepReserve => _cfg.EffectiveTop > 0 ? SepH : 0;
    const float SepH = 14;
    public (float Width, float Height) DesignSize => TableMode ? (TableWidth, TableTop + TableRows * TablePitch + TableBottom)
        
        : (_b04 ? Width2004 : _b10 ? Math.Max(Layout().Width, MinWidth10) : Layout().Width, Top + Rows * Pitch + SepReserve + 2);
    bool _b04, _b98, _b10;
    public void UseTheme(Theme.Theme theme) { _b04 = theme.Style == ThemeStyle.Broadcast2000s; _b98 = theme.Style == ThemeStyle.Broadcast98; _b10 = theme.Style == ThemeStyle.Modern2010s; }
    /// <summary>2010s: cabecalho "RACE" + "LAP n / N" (mockup v5) empurra as linhas para baixo.</summary>
    float Top => _b10 && !TableMode ? RowTop + HeaderH10 : RowTop;
    const float HeaderH10 = 36, MinWidth10 = 240; // cabecalho "RACE" + "LAP n / N" precisa de largura mesmo sem a coluna de gap
    /// <summary>1998–2001 com a coluna "table": tabela inferior de 2 colunas (como a faixa do GP do Brasil 2003). Sem ela, a lista vertical.</summary>
    bool TableMode => _b98 && _cfg.ColumnVisible("table");
    float Pitch => _b04 ? RowPitch2000s : RowPitch;

    const float RowTop = 12, RowPitch = 43, RowPitch2000s = 36;
    const float BoxX = 19, BoxW = 40, BoxH = 34;
    const float NameCellW = 104, FlagW = 44, BadgeW = 40, GapW = 170, GapOnlyW = 125, ColSpacing = 8, EdgeRight = 36;
    const float NameX = 75; // so para a mensagem de espera

    /// <summary>Posicoes das colunas visiveis, da esquerda para a direita, sem buracos (todas visiveis = layout do mockup).</summary>
    readonly record struct Cols(float PosX, float NameCellX, float FlagX, float BadgeCx, float GapRight, float Width);

    Cols Layout()
    {
        float x = BoxX;
        float posX = x, nameX = 0, flagX = 0, badgeCx = 0, gapRight = 0;
        bool any = false;
        if (_cfg.ColumnVisible("pos")) { x += BoxW + ColSpacing; any = true; }
        if (_cfg.ColumnVisible("name")) { nameX = x; x += NameCellW + ColSpacing + 1; any = true; }
        if (_cfg.ColumnVisible("flag")) { flagX = x; x += FlagW + ColSpacing; any = true; }
        if (_cfg.ColumnVisible("class")) { badgeCx = x + BadgeW / 2; x += BadgeW + ColSpacing; any = true; }
        if (_cfg.ColumnVisible("gap")) { x += any ? GapW : GapOnlyW; gapRight = x; any = true; }
        else x -= ColSpacing;
        if (!any) x = BoxX + 100;
        return new Cols(posX, nameX, flagX, badgeCx, gapRight, x + (_cfg.ColumnVisible("gap") ? EdgeRight : BoxX));
    }

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        if (!m.Connected || m.Standings.Count == 0)
        {
            c.Panel(0, 0, w, h);
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, TableMode ? 24 : NameX, TableMode ? TableTop : RowTop, 340, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        // Letra da classe pela ordem de aparição na classificação (líder da classe mais rápida = A).
        var classes = m.Standings.Select(r => r.Car.ClassName).Distinct().ToList();
        // Topo + janela ao redor do jogador (mesma regra do V3), com salto de posições marcado.
        int me = -1;
        for (int i = 0; i < m.Standings.Count; i++) if (m.Standings[i].IsPlayer) { me = i; break; }
        var picks = StandingsSelector.Select(m.Standings.Count, me, _cfg.EffectiveTop, _cfg.EffectiveNear);
        var rows = picks.Select(p => m.Standings[p.Index]).ToList();
        var jumps = picks.Select(p => p.GapBefore).ToList();

        if (TableMode) { c.Panel(0, 0, w, h); DrawTable(c, t, rows, m); return; }
        var L = Layout();
        int gapCount = jumps.Count(j => j);
        // Painel só até a última linha usada (o espaço do "..." não ocupado fica transparente).
        c.Panel(0, 0, w, Math.Min(h, Top + rows.Count * Pitch + gapCount * SepH + 2));
        if (_b10) DrawHeader10(c, t, m, w);
        float yShift = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (jumps[i]) { yShift += SepH; DrawSeparator(c, t, L, Top + i * Pitch + yShift - (SepH + Pitch - BoxH) / 2); }
            float y = Top + i * Pitch + yShift;
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
            if (_cfg.ColumnVisible("flag")) c.Flag(r.Car.Nationality, L.FlagX, y + 5, FlagW, BoxH - 10);
            if (_cfg.ColumnVisible("class")) DrawBadge(c, t, L.BadgeCx, y + BoxH / 2, (char)('A' + Math.Min(classes.IndexOf(r.Car.ClassName), 25)));
            if (_cfg.ColumnVisible("gap"))
            {
                // 1998-2001: o lider mostra "LAP" (rotulo) + numero (fonte de numeros), como na faixa da TV.
                if (t.Style == ThemeStyle.Broadcast98 && r.Car.Position == 1 && r.Car.CurrentLap > 0)
                {
                    string n = r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture);
                    float nw = c.Measure(n, t.Numbers) + t.Numbers.Tracking * n.Length;
                    c.Text(n, t.Numbers, L.GapRight - nw, y, nw + 4, BoxH, t.ValueColor, shadow: t.ValueShadow);
                    c.Text("LAP", t.Label, L.GapRight - nw - 70, y, 66, BoxH, t.ValueColor, HAlign.Right, t.TextShadow);
                }
                else c.Text(ListGap(r, t), t.Numbers, L.GapRight - 160, y, 160, BoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
            }
        }
    }

    /// <summary>Separador "..." entre o topo e a janela do jogador: três pontos na coluna da posição.</summary>
    void DrawSeparator(ThemeCanvas c, Theme.Theme t, Cols L, float cy)
    {
        float cx = _b04 ? X04 + Pos04 / 2 : L.PosX + BoxW / 2;
        var color = t.Style == ThemeStyle.Modern2010s ? t.LabelColor : t.Style == ThemeStyle.Broadcast98 ? t.NumberColor : t.TextColor;
        for (int k = -1; k <= 1; k++) c.FillEllipse(cx + k * 8, cy, 2.2f, 2.2f, color);
    }

    /// <summary>Gap da lista vertical. 1998-2001 (TV): sem sinal "+" e o lider mostra "LAP n" em amarelo; 2010s: lider "–".</summary>
    static string ListGap(StandingRow r, Theme.Theme t)
    {
        if (t.Style == ThemeStyle.Broadcast98)
            return r.Car.Position == 1 && r.Car.CurrentLap > 0 ? "LAP " + r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture) : FormatGap(r).TrimStart('+');
        if (t.Style == ThemeStyle.Modern2010s && r.Car.Position == 1) return "–";
        return FormatGap(r);
    }

    void DrawHeader10(ThemeCanvas c, Theme.Theme t, OverlayModel m, float w)
    {
        Chrome.Header(c, "RACE", 14, 6, w - 28 - c.Measure("RACE", t.Title) - 16);
        string lc = LapCounterWidget.Format(m);
        if (!lc.StartsWith("--")) c.Text("LAP " + lc.Replace("Lap ", "").Replace("/", " / "), t.Label, w - 160 - 14, 6, 160, 30, t.LabelColor, HAlign.Right);
    }

    // Tabela inferior 1998–2001 (faixa do GP do Brasil 2003): 2 colunas x N linhas, [caixa amarela][NOME][gap amarelo à direita]; o líder mostra "LAP n".
    const float TableX = 22, TableTop = 12, TableBottom = 8, TablePitch = 40, TableBoxW = 36, TableBoxH = 34, TableNameW = 246, TableGapW = 118, TableColGap = 44;
    /// <summary>Largura de uma coluna da tabela; sem a coluna de gap, só caixa + nome.</summary>
    float TableColW => TableBoxW + 14 + TableNameW + (_cfg.ColumnVisible("gap") ? TableGapW : 0);
    int TableRows => (Rows + 1) / 2;
    float TableWidth => TableX * 2 + TableColW * 2 + TableColGap;

    void DrawTable(ThemeCanvas c, Theme.Theme t, List<StandingRow> rows, OverlayModel m)
    {
        var field = m.Standings.Select(r => r.Car).ToList();
        int per = TableRows;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float x = TableX + (i / per) * (TableColW + TableColGap), y = TableTop + (i % per) * TablePitch;
            Chrome.AccentBox(c, x, y, TableBoxW, TableBoxH, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers);
            string name = BroadcastUi.ShortName(r.Car, field).ToUpperInvariant();
            c.Text(name, BroadcastUi.Fit(c, name, t.Text, TableNameW), x + TableBoxW + 14, y - 1, TableNameW, TableBoxH, r.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
            float right = x + TableColW;
            if (!_cfg.ColumnVisible("gap")) continue;
            if (r.Car.Position == 1 && r.Car.CurrentLap > 0)
            {
                string n = r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture);
                float nw = c.Measure(n, t.Numbers) + t.Numbers.Tracking * n.Length;
                c.Text(n, t.Numbers, right - nw, y, nw + 4, TableBoxH, t.ValueColor, shadow: t.ValueShadow);
                c.Text("LAP", t.Label, right - nw - 70, y, 66, TableBoxH, t.ValueColor, HAlign.Right, t.TextShadow);
            }
            else c.Text(FormatGap(r).TrimStart('+'), t.Numbers, right - 160, y, 160, TableBoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
    }

    // Mini-torre 2004-2008 (transmissao): [pos][sigla][bandeira][pneu][classe][gap], celulas coladas. O lider mostra "Lap N" em celula preta.
    const float X04 = 4, Pos04 = 40, Name04 = 82, Flag04 = 46, Tyre04 = 30, Class04 = 40, Gap04 = 112;
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
            if (_cfg.ColumnVisible("gap")) x += Gap04;
            return x + 6;
        }
    }

    /// <summary>Linha flutuante 2004–2008: caixa de posição (líder vermelho), sigla em célula branca, bandeira, pneu, selo de classe e célula preta, coladas.</summary>
    void DrawRow2000s(ThemeCanvas c, Theme.Theme t, StandingRow r, Cols L, float y, List<string> classes)
    {
        float h = BoxH, x = X04;
        if (_cfg.ColumnVisible("pos"))
        {
            // Bandeirada: o lider mostra a bandeira quadriculada no lugar do numero.
            if (r.Car.Position == 1 && r.Car.RaceState == RaceState.Finished) Chrome.Checkered(c, x, y, Pos04, h);
            else Chrome.PositionBox(c, x, y, Pos04, h, r.Car.Position, t.Numbers);
            x += Pos04;
        }
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
