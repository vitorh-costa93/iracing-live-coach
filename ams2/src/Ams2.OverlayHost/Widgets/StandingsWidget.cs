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
        : _b18 ? Size18 : (_b04 ? Width2004 : Layout().Width, Top + Rows * Pitch + SepReserve + 2);
    bool _b04, _b98, _b18;
    public void UseTheme(Theme.Theme theme) { _b04 = theme.Style == ThemeStyle.Broadcast2000s; _b98 = theme.Style == ThemeStyle.Broadcast98; _b18 = theme.Style == ThemeStyle.Modern2018; }
    float Top => RowTop;
    /// <summary>1998–2001 com a coluna "table": tabela inferior de 2 colunas (como a faixa do GP do Brasil 2003). Sem ela, a lista vertical.</summary>
    bool TableMode => _b98 && _cfg.ColumnVisible("table");
    float Pitch => _b04 ? RowPitch2000s : RowPitch;

    const float RowTop = 12, RowPitch = 43, RowPitch2000s = 36;
    const float BoxX = 19, BoxH = 34;
    const float BadgeW = 40, ColSpacing = 8, EdgeRight = 36;
    const float NameX = 75; // so para a mensagem de espera
    // Larguras das colunas (perfil: % da largura do tema; 100 % = mockup).
    float BoxW => MathF.Round(_cfg.Width("pos", 40));
    float NameCellW => MathF.Round(_cfg.Width("name", 104));
    float GapW => MathF.Round(_cfg.Width("gap", 170));
    float GapOnlyW => MathF.Round(_cfg.Width("gap", 125));
    /// <summary>Caixa de texto do gap: alinhada à direita, pelo menos 160 (textos longos como "+1:02.345" não cortam).</summary>
    float GapTextW => Math.Max(160, GapW);

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
        if (_b18) { DrawTower18(c, t, m, rows, jumps, classes); return; }
        var L = Layout();
        int gapCount = jumps.Count(j => j);
        // Painel só até a última linha usada (o espaço do "..." não ocupado fica transparente).
        c.Panel(0, 0, w, Math.Min(h, Top + rows.Count * Pitch + gapCount * SepH + 2));
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
                string nm = _cfg.Name(r.Car, RelativeWidget.Code(r.Car.Name));
                c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, NameCellW + 18), L.NameCellX + 8, y, NameCellW + 26, BoxH, ink, shadow: t.NameCellFill.A > 0f ? null : t.TextShadow);
            }
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
                else c.Text(ListGap(r, t), t.Numbers, L.GapRight - GapTextW, y, GapTextW, BoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
            }
        }
    }

    /// <summary>Separador "..." entre o topo e a janela do jogador: três pontos na coluna da posição.</summary>
    void DrawSeparator(ThemeCanvas c, Theme.Theme t, Cols L, float cy)
    {
        float cx = _b04 ? X04 + Pos04 / 2 : L.PosX + BoxW / 2;
        var color = t.Style == ThemeStyle.Modern2018 ? t.LabelColor : t.Style == ThemeStyle.Broadcast98 ? t.NumberColor : t.TextColor;
        for (int k = -1; k <= 1; k++) c.FillEllipse(cx + k * 8, cy, 2.2f, 2.2f, color);
    }

    /// <summary>Gap da lista vertical. 1998-2001 (TV): sem sinal "+" e o lider mostra "LAP n" em amarelo; 2010s: lider "–".</summary>
    string ListGap(StandingRow r, Theme.Theme t)
    {
        if (t.Style == ThemeStyle.Broadcast98)
            return r.Car.Position == 1 && r.Car.CurrentLap > 0 ? "LAP " + r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture) : Gap(r, defaultSign: false);
        if (t.Style == ThemeStyle.Modern2018 && r.Car.Position == 1) return "Leader";
        return Gap(r);
    }

    /// <summary>Gap ao líder no formato do perfil (casas, sinal, sufixo); voltas atrás "+1L".</summary>
    string Gap(StandingRow r, bool defaultSign = true)
    {
        var f = _cfg.Fmt;
        if (r.LapsBehind > 0) return f.FormatLaps(r.LapsBehind, defaultSign);
        return r.GapToLeader is { } g ? f.FormatGap(g, defaultSign) : f.NoGap;
    }

    // ---- Torre 2018–2021 (ref. f1-2018-tower-*.jpg): cabeçalho "LAP" + "n / N" com topo arredondado, filete vermelho, linhas
    // [caixa branca][SIGLA][classe no lugar do logo][coluna de gap mais clara]; marcador roxo de melhor volta à esquerda, fora do
    // painel; pilotos fora da corrida em bloco cinza no fim, sem caixa de posição. O líder mostra "Leader".
    const float M18 = 34, Head18W = 168, Head18H = 78, Rule18 = 4, Pad18 = 5, Pitch18 = 40, Box18 = 32, ClassW18 = 34;
    float Box18W => MathF.Round(_cfg.Width("pos", Box18));
    float Name18W => MathF.Round(_cfg.Width("name", 78));
    float Gap18W => MathF.Round(_cfg.Width("gap", 118));
    float Top18 => Head18H + Rule18 + Pad18;

    /// <summary>Colunas da torre 2018: x da caixa, do nome, da classe e início da coluna de gap; largura total da torre (sem a margem do marcador).</summary>
    (float Box, float Name, float Class, float Gap, float Width) Cols18()
    {
        float x = M18 + 6, box = x, name = 0, cls = 0, gap = 0;
        if (_cfg.ColumnVisible("pos")) x += Box18W + 10;
        if (_cfg.ColumnVisible("name")) { name = x; x += Name18W + 6; }
        if (_cfg.ColumnVisible("class")) { cls = x; x += ClassW18 + 6; }
        if (_cfg.ColumnVisible("gap")) { gap = x; x += Gap18W; } else x += 2;
        return (box, name, cls, gap, Math.Max(x - M18, Head18W));
    }

    (float, float) Size18 => (M18 + Cols18().Width, Top18 + Rows * Pitch18 + SepReserve + Pad18);

    static bool IsOut(StandingRow r) => r.Car.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;

    void DrawTower18(ThemeCanvas c, Theme.Theme t, OverlayModel m, List<StandingRow> rows, List<bool> jumps, List<string> classes)
    {
        var L = Cols18();
        float tw = L.Width, x0 = M18;
        int gapCount = jumps.Count(j => j);
        float bodyH = rows.Count * Pitch18 + gapCount * SepH + 2 * Pad18;
        // Cabeçalho: topo arredondado, "LAP" largo com tracking, filete fino, "n / N" regular.
        float hw = Head18W;
        c.FillRoundRect(x0, 0, hw, Head18H, 7, t.PanelFill);
        c.FillRect(x0, Head18H - 8, hw, 8, t.PanelFill);
        c.Text("LAP", t.Title with { Size = 26, Tracking = 4 }, x0, 4, hw + 4, 34, t.TitleColor, HAlign.Center);
        c.FillRect(x0 + 34, 40, hw - 68, 1.2f, new Vortice.Win32.Numerics.Color4(1f, 1f, 1f, 0.45f));
        string lc = LapCounterWidget.Format(m);
        lc = lc.StartsWith("--") ? "- / -" : lc.StartsWith("Lap ") ? lc[4..] : lc.Replace("/", " / ");
        c.Text(lc, t.Numbers with { Size = 26 }, x0, 42, hw, 32, t.ValueColor, HAlign.Center);
        // Filete vermelho e corpo.
        c.FillRect(x0, Head18H, tw, Rule18, t.AccentBar);
        float by = Head18H + Rule18;
        c.FillRect(x0, by, tw, bodyH, t.PanelFill);
        if (_cfg.ColumnVisible("gap")) c.FillRect(L.Gap, by, x0 + tw - L.Gap, bodyH, t.GapCellFill);

        double fastest = m.Standings.Where(r => r.Car.BestLapTime > 0).Select(r => r.Car.BestLapTime).DefaultIfEmpty(0).Min();
        float yShift = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (jumps[i])
            {
                yShift += SepH;
                float cy = Top18 + i * Pitch18 + yShift - (SepH + Pitch18 - Box18) / 2;
                for (int k = -1; k <= 1; k++) c.FillEllipse(L.Box + Box18W / 2 + k * 8, cy, 2.2f, 2.2f, t.LabelColor);
            }
            float y = Top18 + i * Pitch18 + yShift, rh = Box18;
            bool o = IsOut(r);
            if (o) c.FillRect(x0, y - (Pitch18 - Box18) / 2, tw, Pitch18, t.OutFill);
            if (fastest > 0 && !o && r.Car.BestLapTime == fastest) Chrome.FastestMarker(c, 0, y, Box18);
            if (_cfg.ColumnVisible("pos") && !o) Chrome.PosBox(c, L.Box, y, Box18W, rh, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 21 });
            if (_cfg.ColumnVisible("name"))
            {
                string nm = _cfg.Name(r.Car, RelativeWidget.Code(r.Car.Name));
                var ink = o ? t.OutInk : r.IsPlayer ? t.PlayerColor : t.TextColor;
                c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, Name18W + 4), L.Name, y, Name18W + 12, rh, ink);
            }
            if (_cfg.ColumnVisible("class"))
            {
                // No lugar do logo da equipe (o AMS2 não tem logos): letra da classe numa caixinha neutra.
                int ci = Math.Min(classes.IndexOf(r.Car.ClassName), 25);
                c.FillRoundRect(L.Class + 2, y + 3, ClassW18 - 4, rh - 6, 3, t.BadgeFill);
                c.Text(((char)('A' + ci)).ToString(), t.Label with { Weight = 700 }, L.Class + 2, y + 3, ClassW18 - 4, rh - 6, o ? t.OutInk : t.BadgeInk, HAlign.Center);
            }
            if (_cfg.ColumnVisible("gap"))
            {
                string g = o ? "OUT" : r.Car.Position == 1 ? "Leader" : Gap(r);
                c.Text(g, BroadcastUi.Fit(c, g, t.Numbers, Gap18W - 14), L.Gap, y, Gap18W - 10, rh, o ? t.OutInk : t.ValueColor, HAlign.Right);
            }
        }
    }

    // Tabela inferior 1998–2001 (faixa do GP do Brasil 2003): 2 colunas x N linhas, [caixa amarela][NOME][gap amarelo à direita]; o líder mostra "LAP n".
    const float TableX = 22, TableTop = 12, TableBottom = 8, TablePitch = 40, TableBoxH = 34, TableColGap = 44;
    float TableBoxW => MathF.Round(_cfg.Width("pos", 36));
    float TableNameW => MathF.Round(_cfg.Width("name", 246));
    float TableGapW => MathF.Round(_cfg.Width("gap", 118));
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
            string name = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
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
            else c.Text(Gap(r, defaultSign: false), t.Numbers, right - Math.Max(160, TableGapW), y, Math.Max(160, TableGapW), TableBoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
    }

    // Mini-torre 2004-2008 (transmissao): [pos][sigla][pneu][classe][gap], celulas coladas. O lider mostra "Lap N" em celula preta.
    const float X04 = 4, Tyre04 = 30, Class04 = 40;
    float Pos04 => MathF.Round(_cfg.Width("pos", 40));
    float Name04 => MathF.Round(_cfg.Width("name", 82));
    float Gap04 => MathF.Round(_cfg.Width("gap", 112));
    float Width2004
    {
        get
        {
            float x = X04;
            if (_cfg.ColumnVisible("pos")) x += Pos04;
            if (_cfg.ColumnVisible("name")) x += Name04;
            if (_cfg.ColumnVisible("tyre")) x += Tyre04;
            if (_cfg.ColumnVisible("class")) x += Class04;
            if (_cfg.ColumnVisible("gap")) x += Gap04;
            return x + 6;
        }
    }

    /// <summary>Linha flutuante 2004–2008: caixa de posição (líder vermelho), sigla em célula branca, pneu, selo de classe e célula preta, coladas.</summary>
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
        if (_cfg.ColumnVisible("name"))
        {
            string nm = _cfg.Name(r.Car, RelativeWidget.Code(r.Car.Name));
            Chrome.WhiteCell(c, x, y, Name04, h, nm, BroadcastUi.Fit(c, nm, t.Text, Name04 - 16), ink: r.IsPlayer ? Chrome.PlayerInk : null);
            x += Name04;
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
        if (_cfg.ColumnVisible("gap")) Chrome.BlackCell(c, x, y, Gap04, h, LeaderLap(r) ?? Gap(r), t.Numbers);
    }

    /// <summary>2004–2008: a célula do líder mostra a volta atual ("Lap 26"), como na transmissão.</summary>
    static string? LeaderLap(StandingRow r) => r.Car.Position == 1 && r.Car.CurrentLap > 0 ? "Lap " + r.Car.CurrentLap.ToString(CultureInfo.InvariantCulture) : null;

    static void DrawBadge(ThemeCanvas c, Theme.Theme t, float cx, float cy, char letter)
    {
        if (t.Style == ThemeStyle.Broadcast98) c.FillEllipse(cx, cy, 20, 20, t.BadgeFill);
        else c.FillRoundRect(cx - 20, cy - 14, 40, 28, t.BoxRadius, t.BadgeFill);
        c.Text(letter.ToString(), t.Text, cx - 20, cy - 17, 40, 34, t.BadgeInk, HAlign.Center);
    }

}
