using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Widget rotativo inferior ("board"): só desenha o <see cref="BoardState"/> do Core (ver ams2/reference/board-spec.md), sem recalcular nada.
/// Modos: LineTower (2 colunas x 4 linhas por página), SectorGap (barra de gap S1/S2/S3), LapComparison (tabela "Lap N / N-1 / N-2" com
/// deltas) e DriverPlate (legenda). O conteúdo fica centralizado na horizontal e encostado embaixo numa janela de tamanho fixo (o maior
/// modo do tema), então a janela nunca é redimensionada ao trocar de modo.
/// Transições: ao mudar de modo/página/janela, o conteúdo antigo some em ~0,25 s enquanto o novo entra com fade + slide curto; modos
/// "livres" (legenda, comparativo) só aparecem depois de ~0,25 s estáveis, para não piscar nos intervalos de uma fração de segundo.
/// Antes do fim previsto do modo (<c>RemainingSeconds</c> &lt; 0,4 s) o conteúdo escurece. Cada piloto da torre entra com slide/fade
/// quando cruza a linha (<c>CrossedT</c>), ou escalonado quando a página já estava preenchida.
/// </summary>
public sealed class BoardWidget : IWidget
{
    public string Id => "board";
    enum Style { S98, S04, S10 }
    Style _style = Style.S98;
    WidgetSettings _cfg = new() { Id = "board" };

    public void UseTheme(Theme.Theme theme) => _style = theme.Style switch { ThemeStyle.Broadcast2000s => Style.S04, ThemeStyle.Modern2010s => Style.S10, _ => Style.S98 };
    public void Configure(WidgetSettings s) => _cfg = s;

    /// <summary>Maior conteúdo entre os modos de cada tema (WidgetLayout.DesignSizes tem de bater: DesignSizeTests).</summary>
    public (float Width, float Height) DesignSize => _style switch { Style.S98 => (840, 196), Style.S04 => (590, 164), _ => (760, 210) };

    // ---- Animação (relógio do provider: m.Now) ----
    const double Dwell = 0.25, FadeInFree = 0.30, FadeInBusy = 0.20, FadeOut = 0.25;
    static readonly Color4 Orange = new(0.96f, 0.56f, 0.12f, 1f), GreenFill = new(0.12f, 0.64f, 0.22f, 1f);
    long _key = long.MinValue;
    double _since = double.NegativeInfinity;   // início da exibição atual (-inf no primeiro quadro: já assentado, p.ex. no --png)
    BoardState? _last, _out;
    double _outT;
    bool _lastShown;

    static float Ease(double x) { if (double.IsNaN(x) || x <= 0) return 0f; if (x >= 1) return 1f; double k = 1 - x; return (float)(1 - k * k * k); }
    static float RemainingFade(BoardState b) => double.IsInfinity(b.RemainingSeconds) ? 1f : (float)Math.Clamp(b.RemainingSeconds / 0.4, 0, 1);

    /// <summary>Chave da "tela": muda quando o conteúdo estrutural muda (modo, página da torre, nova janela de setor, volta do comparativo).</summary>
    static long KeyOf(BoardState b) => b.Mode switch
    {
        BoardMode.LineTower => 4_000_000 + (b.Tower?.PageIndex ?? 0),
        BoardMode.SectorGap => 3_000_000 + (long)Math.Round((b.SectorGap?.OpenT ?? 0) * 100),
        BoardMode.LapComparison => 2_000_000 + (b.LapComparison?.PlayerLapsCompleted ?? 0),
        BoardMode.DriverPlate => 1_000_000,
        _ => 0,
    };

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var b = m.Board ?? BoardState.Empty;
        double now = m.Now;
        long key = KeyOf(b);
        if (key != _key)
        {
            if (_key != long.MinValue && _last is { Mode: not BoardMode.None } && _lastShown) { _out = _last; _outT = now; }
            _key = key;
            _since = _last is null ? double.NegativeInfinity : now;
        }
        if (now < _since) _since = now; // relógio voltou

        if (_out is { } o)
        {
            double k = (now - _outT) / FadeOut;
            if (k < 0 || k >= 1) _out = null;
            else DrawState(c, o, o.Now, (1f - (float)k) * RemainingFade(o), -(float)k * 6f);
        }

