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
    public void Configure(WidgetSettings s) { _cfg = s; _valueHold18.Reset(); _rowMotion18.Reset(); }
    /// <summary>Espaço reservado para o separador "..." (só existe se há topo; o painel só o ocupa quando há salto).</summary>
    float SepReserve => _cfg.EffectiveTop > 0 ? SepH : 0;
    const float SepH = 14;
    public (float Width, float Height) DesignSize => TableMode ? (TableWidth, TableTop + TableRows * TablePitch + TableBottom)
        : _b18 ? Size18 : (_b04 ? Width2004 : Layout().Width, Top + Rows * Pitch + SepReserve + 2);
    bool _b04, _b98, _b18;
    public void UseTheme(Theme.Theme theme)
    {
        bool modern = theme.Style == ThemeStyle.Modern2018;
        if (modern != _b18) { _valueHold18.Reset(); _values18.Clear(); _rowMotion18.Reset(); }
        _b04 = theme.Style == ThemeStyle.Broadcast2000s; _b98 = theme.Style == ThemeStyle.Broadcast98; _b18 = modern;
    }
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

    readonly FieldCodes _codes = new();
    readonly Broadcast18ValueHold _valueHold18 = new();
    readonly Broadcast18RowMotion _rowMotion18 = new();
    readonly Dictionary<(int Car, string Mode), double?> _values18 = [];

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        _codes.Update(m.Standings.Select(r => r.Car));
        var t = c.Theme;
        var (w, h) = DesignSize;
        if (!m.Connected || m.Standings.Count == 0)
        {
            _valueHold18.Reset(); _values18.Clear(); _rowMotion18.Reset();
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
        if (_b18) { DrawTower18(c, t, m); return; }
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
                Chrome.AccentBox(c, L.PosX, y, BoxW, BoxH, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position" });
            }
            if (_cfg.ColumnVisible("name"))
            {
                var ink = Chrome.NameCell(c, L.NameCellX, y, NameCellW, BoxH, r.IsPlayer ? t.PlayerColor : t.TextColor);
                string nm = _cfg.Name(r.Car, _codes);
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
                else c.Text(ListGap(r, t), t.Numbers with { Element = "gap" }, L.GapRight - GapTextW, y, GapTextW, BoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
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

    // ---- Torre 2018–2021 (ref. f1-2018-tower-*.jpg, f1-2018-lap-gainedlost.jpg, f1-2018-tower-yellowflag.jpg, f1-2018-crops-pitmap-winner-finishtower.jpg):
    // cabeçalho "LAP" + "n / N" com topo arredondado, filete vermelho, título do modo (GAINED/LOST, PIT STOPS, BEST LAP), linhas
    // [caixa branca][SIGLA][classe no lugar do logo][coluna clara]; marcador roxo de melhor volta à esquerda, fora do painel.
    // Estados: bandeira amarela (cabeçalho "YELLOW FLAG" + faixa amarela "SECTOR n", nomes completos), bandeirada (cabeçalho xadrez,
    // 3 primeiros em faixas altas com caixa roxa no 1º, mini bandeira xadrez à esquerda de quem recebeu a bandeirada), IN PIT / PIT EXIT
    // ciano no lugar do valor, bloco "BATTLE FOR Nth" (jogador a < 1 s de alguém) e bloco cinza "OUT" no fim. Opções do tema (Control
    // Center): mode, modeSeconds, battle, fullNames, outBlock (WidgetCatalog.OptionsFor("f1-2018", "standings")).
    // Safety Car: o $pcars2$ não expõe (HighestFlagColour não distingue SC de amarela), então não há cabeçalho "SAFETY CAR".
    const float M18 = 34, Head18W = 168, Head18H = 78, Rule18 = 4, Pad18 = 5, Pitch18 = 40, Box18 = 32, ClassW18 = 34;
    const float Title18H = 32, BattleTitleH = 30, BattlePitch = 44, FinishSub = 26, OutGap18 = 4;
    const int OutMax18 = 3;
    const double BattleSeconds = 1.0;
    const uint FlagYellow = 6, FlagDoubleYellow = 7;   // FLAG_COLOUR_YELLOW / DOUBLE_YELLOW do SharedMemory.h
    float Box18W => MathF.Round(_cfg.Width("pos", Box18));
    float Name18W => MathF.Round(_cfg.Width("name", 78));
    float Gap18W => MathF.Round(_cfg.Width("gap", 118));
    float Top18 => Head18H + Rule18 + Pad18;

    /// <summary>Modos da coluna clara; "auto" alterna entre eles (gained/lost só quando há grid capturado).</summary>
    public static readonly string[] Modes18 = ["gap", "interval", "gainedlost", "pitstops", "bestlap"];
    string Mode18 => _cfg.OptionOr("mode", "gap").ToLowerInvariant();
    double ModeSeconds18 => double.TryParse(_cfg.OptionOr("modeSeconds", "10"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 5, 30) : 10;
    double IntervalSeconds18 => double.TryParse(_cfg.OptionOr("intervalSeconds", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Broadcast18ValueHold.Period(v) : 1;
    bool Battle18 => !string.Equals(_cfg.OptionOr("battle", "true"), "false", StringComparison.OrdinalIgnoreCase);
    bool FullNames18 => !string.Equals(_cfg.OptionOr("fullNames", "true"), "false", StringComparison.OrdinalIgnoreCase);
    bool OutBlock18 => !string.Equals(_cfg.OptionOr("outBlock", "true"), "false", StringComparison.OrdinalIgnoreCase);
    /// <summary>Modos com título sob o filete vermelho (o espaço do título fica reservado também no automático).</summary>
    static bool HasTitle18(string mode) => mode is "gainedlost" or "pitstops" or "bestlap" or "auto";

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

    /// <summary>Altura fixa da janela: cabeçalho + o maior entre (título do modo + bloco BATTLE) e (faixas altas da bandeirada) + linhas
    /// + "..." + bloco OUT. O que não estiver em uso fica transparente.</summary>
    (float, float) Size18
    {
        get
        {
            float extra = Math.Max((HasTitle18(Mode18) ? Title18H : 0) + (Battle18 ? BattleTitleH + 2 * (BattlePitch - Pitch18) + 4 : 0), 3 * FinishSub);
            float outH = OutBlock18 ? OutGap18 + OutMax18 * Pitch18 : 0;
            return (M18 + Cols18().Width, Top18 + extra + Rows * Pitch18 + SepReserve + Pad18 + outH);
        }
    }

    static bool IsOut(StandingRow r) => r.Car.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;

    void SampleValues18(OverlayModel m, IReadOnlyList<StandingRow> active, string mode)
    {
        var s = m.Session!;
        // Sorted identity preserves holds during position changes, but replacing a driver resets the roster.
        string roster = string.Join("|", s.Cars.OrderBy(c => c.Index).Select(c => $"{c.Index}:{c.Name}:{c.CarName}"));
        _valueHold18.BeginFrame(m.Now, (s.Kind, s.Track, s.TrackVariation, s.TrackLength, s.LapsInEvent,
            s.InSession, m.Broadcast?.SessionSeenT, roster, mode, IntervalSeconds18));
        _values18.Clear();
        var leader = m.Standings[0];
        for (int i = 0; i < active.Count; i++)
        {
            var r = active[i];
            foreach (string channel in new[] { "gap", "interval" })
            {
                var reference = channel == "gap" ? leader : active[Math.Max(0, i - 1)];
                var key = new Broadcast18ValueKey(r.Car.Index, reference.Car.Index, channel,
                    r.Car.Position, r.LapsBehind, reference.LapsBehind, r.Car.PitState, r.Car.RaceState,
                    reference.Car.PitState, reference.Car.RaceState);
                double? raw = channel == "gap" ? r.GapToLeader : IntervalAhead(active, r);
                if (InPit18(r) || r.LapsBehind != reference.LapsBehind) raw = null;
                _values18[(r.Car.Index, channel)] = _valueHold18.Sample(key, raw, m.Now, IntervalSeconds18);
            }
        }
        // OUT rows invalidate immediately, even if they return to the active roster in the next frame.
        foreach (var r in m.Standings.Where(IsOut))
            foreach (string channel in new[] { "gap", "interval" })
                _valueHold18.Sample(new Broadcast18ValueKey(r.Car.Index, -1, channel, 0, 0, 0,
                    r.Car.PitState, r.Car.RaceState, PitState.None, RaceState.Invalid), null, m.Now);
    }

    string HeldText18(IReadOnlyList<StandingRow> active, StandingRow r, string channel)
    {
        var f = _cfg.Fmt;
        if (r.Car.Position == 1) return channel == "interval" ? "Interval" : "Leader";
        int laps = r.LapsBehind;
        if (channel == "interval")
        {
            int i = -1;
            for (int k = 0; k < active.Count; k++) if (active[k].Car.Index == r.Car.Index) { i = k; break; }
            laps = i > 0 ? Math.Max(0, laps - active[i - 1].LapsBehind) : 0;
        }
        if (laps > 0) return f.FormatLaps(laps);
        return _values18.GetValueOrDefault((r.Car.Index, channel)) is { } value ? f.FormatGap(value) : f.NoGap;
    }

    /// <summary>Modo efetivo agora: o configurado, ou no automático o da vez (troca a cada <c>modeSeconds</c> do relógio do provider).</summary>
    public static string EffectiveMode18(string mode, double now, double seconds, bool hasGrid)
    {
        if (mode != "auto") return Array.IndexOf(Modes18, mode) >= 0 ? mode : "gap";
        var list = hasGrid ? Modes18 : Modes18.Where(x => x != "gainedlost").ToArray();
        long k = (long)Math.Floor(Math.Max(0, now) / Math.Max(1, seconds));
        return list[(int)(k % list.Length)];
    }

    static string Ordinal(int n)
    {
        string suf = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suf;
    }

    /// <summary>Intervalo (s) de <paramref name="r"/> para o carro imediatamente à frente na classificação; null sem dado ou com volta de diferença.</summary>
    static double? IntervalAhead(IReadOnlyList<StandingRow> all, StandingRow r)
    {
        int i = -1;
        for (int k = 0; k < all.Count; k++) if (all[k].Car.Index == r.Car.Index) { i = k; break; }
        if (i <= 0) return null;
        var a = all[i - 1];
        if (r.LapsBehind != a.LapsBehind) return null;
        double ga = a.Car.Position == 1 ? 0 : a.GapToLeader ?? double.NaN, gr = r.GapToLeader ?? double.NaN;
        return double.IsNaN(ga) || double.IsNaN(gr) ? null : Math.Max(0, gr - ga);
    }

    /// <summary>Sobrenome em caixa alta (nomes completos da TV), respeitando o formato de nome do perfil quando configurado.</summary>
    string FullName18(StandingRow r, IReadOnlyList<CarSnapshot> field) => _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();

    enum Head18 { Normal, Yellow, Finish }

    void DrawTower18(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        var L = Cols18();
        float tw = L.Width, x0 = M18;
        var s = m.Session!;
        var all = m.Standings;
        var field = all.Select(r => r.Car).ToList();
        var classes = all.Select(r => r.Car.ClassName).Distinct().ToList();
        bool finished = s.Kind == SessionKind.Race && all[0].Car.RaceState == RaceState.Finished;
        var head = finished ? Head18.Finish : s.FlagColour is FlagYellow or FlagDoubleYellow ? Head18.Yellow : Head18.Normal;
        bool hasGrid = m.Grid is { Count: > 0 };
        string mode = EffectiveMode18(Mode18, m.Now, ModeSeconds18, hasGrid);
        bool gapCol = _cfg.ColumnVisible("gap");
        bool fullNames = head == Head18.Yellow && FullNames18;

        // Linhas: os pilotos fora da corrida saem da seleção; com outBlock vão para o bloco cinza do fim (até OutMax18), senão somem.
        var active = all.Where(r => !IsOut(r)).ToList();
        SampleValues18(m, active, mode);
        var outs = OutBlock18 ? all.Where(IsOut).Take(OutMax18).ToList() : [];
        int me = active.FindIndex(r => r.IsPlayer);
        var picks = StandingsSelector.Select(active.Count, me, _cfg.EffectiveTop, _cfg.EffectiveNear);

        // BATTLE FOR Nth: o jogador a menos de 1 s do carro da frente ou de trás (o mais próximo), os dois na seleção.
        int battleFront = -1;
        if (Battle18 && !finished && me >= 0)
        {
            double best = BattleSeconds;
            if (me > 0 && IntervalAhead(active, active[me]) is { } ia && ia < best) { best = ia; battleFront = me - 1; }
            if (me + 1 < active.Count && IntervalAhead(active, active[me + 1]) is { } ib && ib < best) battleFront = me;
            if (battleFront >= 0)
            {
                int pi = -1;
                for (int k = 0; k < picks.Count; k++) if (picks[k].Index == battleFront) { pi = k; break; }
                if (pi < 0 || pi + 1 >= picks.Count || picks[pi + 1].Index != battleFront + 1) battleFront = -1;
            }
        }

        DrawHeader18(c, t, m, head, x0, tw);
        if (head != Head18.Yellow) c.FillRect(x0, Head18H, tw, Rule18, t.AccentBar);
        float by = Head18H + Rule18;
        // Título do modo (fora da bandeirada).
        string? title = finished ? null : mode switch { "gainedlost" => "GAINED/LOST", "pitstops" => "PIT STOPS", "bestlap" => "BEST LAP", _ => null };
        if (title is not null)
        {
            c.FillRect(x0, by, tw, Title18H, t.PanelFill);
            c.Text(title, t.Title with { Size = 20, Tracking = 0.5f }, x0, by, tw, Title18H, t.TitleColor, HAlign.Center);
            by += Title18H;
        }

        // Altura do corpo: linhas + "..." + bloco BATTLE + faixas altas da bandeirada.
        float bodyH = 2 * Pad18;
        for (int i = 0; i < picks.Count; i++)
        {
            var r = active[picks[i].Index];
            if (picks[i].GapBefore) bodyH += SepH;
            if (picks[i].Index == battleFront) { bodyH += BattleTitleH + 2 * BattlePitch + 4; i++; continue; }
            bodyH += Pitch18 + (finished && r.Car.Position <= 3 ? FinishSub : 0);
        }
        c.FillRect(x0, by, tw, bodyH, t.PanelFill);
        if (gapCol && !fullNames) c.FillRect(L.Gap, by, x0 + tw - L.Gap, bodyH, t.GapCellFill);

        // Nomes completos: um tamanho único para todas as linhas (o menor que cabe ao lado do valor de cada uma).
        _nameFont18 = t.Text;
        if (fullNames)
            foreach (var r in picks.Select(p => active[p.Index]).Concat(outs))
                _nameFont18 = MinFont(_nameFont18, BroadcastUi.Fit(c, FullName18(r, field), t.Text, FullNameW18(c, t, m, mode, active, r, L, fastest0(all), IsOut(r))));

        double fastest = fastest0(all);
        float y = by + Pad18 + (Pitch18 - Box18) / 2;   // topo da caixa da linha
        IReadOnlyDictionary<int, float>? visualY = null;
        // BATTLE and podium bands have different heights: snap their layout so rows cannot cross those bands.
        if (battleFront >= 0 || finished) _rowMotion18.Reset();
        else
        {
            float targetY = y;
            var targets = new Dictionary<int, float>();
            foreach (var pick in picks)
            {
                if (pick.GapBefore) targetY += SepH;
                targets[active[pick.Index].Car.Index] = targetY;
                targetY += Pitch18;
            }
            string roster = string.Join("|", s.Cars.OrderBy(car => car.Index).Select(car => $"{car.Index}:{car.Name}:{car.CarName}"));
            string selected = string.Join(",", targets.Keys.Order());
            visualY = _rowMotion18.Evaluate(m.Now, (s.Kind, s.Track, s.TrackVariation, s.InSession,
                m.Broadcast?.SessionSeenT, roster, selected, by, fullNames), targets);
        }
        for (int i = 0; i < picks.Count; i++)
        {
            var r = active[picks[i].Index];
            if (picks[i].GapBefore)
            {
                float cy = y + SepH / 2 - (Pitch18 - Box18) / 2;
                for (int k = -1; k <= 1; k++) c.FillEllipse(L.Box + Box18W / 2 + k * 8, cy, 2.2f, 2.2f, t.LabelColor);
                y += SepH;
            }
            if (picks[i].Index == battleFront)
            {
                y = DrawBattle18(c, t, m, mode, active, r, active[picks[i].Index + 1], field, L, x0, tw, y - (Pitch18 - Box18) / 2, fastest);
                i++;
                continue;
            }
            bool tall = finished && r.Car.Position <= 3;
            DrawRow18(c, t, m, mode, active, field, classes, r, L, visualY?.GetValueOrDefault(r.Car.Index, y) ?? y, fastest, finished, tall, fullNames);
            y += Pitch18 + (tall ? FinishSub : 0);
        }

        // Bloco cinza dos pilotos fora: sem caixa de posição, sigla e "OUT" em cinza.
        if (outs.Count > 0)
        {
            float oy = by + bodyH + OutGap18;
            c.FillRect(x0, oy, tw, outs.Count * Pitch18, t.OutFill);
            for (int k = 0; k < outs.Count; k++)
            {
                float ry = oy + k * Pitch18 + (Pitch18 - Box18) / 2;
                var r = outs[k];
                if (_cfg.ColumnVisible("name"))
                {
                    string nm = fullNames ? FullName18(r, field) : _cfg.Name(r.Car, _codes);
                    float nw = fullNames ? FullNameW18(c, t, m, mode, active, r, L, 0, true) : Name18W + 4;
                    c.Text(nm, fullNames ? BroadcastUi.Fit(c, nm, _nameFont18, nw) : BroadcastUi.Fit(c, nm, t.Text, nw), L.Name, ry, nw + 8, Box18, t.OutInk);
                }
                if (gapCol) c.Text("OUT", t.Numbers with { Element = "label" }, L.Gap, ry, Gap18W - 10, Box18, t.OutInk, HAlign.Right);
            }
        }
    }

    FontToken _nameFont18 = Themes.F1_2018.Text;
    static double fastest0(IReadOnlyList<StandingRow> all) => all.Where(r => r.Car.BestLapTime > 0).Select(r => r.Car.BestLapTime).DefaultIfEmpty(0).Min();
    static FontToken MinFont(FontToken a, FontToken b) => b.Size < a.Size ? b : a;

    /// <summary>Largura livre para o nome completo: do início do nome até o valor da linha (o nome avança sobre a classe e a coluna clara).</summary>
    float FullNameW18(ThemeCanvas c, Theme.Theme t, OverlayModel m, string mode, IReadOnlyList<StandingRow> active, StandingRow r,
        (float Box, float Name, float Class, float Gap, float Width) L, double fastest, bool isOut = false)
    {
        float right = M18 + L.Width - 10;
        if (!_cfg.ColumnVisible("gap")) return right - L.Name;
        // Nomes completos (como no Safety Car/ENDING da TV): a coluna de valores some; só ficam IN PIT / PIT EXIT e OUT.
        float vw = isOut ? c.Measure("OUT", t.Numbers with { Element = "label" }) : InPit18(r) ? c.Measure(PitText18(r), t.Numbers with { Element = "label" }) : 0;
        return right - (vw > 0 ? vw + 14 : 0) - L.Name;
    }

    static bool InPit18(StandingRow r) => r.Car.PitState is PitState.DrivingIntoPits or PitState.InPit or PitState.DrivingOutOfPits;
    static string PitText18(StandingRow r) => r.Car.PitState == PitState.DrivingOutOfPits ? "PIT EXIT" : "IN PIT";

    void DrawPit18(ThemeCanvas c, Theme.Theme t, StandingRow r, float x, float y, float w, float h)
    {
        string p = PitText18(r);
        c.Text(p, BroadcastUi.Fit(c, p, t.Numbers with { Element = "label" }, w - 14), x, y, w - 10, h, t.PitTimeColor, HAlign.Right);
    }

    /// <summary>Largura do valor desenhado por <see cref="DrawValue18"/> na linha.</summary>
    float ValueW18(ThemeCanvas c, Theme.Theme t, OverlayModel m, string mode, IReadOnlyList<StandingRow> active, StandingRow r)
    {
        var f = _cfg.Fmt;
        if (mode is "gap" or "interval" or "bestlap" && InPit18(r)) return c.Measure(PitText18(r), t.Numbers with { Element = "label" });
        string v = mode switch
        {
            "gainedlost" => "^ 00",
            "pitstops" => BroadcastUi.State(m).StopsOf(r.Car.Index).ToString(CultureInfo.InvariantCulture),
            "bestlap" => r.Car.BestLapTime > 0 ? f.FormatLapTime(r.Car.BestLapTime) : f.NoGap,
            "interval" => HeldText18(active, r, "interval"),
            _ => HeldText18(active, r, "gap"),
        };
        return Math.Min(Gap18W - 10, c.Measure(v, t.Numbers with { Element = mode switch { "gainedlost" => "position", "pitstops" => "value", "bestlap" => "time", _ => "gap" } }));
    }

    /// <summary>Cabeçalho: "LAP" + "n / N" (normal), "YELLOW FLAG" + faixa amarela "SECTOR n", ou "LAP | N / N" sobre a bandeira xadrez.</summary>
    void DrawHeader18(ThemeCanvas c, Theme.Theme t, OverlayModel m, Head18 head, float x0, float tw)
    {
        string lc = LapCounterWidget.Format(m);
        lc = lc.StartsWith("--") ? "- / -" : lc.StartsWith("Lap ") ? lc[4..] : lc.Replace("/", " / ");
        var white = new Vortice.Win32.Numerics.Color4(1f, 1f, 1f, 1f);
        switch (head)
        {
            case Head18.Yellow:
            {
                // Largura total da torre: parte preta com "YELLOW FLAG" amarelo e faixa amarela com bandeira e "SECTOR n" preto.
                const float top = 44;
                c.FillRoundRect(x0, 0, tw, top, 7, t.PanelFill);
                c.FillRect(x0, top - 8, tw, 8, t.PanelFill);
                c.Text("YELLOW FLAG", BroadcastUi.Fit(c, "YELLOW FLAG", t.Title with { Size = 24, Tracking = 0.5f }, tw - 16), x0, 2, tw, top - 2, t.FlagColor, HAlign.Center);
                c.FillRect(x0, top, tw, Head18H - top, t.FlagColor);
                var ink = new Vortice.Win32.Numerics.Color4(0.04f, 0.04f, 0.05f, 1f);
                int sector = Math.Clamp((m.Session?.PlayerCar?.Sector ?? 0) + 1, 1, 3);
                string st = "SECTOR " + sector.ToString(CultureInfo.InvariantCulture);
                var sf = t.Title with { Size = 20 };
                float sw = c.Measure(st, sf), fx = x0 + (tw - sw) / 2 - 30, fy = top + 8;
                // Bandeirinha: mastro + pano preto.
                c.FillRect(fx, fy, 2.2f, Head18H - top - 14, ink);
                c.FillRect(fx + 2, fy, 16, 11, ink);
                c.Text(st, sf, fx + 30, top, sw + 8, Head18H - top, ink);
                return;
            }
            case Head18.Finish:
            {
                // Bandeira xadrez translúcida no cabeçalho inteiro, "LAP" | "N / N" numa linha.
                c.FillRoundRect(x0, 0, tw, Head18H, 7, t.PanelFill);
                BroadcastUi.WithAlpha(c, 0.42f, () =>
                {
                    float hh = Head18H / 4;
                    for (int k = 0; k < 4; k += 2) Chrome.Checkered(c, x0, k * hh, tw, 2 * hh);
                });
                float half = tw * 0.46f;
                c.Text("LAP", t.Title with { Size = 26, Tracking = 4 }, x0, 0, half, Head18H, white, HAlign.Center);
                c.FillRect(x0 + half, 18, 1.5f, Head18H - 36, white);
                c.Text(lc, t.Numbers with { Size = 28, Weight = 700 }, x0 + half, 0, tw - half, Head18H, white, HAlign.Center);
                return;
            }
            default:
            {
                float hw = Head18W;
                c.FillRoundRect(x0, 0, hw, Head18H, 7, t.PanelFill);
                c.FillRect(x0, Head18H - 8, hw, 8, t.PanelFill);
                c.Text("LAP", t.Title with { Size = 26, Tracking = 4 }, x0, 4, hw + 4, 34, t.TitleColor, HAlign.Center);
                c.FillRect(x0 + 34, 40, hw - 68, 1.2f, new Vortice.Win32.Numerics.Color4(1f, 1f, 1f, 0.45f));
                c.Text(lc, t.Numbers with { Size = 26 }, x0, 42, hw, 32, t.ValueColor, HAlign.Center);
                return;
            }
        }
    }

    /// <summary>Uma linha normal (ou faixa alta da bandeirada) da torre 2018; <paramref name="y"/> = topo da caixa de posição.</summary>
    void DrawRow18(ThemeCanvas c, Theme.Theme t, OverlayModel m, string mode, IReadOnlyList<StandingRow> active, IReadOnlyList<CarSnapshot> field,
        List<string> classes, StandingRow r, (float Box, float Name, float Class, float Gap, float Width) L, float y, double fastest, bool finished, bool tall, bool fullNames)
    {
        float rh = Box18;
        bool gapCol = _cfg.ColumnVisible("gap");
        // À esquerda, fora do painel: mini bandeira xadrez de quem recebeu a bandeirada; senão o marcador roxo da melhor volta.
        if (finished && r.Car.RaceState == RaceState.Finished) Chrome.Checkered(c, 6, y + 8, 22, 16);
        else if (fastest > 0 && r.Car.BestLapTime == fastest) Chrome.FastestMarker(c, 0, y, Box18);
        if (_cfg.ColumnVisible("pos"))
            Chrome.PosBox(c, L.Box, y, Box18W, rh, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 21 },
                tall && r.Car.Position == 1 ? t.FastestFill : null);
        var ink = r.IsPlayer ? t.PlayerColor : t.TextColor;
        if (tall)
        {
            // Faixa alta (sem retrato no AMS2): sobrenome em caixa alta na largura toda e, embaixo, faixa com tique da classe e a equipe.
            float nx = _cfg.ColumnVisible("name") ? L.Name : L.Box + Box18W + 10;
            string nm = FullName18(r, field);
            float nw = M18 + L.Width - nx - 8;
            c.Text(nm, BroadcastUi.Fit(c, nm, t.Text with { Size = 24 }, nw), nx, y, nw + 8, rh, ink);
            float sy = y + Pitch18 - (Pitch18 - Box18) / 2;
            int ci = Math.Max(0, classes.IndexOf(r.Car.ClassName));
            c.FillRect(M18, sy, L.Width, FinishSub, t.SubPanelFill);
            Chrome.Tick(c, nx, sy + 5, FinishSub - 10, Chrome.ClassTick(ci));
            string team = BroadcastUi.Team(r.Car);
            c.Text(team, BroadcastUi.Fit(c, team, t.Label with { Element = "name", Size = 16 }, nw - 12), nx + 10, sy, nw - 4, FinishSub, t.LabelColor);
            return;
        }
        if (_cfg.ColumnVisible("name"))
        {
            string nm = fullNames ? FullName18(r, field) : _cfg.Name(r.Car, _codes);
            float nw = fullNames ? FullNameW18(c, t, m, mode, active, r, L, fastest) : Name18W + 4;
            c.Text(nm, BroadcastUi.Fit(c, nm, fullNames ? _nameFont18 : t.Text, nw), L.Name, y, nw + 8, rh, ink);
        }
        if (_cfg.ColumnVisible("class") && !fullNames)
        {
            // No lugar do logo da equipe (o AMS2 não tem logos): letra da classe numa caixinha neutra.
            int ci = Math.Min(Math.Max(0, classes.IndexOf(r.Car.ClassName)), 25);
            c.FillRoundRect(L.Class + 2, y + 3, ClassW18 - 4, rh - 6, 3, t.BadgeFill);
            c.Text(((char)('A' + ci)).ToString(), t.Label with { Weight = 700 }, L.Class + 2, y + 3, ClassW18 - 4, rh - 6, t.BadgeInk, HAlign.Center);
        }
        if (gapCol && fullNames) { if (InPit18(r)) DrawPit18(c, t, r, L.Gap, y, Gap18W, rh); }
        else if (gapCol) DrawValue18(c, t, m, mode, active, r, L.Gap, y, Gap18W, rh, fastest);
    }

    /// <summary>Valor da coluna clara conforme o modo; IN PIT / PIT EXIT ciano no lugar do tempo (gap, intervalo e melhor volta).</summary>
    void DrawValue18(ThemeCanvas c, Theme.Theme t, OverlayModel m, string mode, IReadOnlyList<StandingRow> active, StandingRow r, float x, float y, float w, float h, double fastest)
    {
        var f = _cfg.Fmt;
        float right = x + w - 10;
        if (mode is "gap" or "interval" or "bestlap" && InPit18(r)) { DrawPit18(c, t, r, x, y, w, h); return; }
        switch (mode)
        {
            case "gainedlost":
            {
                int? d = GridTracker.Delta(m.Grid, r.Car);
                if (d is null) { c.Text(f.NoGap, t.Numbers with { Element = "position" }, x, y, w - 10, h, t.LabelColor, HAlign.Right); return; }
                int n = Math.Abs(d.Value);
                string num = n.ToString(CultureInfo.InvariantCulture);
                var nf = t.Numbers with { Element = "position", Size = 22 };
                float nw = c.Measure(num, nf);
                c.Text(num, nf, right - nw - 2, y, nw + 6, h, t.ValueColor);
                float cx = right - nw - 26, cy = y + h / 2;
                if (d > 0) { var g = new Vortice.Win32.Numerics.Color4(37 / 255f, 194 / 255f, 58 / 255f, 1f); c.Line(cx - 7, cy + 4, cx, cy - 4, g, 3f); c.Line(cx, cy - 4, cx + 7, cy + 4, g, 3f); }
                else if (d < 0) { var yl = t.FlagColor; c.Line(cx - 7, cy - 4, cx, cy + 4, yl, 3f); c.Line(cx, cy + 4, cx + 7, cy - 4, yl, 3f); }
                else c.FillRect(cx - 5, cy - 1.5f, 10, 3, t.LabelColor);
                return;
            }
            case "pitstops":
            {
                string n = BroadcastUi.State(m).StopsOf(r.Car.Index).ToString(CultureInfo.InvariantCulture);
                c.Text(n, t.Numbers with { Size = 22 }, x, y, w - 10, h, t.ValueColor, HAlign.Right);
                return;
            }
            case "bestlap":
            {
                bool best = fastest > 0 && r.Car.BestLapTime == fastest;
                string v = r.Car.BestLapTime > 0 ? f.FormatLapTime(r.Car.BestLapTime) : f.NoGap;
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers with { Element = "time" }, w - 14), x, y, w - 10, h, best ? new Vortice.Win32.Numerics.Color4(0.80f, 0.45f, 1f, 1f) : t.ValueColor, HAlign.Right);
                return;
            }
            case "interval":
            {
                string v = HeldText18(active, r, "interval");
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers with { Element = "gap" }, w - 14), x, y, w - 10, h, t.ValueColor, HAlign.Right);
                return;
            }
            default:
            {
                string v = HeldText18(active, r, "gap");
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers with { Element = "gap" }, w - 14), x, y, w - 10, h, t.ValueColor, HAlign.Right);
                return;
            }
        }
    }

    /// <summary>Bloco "BATTLE FOR Nth": título com filete vermelho e duas faixas altas (caixa, sobrenome, valor; o de trás mostra o intervalo).
    /// <paramref name="top"/> = topo do bloco; devolve o topo da caixa da próxima linha.</summary>
    float DrawBattle18(ThemeCanvas c, Theme.Theme t, OverlayModel m, string mode, IReadOnlyList<StandingRow> active, StandingRow front, StandingRow back,
        IReadOnlyList<CarSnapshot> field, (float Box, float Name, float Class, float Gap, float Width) L, float x0, float tw, float top, double fastest)
    {
        var dark = new Vortice.Win32.Numerics.Color4(0f, 0f, 0f, 0.55f);
        float y = top;
        c.FillRect(x0, y, tw, 3, t.AccentBar);
        c.FillRect(x0, y + 3, tw, BattleTitleH - 3, dark);
        var tf = t.Title with { Size = 18, Weight = 400, Tracking = 0 };
        string a = "BATTLE FOR ", b = Ordinal(front.Car.Position);
        var bf = tf with { Weight = 700 };
        float aw = c.Measure(a, tf), bw = c.Measure(b, bf), sx = x0 + (tw - aw - bw) / 2;
        c.Text(a, tf, sx, y + 3, aw + 4, BattleTitleH - 3, t.TitleColor);
        c.Text(b, bf, sx + aw, y + 3, bw + 6, BattleTitleH - 3, t.TitleColor);
        y += BattleTitleH;
        bool gapCol = _cfg.ColumnVisible("gap");
        bool centerGap = mode is "gap" or "interval" && back.Car.PitState == PitState.None;
        float nx = _cfg.ColumnVisible("name") ? L.Name : L.Box + Box18W + 10;
        float NameW(StandingRow r) => (gapCol ? x0 + tw - 10 - ValueW18(c, t, m,
            r.Car.Index == back.Car.Index && (mode is "gap" or "interval") ? "interval" : mode, active, r) - 14 : x0 + tw - 8) - nx;
        var nf = MinFont(BroadcastUi.Fit(c, FullName18(front, field), t.Text, NameW(front)), BroadcastUi.Fit(c, FullName18(back, field), t.Text, NameW(back)));
        foreach (var r in new[] { front, back })
        {
            c.FillRect(x0, y, tw, BattlePitch - 2, dark);
            float by = y + (BattlePitch - 2 - Box18) / 2;
            if (fastest > 0 && r.Car.BestLapTime == fastest) Chrome.FastestMarker(c, 0, by, Box18);
            if (_cfg.ColumnVisible("pos")) Chrome.PosBox(c, L.Box, by, Box18W, Box18, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 21 });
            string nm = FullName18(r, field);
            float nw = NameW(r);
            c.Text(nm, BroadcastUi.Fit(c, nm, nf, nw), nx, by, nw + 8, Box18, r.IsPlayer ? t.PlayerColor : t.TextColor);
            // Com gap/interval o valor vai ao centro, entre as faixas; senão cada faixa mostra o seu.
            if (gapCol && !centerGap) DrawValue18(c, t, m, mode, active, r, L.Gap, by, Gap18W, Box18, fastest);
            y += BattlePitch;
        }
        if (gapCol && centerGap)
        {
            // Pílula central a cavalo sobre a divisa das faixas: seta para cima, intervalo, seta para baixo.
            string v = HeldText18(active, back, "interval");
            var vf = BroadcastUi.Fit(c, v, t.Numbers with { Element = "gap", Size = 20 }, 130);
            float pw = 190, ph = 30, px = x0 + (tw - pw) / 2, py = y - BattlePitch - 1 - ph / 2;
            c.FillRect(px, py, pw, ph, new Vortice.Win32.Numerics.Color4(0f, 0f, 0f, 0.85f));
            c.FillRect(px, py, pw, 2, t.AccentBar);
            c.Text(v, vf, px + 30, py, pw - 60, ph, t.ValueColor, HAlign.Center);
            float cy = py + ph / 2;
            c.FillPolygon([new(px + 8, cy + 6), new(px + 16, cy - 6), new(px + 24, cy + 6)], t.ValueColor);
            c.FillPolygon([new(px + pw - 24, cy - 6), new(px + pw - 16, cy + 6), new(px + pw - 8, cy - 6)], t.ValueColor);
        }
        return y + 4 + (Pitch18 - Box18) / 2;
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
            Chrome.AccentBox(c, x, y, TableBoxW, TableBoxH, r.Car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position" });
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
            else c.Text(Gap(r, defaultSign: false), t.Numbers with { Element = "gap" }, right - Math.Max(160, TableGapW), y, Math.Max(160, TableGapW), TableBoxH, t.ValueColor, HAlign.Right, t.ValueShadow);
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
            else Chrome.PositionBox(c, x, y, Pos04, h, r.Car.Position, t.Numbers with { Element = "position" });
            x += Pos04;
        }
        if (_cfg.ColumnVisible("name"))
        {
            string nm = _cfg.Name(r.Car, _codes);
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
        if (_cfg.ColumnVisible("gap")) Chrome.BlackCell(c, x, y, Gap04, h, LeaderLap(r) ?? Gap(r), t.Numbers with { Element = "gap" });
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