        _lastShown = false;
        if (b.Mode != BoardMode.None)
        {
            double age = now - _since;
            bool free = b.Mode is BoardMode.DriverPlate or BoardMode.LapComparison;
            float fin = free ? (age < Dwell ? 0f : Ease((age - Dwell) / FadeInFree)) : Ease(age / FadeInBusy);
            float alpha = fin * RemainingFade(b);
            if (alpha > 0.01f)
            {
                DrawState(c, b, now, alpha, (1f - fin) * 10f);
                _lastShown = true;
            }
        }
        _last = b;
    }

    void DrawState(ThemeCanvas c, BoardState b, double now, float alpha, float slide)
    {
        if (alpha <= 0.01f) return;
        var (cw, ch) = ContentSize(b);
        if (cw <= 0) return;
        var (w, h) = DesignSize;
        float ox = MathF.Round((w - cw) / 2), oy = h - ch + slide;
        float prev = c.Opacity;
        c.Opacity = prev * alpha;
        try
        {
            switch (b.Mode)
            {
                case BoardMode.LineTower when b.Tower is { } tw: Tower(c, tw, now, ox, oy, cw, ch); break;
                case BoardMode.SectorGap when b.SectorGap is { } sg: Sector(c, sg, ox, oy, cw, ch); break;
                case BoardMode.LapComparison when b.LapComparison is { } lc: Laps(c, lc, ox, oy, cw, ch); break;
                case BoardMode.DriverPlate when b.Plate is { } p: Plate(c, p, ox, oy); break;
            }
        }
        finally { c.Opacity = prev; }
    }

    bool Flag => _cfg.ColumnVisible("flag");
    bool TyreCol => _cfg.ColumnVisible("tyre");
    bool PageCol => _cfg.ColumnVisible("page");

    // Torre: geometria por estilo.
    const float T98Edge = 20, T98ColW = 380, T98ColGap = 36, T98Box = 36, T98Pitch = 40, T98NameW = 220, T98GapW = 110;
    const float T04Pos = 38, T04Name = 84, T04Flag = 44, T04Gap = 96, T04Pitch = 33, T04H = 31, T04ColGap = 28;
    const float T10Edge = 20, T10ColW = 344, T10ColGap = 32, T10Box = 36, T10Pitch = 42, T10NameW = 196, T10GapW = 100;

    float T04ColW => T04Pos + T04Name + (Flag ? T04Flag : 0) + T04Gap;

    (float W, float H) ContentSize(BoardState b)
    {
        switch (b.Mode)
        {
            case BoardMode.LineTower:
            {
                bool ind = PageCol && (b.Tower?.PageCount ?? 0) > 1;
                return _style switch
                {
                    Style.S98 => (2 * T98Edge + 2 * T98ColW + T98ColGap, 12 + 4 * T98Pitch + (ind ? 24 : 8)),
                    Style.S04 => (2 * T04ColW + T04ColGap + 8, 4 * T04Pitch + (ind ? 30 : 0)),
                    _ => (2 * T10Edge + 2 * T10ColW + T10ColGap, 14 + 3 * T10Pitch + 34 + 14 + (ind ? 22 : 0)),
                };
            }
            case BoardMode.SectorGap: return _style switch { Style.S98 => (800, 108), Style.S04 => (552, 62), _ => (600, 96) };
            case BoardMode.LapComparison: return _style switch { Style.S98 => (770, 170), Style.S04 => (568, 118), _ => (620, 190) };
            case BoardMode.DriverPlate: return (334, 94);
            default: return (0, 0);
        }
    }

    static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ LineTower

    static float EntryAlpha(BoardTowerEntry e, BoardTower tw, double now)
    {
        double start = Math.Max(e.CrossedT, tw.PageStartT);
        double delay = e.CrossedT < tw.PageStartT ? 0.05 * (e.PageSlot - 1) : 0; // página já preenchida: escalona de cima para baixo
        return Ease((now - start - delay) / 0.28);
    }

    void Tower(ThemeCanvas c, BoardTower tw, double now, float ox, float oy, float cw, float ch)
    {
        var t = c.Theme;
        if (_style != Style.S04) c.Panel(ox, oy, cw, ch);
        foreach (var e in tw.Entries)
        {
            float ea = EntryAlpha(e, tw, now);
            if (ea <= 0.01f) continue;
            float dx = -(1f - ea) * 14f;
            float prev = c.Opacity;
            c.Opacity = prev * ea;
            try
            {
                switch (_style)
                {
                    case Style.S98: Row98(c, t, e, ox + T98Edge + e.Column * (T98ColW + T98ColGap) + dx, oy + 12 + e.Row * T98Pitch); break;
                    case Style.S04: Row04(c, t, e, ox + 4 + e.Column * (T04ColW + T04ColGap) + dx, oy + e.Row * T04Pitch); break;
                    default: Row10(c, t, e, ox + T10Edge + e.Column * (T10ColW + T10ColGap) + dx, oy + 14 + e.Row * T10Pitch); break;
                }
            }
            finally { c.Opacity = prev; }
        }
        if (PageCol && tw.PageCount > 1)
        {
            string txt = Num(tw.PageIndex + 1) + "/" + Num(tw.PageCount);
            switch (_style)
            {
                case Style.S98: c.Text(txt, t.Label with { Size = 17 }, ox + cw - T98Edge - 70, oy + ch - 24, 70, 20, t.LabelColor, HAlign.Right); break;
                case Style.S04: { float w = c.Measure(txt, t.Label with { Size = 16 }) + 18; Chrome.Caption(c, ox + cw - 4 - w, oy + 4 * T04Pitch + 4, txt, 24, t.Label with { Size = 16 }, Chrome.CellKind.Navy); break; }
                default: c.Text(txt, t.Label with { Size = 18 }, ox + cw - T10Edge - 70, oy + ch - 14 - 22, 70, 20, t.LabelColor, HAlign.Right); break;
            }
        }
    }

    void Row98(ThemeCanvas c, Theme.Theme t, BoardTowerEntry e, float x, float y)
    {
        if (e.IsPlayer) c.FillRect(x - 8, y - 3, T98ColW + 16, T98Pitch, new Color4(1, 1, 1, 0.10f));
        Chrome.AccentBox(c, x, y, T98Box, 34, Num(e.Position), t.Numbers);
        float nx = x + T98Box + 14, nameW = Flag ? T98NameW - 48 : T98NameW;
        string name = e.ShortName.ToUpperInvariant();
        c.Text(name, BroadcastUi.Fit(c, name, t.Text, nameW), nx, y - 1, nameW + 8, 34, e.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
        if (Flag) c.Flag(e.Nationality, nx + T98NameW - 40, y + 5, 40, 24);
        float right = x + T98ColW;
        if (e.GapKind == BoardGapKind.Leader)
        {
            string n = Num(e.GapLaps);
            float nw = c.Measure(n, t.Numbers) + t.Numbers.Tracking * n.Length;
            c.Text(n, t.Numbers, right - nw, y, nw + 4, 34, t.ValueColor, shadow: t.ValueShadow);
            c.Text("LAP", t.Label, right - nw - 70, y, 66, 34, t.ValueColor, HAlign.Right, t.TextShadow);
        }
        else if (e.GapKind == BoardGapKind.Laps) c.Text(e.GapText, t.Label, right - T98GapW - 40, y, T98GapW + 40, 34, t.ValueColor, HAlign.Right, t.TextShadow);
        else c.Text(e.GapText.TrimStart('+'), t.Numbers, right - 160, y, 160, 34, t.ValueColor, HAlign.Right, t.ValueShadow);
    }

    void Row04(ThemeCanvas c, Theme.Theme t, BoardTowerEntry e, float x, float y)
    {
        Chrome.PositionBox(c, x, y, T04Pos, T04H, e.Position, t.Numbers);
        x += T04Pos;
        Chrome.WhiteCell(c, x, y, T04Name, T04H, e.Code, t.Text, ink: e.IsPlayer ? Chrome.PlayerInk : null);
        x += T04Name;
        if (Flag)
        {
            Chrome.Box(c, x, y, T04Flag, T04H, "", t.Text, Chrome.CellKind.White);
            c.Flag(e.Nationality, x + 8, y + 4, T04Flag - 16, T04H - 8);
            x += T04Flag;
        }
        Chrome.BlackCell(c, x, y, T04Gap, T04H, e.GapText, t.Numbers);
    }

    void Row10(ThemeCanvas c, Theme.Theme t, BoardTowerEntry e, float x, float y)
    {
        if (e.IsPlayer) c.FillRoundRect(x - 8, y - 4, T10ColW + 16, 42, 6, new Color4(1, 1, 1, 0.10f));
        Chrome.AccentBox(c, x, y, T10Box, 34, Num(e.Position), t.Numbers);
        float nx = x + T10Box + 12, nameW = Flag ? T10NameW - 46 : T10NameW;
        string name = e.ShortName.ToUpperInvariant();
        c.Text(name, BroadcastUi.Fit(c, name, t.Text, nameW), nx, y - 1, nameW + 6, 34, e.IsPlayer ? t.PlayerColor : t.TextColor);
        if (Flag) c.Flag(e.Nationality, nx + T10NameW - 36, y + 5, 36, 24);
        string gap = e.GapKind == BoardGapKind.Leader ? e.GapText.ToUpperInvariant() : e.GapText;
        c.Text(gap, t.Numbers, x + T10ColW - 140, y - 1, 140, 34, t.AccentFill, HAlign.Right);
    }

    // ------------------------------------------------------------------ SectorGap

    void Sector(ThemeCanvas c, BoardSectorGap sg, float ox, float oy, float cw, float ch)
    {
        var t = c.Theme;
        var left = sg.NeighborAhead ? sg.Neighbor : sg.Player;    // quem está à frente fica à esquerda (como na barra da TV)
        var right = sg.NeighborAhead ? sg.Player : sg.Neighbor;
        string label = "S" + Num(sg.Sector);
        float live = sg.IsSplit ? 1f : 0.62f;                     // ao vivo: apagado; split exato: destaque
        switch (_style)
        {
            case Style.S98:
            {
                const float edge = 20, box = 56, barW = 160, top = 30, bandH = 78;
                float w = cw, bt = oy + top;
                c.Panel(ox, bt, w, bandH);
                Chrome.BlackTag(c, ox + w - 110, oy, 110, top - 2, label, t.Label with { Size = 21 });
                float by = bt + 10, nameW = w / 2 - edge - box - 14 - 120;
                var big = t.Numbers with { Size = 46 };
                Chrome.AccentBox(c, ox + edge, by, box, 58, Num(left.Position), big);
                float lx = ox + edge + box + 14;
                Chrome.SplitBar(c, lx, by + 4, barW, 12, false);
                string ln = left.ShortName.ToUpperInvariant();
                c.Text(ln, BroadcastUi.Fit(c, ln, t.Text, nameW), lx, by + 18, nameW + 10, 38, left.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
                Chrome.AccentBox(c, ox + w - edge - box, by, box, 58, Num(right.Position), big);
                float rx = ox + w - edge - box - 14;
                Chrome.SplitBar(c, rx - barW, by + 4, barW, 12, true);
                string rn = right.ShortName.ToUpperInvariant();
                c.Text(rn, BroadcastUi.Fit(c, rn, t.Text, nameW), rx - nameW - 10, by + 18, nameW + 10, 38, right.IsPlayer ? t.PlayerColor : t.TextColor, HAlign.Right, t.TextShadow);
                float prev = c.Opacity; c.Opacity = prev * live;
                c.Text(sg.GapText.TrimStart('+', '-'), t.Numbers with { Size = 44 }, ox + w / 2 - 110, by + 6, 220, 46, t.ValueColor, HAlign.Center, t.ValueShadow);
                c.Opacity = prev;
                break;
            }
            case Style.S04:
            {
                const float posW = 34, nameW = 176, gapW = 118, h = 29;
                float x = ox + 4;
                Chrome.Caption(c, x, oy + 2, label, 26, t.Label, ink: Chrome.HeaderTeal);
                float y = oy + 32;
                var nf = t.Text with { Size = 22 };
                Chrome.PositionBox(c, x, y, posW, h, left.Position, t.Numbers);
                Chrome.WhiteCell(c, x + posW, y, nameW, h, left.ShortName, BroadcastUi.Fit(c, left.ShortName, nf, nameW - 16), HAlign.Right, left.IsPlayer ? Chrome.PlayerInk : null);
                var kind = !sg.IsSplit ? Chrome.CellKind.Black : sg.NeighborAhead ? Chrome.CellKind.Orange : Chrome.CellKind.Green;
                Chrome.BlackCell(c, x + posW + nameW, y, gapW, h, sg.GapText, t.Numbers with { Size = 22 }, HAlign.Center, ink: sg.IsSplit ? null : new Color4(0.78f, 0.78f, 0.80f, 1f), kind: kind);
                Chrome.WhiteCell(c, x + posW + nameW + gapW, y, nameW, h, right.ShortName, BroadcastUi.Fit(c, right.ShortName, nf, nameW - 16), HAlign.Left, right.IsPlayer ? Chrome.PlayerInk : null);
                Chrome.PositionBox(c, x + posW + 2 * nameW + gapW, y, posW, h, right.Position, t.Numbers);
                break;
            }
            default:
            {
                float w = cw;
                c.Panel(ox, oy, w, ch);
                Chrome.Header(c, "SECTOR " + Num(sg.Sector), ox + 14, oy + 6, w - 28 - c.Measure("SECTOR 3", t.Title) - 16);
                float y = oy + 46;
                Chrome.AccentBox(c, ox + 16, y, 40, 38, Num(left.Position), t.Numbers);
                string ln = left.ShortName.ToUpperInvariant(), rn = right.ShortName.ToUpperInvariant();
                c.Text(ln, BroadcastUi.Fit(c, ln, t.Text, 190), ox + 68, y - 1, 200, 38, left.IsPlayer ? t.PlayerColor : t.TextColor);
                Chrome.AccentBox(c, ox + w - 16 - 40, y, 40, 38, Num(right.Position), t.Numbers);
                c.Text(rn, BroadcastUi.Fit(c, rn, t.Text, 190), ox + w - 68 - 200, y - 1, 200, 38, right.IsPlayer ? t.PlayerColor : t.TextColor, HAlign.Right);
                float prev = c.Opacity; c.Opacity = prev * live;
                c.Text(sg.GapText, t.Numbers with { Size = 38 }, ox + w / 2 - 80, y - 2, 160, 40, t.AccentFill, HAlign.Center);
                c.Opacity = prev;
                break;
            }
        }
    }

    // ------------------------------------------------------------------ LapComparison

    static string LapTime(double? s) => s is { } v && v > 0 ? BroadcastUi.RaceTime(v) : "--:--.---";
    static string Delta(double? d) => d is { } v ? BoardText.Gap(v) : "--.---";

    void Laps(ThemeCanvas c, BoardLapComparison lc, float ox, float oy, float cw, float ch)
    {
        var t = c.Theme;
        var me = lc.Player; var nb = lc.Neighbor;
        var rows = lc.Laps;
        switch (_style)
        {
            case Style.S98:
            {
                c.Panel(ox, oy, cw, ch);
                const float edge = 20, box = 56, timeW = 160;
                float cy = oy + ch / 2;
                var big = t.Numbers with { Size = 46 };
                Chrome.AccentBox(c, ox + edge, cy - 30, box, 60, Num(me.Position), big);
                Chrome.AccentBox(c, ox + cw - edge - box, cy - 30, box, 60, Num(nb.Position), big);
                float lx = ox + 92, rx = ox + cw - 92 - timeW, cx = ox + cw / 2;
                string ln = me.ShortName.ToUpperInvariant(), rn = nb.ShortName.ToUpperInvariant();
                c.Text(ln, BroadcastUi.Fit(c, ln, t.Text, timeW + 20), lx, oy + 6, timeW + 30, 36, me.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
                c.Text(rn, BroadcastUi.Fit(c, rn, t.Text, timeW + 20), rx - 30, oy + 6, timeW + 30, 36, nb.IsPlayer ? t.PlayerColor : t.TextColor, HAlign.Right, t.TextShadow);
                var tf = t.Numbers with { Size = 30 };
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    float y = oy + 46 + i * 38;
                    c.Text(LapTime(r.PlayerTime), BroadcastUi.Fit(c, LapTime(r.PlayerTime), tf, timeW), lx, y, timeW + 8, 34, t.ValueColor, shadow: t.ValueShadow);
                    c.Text(LapTime(r.NeighborTime), BroadcastUi.Fit(c, LapTime(r.NeighborTime), tf, timeW), rx, y, timeW + 8, 34, t.ValueColor, shadow: t.ValueShadow);
                    c.Text("LAP " + Num(r.Lap), t.Label, cx - 112, y, 110, 34, t.TextColor, HAlign.Right, t.TextShadow);
                    if (r.Delta is { } d)
                        c.Text(BoardText.Gap(d), t.Numbers with { Size = 26 }, cx + 12, y, 130, 34, r.PlayerFaster ? t.ThrottleColor : Orange, shadow: t.ValueShadow);
                }
                break;
            }
            case Style.S04:
            {
                const float posW = 34, nameW = 214, timeW = 104, h = 29, pitch = 30;
                float x = ox + 4, tx = x + posW + nameW, y0 = oy;
                var nf = t.Text with { Size = 22 };
                for (int i = 0; i < rows.Count; i++)
                    Chrome.Box(c, tx + i * timeW, y0, timeW, 26, "Lap " + Num(rows[i].Lap), t.Label, Chrome.CellKind.Navy, HAlign.Center, 0);
                float y1 = y0 + 28, y2 = y1 + pitch, y3 = y2 + pitch;
                Chrome.PositionBox(c, x, y1, posW, h, me.Position, t.Numbers);
                Chrome.WhiteCell(c, x + posW, y1, nameW, h, me.ShortName, BroadcastUi.Fit(c, me.ShortName, nf, nameW - 16), ink: me.IsPlayer ? Chrome.PlayerInk : null);
                Chrome.PositionBox(c, x, y2, posW, h, nb.Position, t.Numbers);
                Chrome.WhiteCell(c, x + posW, y2, nameW, h, nb.ShortName, BroadcastUi.Fit(c, nb.ShortName, nf, nameW - 16), ink: nb.IsPlayer ? Chrome.PlayerInk : null);
                Chrome.Box(c, x, y3, posW + nameW, h, "Delta", t.Label, Chrome.CellKind.Navy, HAlign.Left, 10);
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    float cx = tx + i * timeW;
                    var tfont = t.Numbers with { Size = 22 };
                    Chrome.BlackCell(c, cx, y1, timeW, h, LapTime(r.PlayerTime), tfont, HAlign.Center);
                    Chrome.BlackCell(c, cx, y2, timeW, h, LapTime(r.NeighborTime), tfont, HAlign.Center);
                    var kind = r.Delta is null ? Chrome.CellKind.Black : r.PlayerFaster ? Chrome.CellKind.Green : Chrome.CellKind.Orange;
                    Chrome.BlackCell(c, cx, y3, timeW, h, Delta(r.Delta), tfont, HAlign.Center, kind: kind);
                }
                break;
            }
            default:
            {
                c.Panel(ox, oy, cw, ch);
                Chrome.Header(c, "LAP TIMES", ox + 14, oy + 6, cw - 28 - c.Measure("LAP TIMES", t.Title) - 16);
                const float timeW = 110, h = 30, pitch = 36;
                float tx = ox + cw - 16 - 3 * timeW;
                for (int i = 0; i < rows.Count; i++)
                    c.Text("LAP " + Num(rows[i].Lap), t.Label, tx + i * timeW, oy + 44, timeW - 6, 22, t.LabelColor, HAlign.Right);
                float y1 = oy + 70, y2 = y1 + pitch, y3 = y2 + pitch;
                Chrome.AccentBox(c, ox + 16, y1, 34, h, Num(me.Position), t.Numbers);
                Chrome.AccentBox(c, ox + 16, y2, 34, h, Num(nb.Position), t.Numbers);
                c.Text(me.ShortName.ToUpperInvariant(), t.Text, ox + 60, y1 - 1, 190, h, me.IsPlayer ? t.PlayerColor : t.TextColor);
                c.Text(nb.ShortName.ToUpperInvariant(), t.Text, ox + 60, y2 - 1, 190, h, nb.IsPlayer ? t.PlayerColor : t.TextColor);
                c.Text("DELTA", t.Label, ox + 60, y3 - 1, 190, h, t.LabelColor);
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    float cx = tx + i * timeW;
                    c.Text(LapTime(r.PlayerTime), t.Numbers, cx, y1 - 1, timeW - 6, h, t.ValueColor, HAlign.Right);
                    c.Text(LapTime(r.NeighborTime), t.Numbers, cx, y2 - 1, timeW - 6, h, t.ValueColor, HAlign.Right);
                    if (r.Delta is null) { c.Text("--.---", t.Numbers, cx, y3 - 1, timeW - 6, h, t.LabelColor, HAlign.Right); continue; }
                    c.FillRoundRect(cx + 6, y3, timeW - 6, h, 4, r.PlayerFaster ? GreenFill : Orange);
                    c.Text(Delta(r.Delta), t.Numbers, cx + 6, y3 - 1, timeW - 6 - 8, h, new Color4(1, 1, 1, 1), HAlign.Right);
                }
                break;
            }
        }
    }

    // ------------------------------------------------------------------ DriverPlate

    static string CarNumber(BoardDriver d) => Num(d.CarIndex + 1);

    void Plate(ThemeCanvas c, BoardDriver d, float ox, float oy)
    {
        var t = c.Theme;
        const float w = 334, h = 94;
        string name = d.ShortName, team = d.Team;
        switch (_style)
        {
            case Style.S04:
            {
                const float x0 = 4, y0 = 4, leftW = 230, headH = 26, rowH = 30;
                float x = ox + x0, y = oy + y0;
                Chrome.HeaderCell(c, x, y, leftW, headH, "DRIVER", t.Label);
                Chrome.WhiteCell(c, x, y + headH, leftW, rowH, name, BroadcastUi.Fit(c, name, t.Text, leftW - 16), ink: null);
                Chrome.Box(c, x, y + headH + rowH, leftW, rowH, team, BroadcastUi.Fit(c, team, t.Text, leftW - 16), Chrome.CellKind.Navy);
                float rx = x + leftW;
                var posKind = d.Position == 1 ? Chrome.CellKind.Red : Chrome.CellKind.Navy;
                Chrome.Box(c, rx, y + headH, 40, rowH, "", t.Text, posKind);
                if (Flag) c.Flag(d.Nationality, rx + 6, y + headH + 5, 28, rowH - 10);
                if (!(TyreCol && Chrome.TyreBox(c, rx, y + headH + rowH, 40, rowH, d.TyreSupplier, t.Text with { Size = 22 })))
                    Chrome.Box(c, rx, y + headH + rowH, 40, rowH, "", t.Text, Chrome.CellKind.Navy);
                Chrome.Box(c, rx + 40, y + headH, 56, rowH * 2, Num(d.Position), t.Numbers with { Size = 38 }, posKind, HAlign.Center, 0);
                break;
            }
            case Style.S10:
            {
                c.Panel(ox, oy, w, h);
                Chrome.AccentBox(c, ox + 16, oy + 16, 58, h - 32, Num(d.Position), t.Numbers with { Size = 36 });
                c.Text(name, BroadcastUi.Fit(c, name, t.Text, 165), ox + 90, oy + 14, 170, 34, t.TextColor);
                c.Text(team, BroadcastUi.Fit(c, team, t.Label, 165), ox + 90, oy + 48, 170, 30, t.LabelColor);
                if (Flag) c.Flag(d.Nationality, ox + w - 56, oy + 16, 40, 26);
                if (TyreCol && d.TyreSupplier.Length > 0) c.Text(d.TyreSupplier, t.Label, ox + w - 56, oy + 48, 40, 30, t.ValueColor, HAlign.Center);
                break;
            }
            default:
            {
                const float bandTop = 26, tagH = 24;
                c.Panel(ox, oy + bandTop, w, h - bandTop);
                float tagRight = ox + w - 4;
                c.FillRect(tagRight - 150, oy + bandTop - tagH, 150, tagH, new Color4(21 / 255f, 150 / 255f, 176 / 255f, 0.97f));
                c.Text("DRIVER", t.Label with { Size = 20 }, tagRight - 150, oy + bandTop - tagH - 1, 150, tagH, new Color4(1, 1, 1, 1), HAlign.Center, t.TextShadow);
                float x = ox + 16, y1 = oy + bandTop + 8, y2 = oy + bandTop + 38;
                float maxW = Flag ? w - 16 - 62 - 62 : w - 16 - 62 - 14;
                Chrome.Bubble(c, x, y1, 50, 28, CarNumber(d), t.Numbers with { Size = 24, Tracking = 1f });
                string nm = name.ToUpperInvariant(), tm = team.ToUpperInvariant();
                c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, maxW), x + 62, y1 - 1, maxW + 8, 30, t.TextColor, shadow: t.TextShadow);
                if (TyreCol) Chrome.TyreEmblem(c, x + 25, y2 + 14, 12, d.TyreSupplier, t.Text with { Size = 17 });
                c.Text(tm, BroadcastUi.Fit(c, tm, t.Label, maxW), x + 62, y2, maxW + 8, 28, t.LabelColor, shadow: t.TextShadow);
                if (Flag) c.Flag(d.Nationality, ox + w - 54, oy + bandTop + 12, 40, 26);
                break;
            }
        }
    }
}
