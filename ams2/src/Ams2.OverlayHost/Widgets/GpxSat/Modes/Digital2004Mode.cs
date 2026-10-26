using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;

namespace Ams2.OverlayHost.Widgets.GpxSat.Modes;

/// <summary>
/// GPxSAT "Digital 2004" (tema F1 2004-2008). Layouts de art\spec_digital2004.md secao 4 / art\tpl2.html (aprovado), despacho da secao 3.
/// Medidas no espaco 1024x768 com FH = 17: L0 = W/5, C1 = [L0, L0+W/5), C2 = W/5-FH a partir de 2*L0, C3 a partir de 2*L0+W/5-FH,
/// linha "-n" = topo em 768 - n*FH. Cada lista de caixas com o mesmo Group e uma linha (ou coluna, no standings) do animador.
/// </summary>
public sealed class Digital2004Mode : IGpxMode
{
    public string Id => "digital2004";
    public int CarsPerPage => 8;
    public bool Implemented => true;
    // gpxsat.ini: Font=digital2004 (digital2004.ttf, Height 17, peso 400). FONT_BIG so nos numeros 1.5FH x 2FH (tpl2: 1.9 FH).
    static readonly string[] Families = ["Digital Standard", "Digital", "Digital2004", "Digital 2004", "digital2004"];
    public GpxModeSpec Spec { get; } = new(17, new(Families, 400, 1f, 600, 0.95f), new(Families, 400, 1.9f, 600, 1.75f));

    const uint K = GpxInk.Black, Wh = GpxInk.White;
    const string Key0 = "d04:";

    // ---------- despacho (secao 3) ----------

    public IReadOnlyList<GpxScreen> Dispatch(GpxContext c)
    {
        var list = new List<GpxScreen>();
        if (c.Previewing)
        {
            if (Preview(c) is { } p) list.Add(p);
            return list;
        }
        if (c.Options.ShowMode == "none") return list;
        if (c.Options.ShowTimeLeft && TimeLeft(c) is { } t) list.Add(t);
        if (c.ShowSky && SkyMessage(c) is { } sky) list.Add(sky);
        if (!c.ShowAll) return list;
        if (c.IsRace) RaceScreens(c, list); else OtherScreens(c, list);
        // Legenda recorrente: menor prioridade, so quando o slot principal ficaria vazio.
        if (c.CaptionOpen && !list.Any(s => s.Slot == "main") && c.Focus is { InPitLane: false } me) list.Add(Caption(c, me));
        return list;
    }

    /// <summary>
    /// Legenda recorrente do piloto entre eventos: placa completa (nome + posicao, equipe com pneu, bandeira do pais, logo opcional) ou, com a janela
    /// curta (menos de 4 s), a variante simples so com a faixa branca (nome + posicao + bandeira). Prioridade 0.
    /// </summary>
    GpxScreen Caption(GpxContext c, CarSnapshot car)
    {
        var g = c.G; float fh = g.FH;
        bool full = c.Options.CaptionSeconds >= 4;
        var boxes = new List<GpxBox> { Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car)) };
        int pos = c.PositionOf(car);
        if (pos > 0) boxes.Add(PosBig(g.L0 + C1w, g.Row(4), fh, pos, 0));
        if (!string.IsNullOrWhiteSpace(car.Country))
            boxes.Add(Bx(g.L0 + C1w + (pos > 0 ? 1.5f * fh : 0) + 4, g.Row(4), 1.5f * fh, fh, GpxTextures.White, car.Country, img: GpxImage.CountryFlag) with { Asset = null });
        if (full)
        {
            boxes.Add(Bx(g.L0, g.Row(3), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 1));
            if (car.TyreSupplier is "M" or "B")
                boxes.Add(Bx(g.L0 + C1w - fh, g.Row(3), fh, fh, GpxTextures.White, group: 1, img: car.TyreSupplier == "M" ? GpxImage.TyreM : GpxImage.TyreB));
            boxes.Add(Logo(g.L0, g.Row(5), C1w, fh));
        }
        return new GpxScreen("main", $"{Key0}cap:{car.Index}{(full ? "" : ":s")}", boxes);
    }

    /// <summary>Sky message ativa: vencedor (bandeirada do 1o), volta mais rapida, abandono, boxes (carro em foco = timer) e riding with.</summary>
    GpxScreen? SkyMessage(GpxContext c)
    {
        var b = c.Broadcast;
        if (c.IsRace && b.Winner is { } w && c.Now - w.FinishedT is >= 0 and var age && age < c.Options.SkySeconds) return Winner(c, w) with { Priority = 110 };
        if (c.Sky.Active(c.Now) is not { } e) return null;
        GpxScreen s = e.Kind switch
        {
            GpxSkyKind.FastestLap => FastestLap(c, e.Car, e.LapTime, e.Key),
            GpxSkyKind.OutOfRace => OutOfRace(c, e.Car, e.Key),
            GpxSkyKind.InPits => InPits(c, e.Car, e.Key),
            _ => RidingWith(c, e.Car, e.Key),
        };
        return s with { Priority = 100 };
    }

    /// <summary>Corrida (0x9c0df02): drive-through (sem dado no AMS2) &gt; carro em foco nos boxes &gt; Director's cut &gt; standings &gt; SpeedSplit.</summary>
    void RaceScreens(GpxContext c, List<GpxScreen> list)
    {
        // Retrato da classificacao no instante em que o lider cruza a linha (mesmo com outra tela no slot): valores congelados na janela.
        if (!c.Options.DirectorsCut && (c.Options.Rotation || c.Options.RaceStandings is "standings" or "pitstops")) FrozenRace(c);
        if (c.Focus is { InPitLane: true } f) { list.Add(PitTimer(c, f, PitSeconds(c)) with { Priority = 90 }); return; }
        if (c.Options.DirectorsCut && DirectorsCut(c) is { } dc) { list.Add(dc with { Priority = 50 }); return; }
        var split = c.Options.SpeedSplitRace switch
        {
            "behind" => c.Split.Behind is { } gb && gb.ActiveAt(c.Now) ? GapBehind(c, gb) : null,
            "trap" => c.Split.Trap is { } tw && tw.ActiveAt(c.Now) ? SpeedTrap(c, tw) : null,
            _ => c.Split.Ahead is { } ga && ga.ActiveAt(c.Now) ? GapAhead(c, ga) : null,
        };
        if (c.Options.Rotation)
        {
            if (split is not null) list.Add(split with { Priority = 30 });
            else if (LapStandings(c) is { } page) list.Add(page);
            return;
        }
        if (c.Options.RaceStandings is "standings" or "pitstops")
        {
            if (FrozenRace(c) is { Count: > 0 } all) list.Add(all[c.PageOf(all.Count * CarsPerPage, CarsPerPage)]);
            return;
        }
        if (split is not null) list.Add(split with { Priority = 30 });
    }

    /// <summary>Standings da corrida orientados a evento: so na janela aberta quando o lider fecha a volta (estaticos, uma passagem por pagina); fora dela, slot vazio.</summary>
    GpxScreen? LapStandings(GpxContext c)
    {
        var pages = FrozenRace(c);
        return GpxRotation.EventPage(c.Split.LeaderLapT, c.Now, c.Options.StandingsPageSeconds, pages.Count) is { } page ? pages[page] : null;
    }

    readonly GpxEventSnapshot _frozen = new();
    /// <summary>Todas as paginas da classificacao (ou paradas) montadas quando o lider cruzou a linha e congeladas ate o proximo cruzamento.</summary>
    IReadOnlyList<GpxScreen> FrozenRace(GpxContext c)
    {
        bool stops = c.Options.RaceStandings == "pitstops";
        return _frozen.Get(stops ? "stP" : "stR", c.Split.LeaderLapT, () => Enumerable.Range(0, GpxRotation.PagesOf(RaceRows(c).Count, CarsPerPage))
            .Select(p => stops ? PitStopsStandings(c, p) : RaceStandings(c, p)).ToArray());
    }

    /// <summary>Fora da corrida (0x9c0dffc): standings de classificacao (se ligado e houver mais de um tempo), senao o SpeedSplit.</summary>
    void OtherScreens(GpxContext c, List<GpxScreen> list)
    {
        var q = c.Model.Quali;
        if (c.Options.QualiMerged && c.Options.SpeedSplitQuali != "trap") { if (QualiMerged(c) is { } m) list.Add(m with { Priority = 30 }); return; }
        // Parcial/resultado da volta (janela SplitSeconds) tem prioridade sobre a tabela; antes a tabela retornava primeiro e a parcial nunca aparecia.
        if (c.Options.SpeedSplitQuali != "trap" && QualiSplitNow(c) is { } live && live.Key.Contains("split:")) { list.Add(live with { Priority = 30 }); return; }
        if (c.Options.QualiStandings && q is not null && q.Rows.Count(r => r.HasTime) > 1) { list.Add(QualiStandings(c)); return; }
        if (c.Options.SpeedSplitQuali == "trap")
        {
            if (c.Split.Trap is { } tw && tw.ActiveAt(c.Now)) list.Add(SpeedTrap(c, tw) with { Priority = 30 });
            return;
        }
        if (QualiSplitNow(c) is { } s) list.Add(s with { Priority = 30 });
    }

    readonly GpxQualiFlow _qualiFlow = new();

    /// <summary>
    /// Qualy Board mesclado: tabela de melhores voltas (retrato) so na volta de saida, depois o cartao; na volta lancada, relogio,
    /// aproximacao, intermediaria e resultado nos tempos da Exibicao.
    /// </summary>
    GpxScreen? QualiMerged(GpxContext c)
    {
        if (c.Focus is not { } f || c.Model.QualiLap is not { } ql || ql.CarIndex != f.Index || f.InGarage) return null;
        int rows = Math.Min(22, c.Model.Quali?.Rows.Count ?? 0);
        var st = _qualiFlow.Step(ql, f.Index, c.Now, rows, CarsPerPage, c.Session.Track, c.Options.QualiTimings);
        switch (st.Phase)
        {
            case GpxQualiPhase.Table:
                if (rows == 0) return GenericCard(c, f, $"{Key0}card:{f.Index}");
                var pages = _frozen.Get("stQ", st.TableStart, () => Enumerable.Range(0, GpxRotation.PagesOf(rows, CarsPerPage)).Select(p => QualiStandings(c, p)).ToArray());
                return pages[Math.Min(st.TablePage, pages.Length - 1)];
            case GpxQualiPhase.OutLap: return f.InPitLane ? null : GenericCard(c, f, $"{Key0}card:{f.Index}");
            case GpxQualiPhase.Running: return QualiSplit(c, f, GpxFormat.Lap(Math.Max(1, ql.Lap)), st.View!.Time, null, c.PositionOf(f), $"{Key0}split:{f.Index}:run:{ql.Lap}");
            case GpxQualiPhase.Approach: return QualiSplit(c, f, GpxFormat.Intermediate(Math.Max(1, st.View!.Mark)), st.View.Time, null, c.PositionOf(f), $"{Key0}split:{f.Index}:app:{ql.Lap}:{st.View.Mark}");
            case GpxQualiPhase.Split or GpxQualiPhase.Finish: return QualiSplitNow(c);
            default: return null;
        }
    }

    /// <summary>Tempo parado do carro em foco: ao vivo enquanto parado, a ultima parada desta passagem pela pit lane, senao 0.</summary>
    static double PitSeconds(GpxContext c)
    {
        var b = c.Broadcast;
        if (b.PlayerStopped) return b.PlayerStopNow(c.Now);
        return b.PlayerStopEndT >= b.PlayerPitLaneStartT ? b.PlayerStopSeconds : 0;
    }

    /// <summary>
    /// Director's cut (0x9c1a41f, C/P): em cada passagem do carro em foco, ~3 em 10 vao para o speed trap (sorteio deterministico pela
    /// passagem); senao o menor gap entre o carro a frente e o de tras.
    /// </summary>
    GpxScreen? DirectorsCut(GpxContext c)
    {
        var a = c.Split.Ahead is { } x && x.ActiveAt(c.Now) ? x : null;
        var b = c.Split.Behind is { } y && y.ActiveAt(c.Now) ? y : null;
        var t = c.Split.Trap is { } z && z.ActiveAt(c.Now) ? z : null;
        int pass = a?.Pass ?? b?.Pass ?? 0;
        if (t is not null && (uint)(pass * 2654435761u) % 10 < 3) return SpeedTrap(c, t);
        if (a is not null && b is not null)
        {
            double ga = a.Laps > 0 ? double.MaxValue : a.Gap ?? double.MaxValue, gb = b.Laps > 0 ? double.MaxValue : b.Gap ?? b.Running(c.Now);
            return gb < ga ? GapBehind(c, b) : GapAhead(c, a);
        }
        if (a is not null) return GapAhead(c, a);
        if (b is not null) return GapBehind(c, b);
        return t is not null ? SpeedTrap(c, t) : null;
    }

    /// <summary>
    /// Split de quali ao vivo (0x9c11ed2): na janela de SplitSeconds depois de cada parcial ou da linha, com o delta para o 1o.
    /// Na volta de saida o GPx mostra o cartao generico (0x9c111e7) com o melhor tempo e o gap: aqui na janela de cada passagem.
    /// </summary>
    GpxScreen? QualiSplitNow(GpxContext c)
    {
        if (c.Focus is not { } f || c.Model.QualiLap is not { CarIndex: >= 0 } ql) return null;
        double holdS = c.Options.QualiMerged ? c.Options.QualiTimings.Split : c.Options.SplitSeconds, holdR = c.Options.QualiMerged ? c.Options.QualiTimings.Result : c.Options.SplitSeconds;
        var sp = ql.LastSplit is { } s && c.Now - s.At is >= 0 and var sa && sa < holdS ? s : null;
        var lr = ql.LastResult is { } r && c.Now - r.At is >= 0 and var ra && ra < holdR ? r : null;
        if (lr is not null && (sp is null || lr.At >= sp.At))
            return QualiSplit(c, f, GpxFormat.FinishLine, lr.LapTime, lr.Invalid ? null : lr.GapToFirst, lr.Position, $"{Key0}split:{f.Index}:L{lr.Lap}");
        if (sp is not null)
            return QualiSplit(c, f, GpxFormat.Intermediate(sp.Sector), sp.Elapsed, sp.DeltaLeader ?? sp.DeltaPersonal, c.PositionOf(f), $"{Key0}split:{f.Index}:{ql.Lap}:{sp.Sector}");
        if ((ql.OutLap || ql.InPit) && c.Split.Trap is { } tw && tw.ActiveAt(c.Now))
            return GenericCard(c, f, $"{Key0}card:{f.Index}:{tw.OpenT:0.000}");
        return null;
    }

    // ---------- telas (secao 4) ----------

    float FH(GpxContext c) => c.G.FH;
    static float C1w => GpxGeometry.W / 5;

    static GpxBox Bx(float l, float t, float w, float h, string tex, string? text = null, GpxAlign al = GpxAlign.Left, uint ink = K, int group = 0,
        float indent = float.NaN, GpxFontRole font = GpxFontRole.Normal, GpxImage img = GpxImage.None, bool logo = false)
        => new(new GpxRect(l, t, w, h), tex, text, al, ink, group, indent, font, img, logo)
        {
            Asset = img switch { GpxImage.Flag => "digital_flag", GpxImage.TyreM => "digital_2004_m", GpxImage.TyreB => "digital_2004_b", _ => tex switch
                { GpxTextures.White => "white_button", GpxTextures.Black => "black_button", GpxTextures.Blue => "blue_button",
                  GpxTextures.Yellow => "yellow_button", GpxTextures.YellowFlag => "yellow_flag_button", GpxTextures.Green => "green_button",
                  GpxTextures.Red => "red_button", GpxTextures.Positions => "positions_button", GpxTextures.Leader => "leader_button", _ => null } },
        };

    /// <summary>Logo opcional (siemens_2004.tga no GPx): procedural, caixa branca com o nome em verde-azulado.</summary>
    static GpxBox Logo(float l, float t, float w, float h) => Bx(l, t, w, h, GpxTextures.White, null, GpxAlign.Center, 0xff009a9a, logo: true) with { Asset = "siemens_2004" };

    static string Pos(int p) => p.ToString(CultureInfo.InvariantCulture);

    /// <summary>4.1 Time left (h:mm:ss / m:ss) ou lap counter na corrida por voltas, caixa amarela W/7.5 centrada em 2FH; sessao curta a esquerda.</summary>
    GpxScreen? TimeLeft(GpxContext c, bool force = false)
    {
        var s = c.Session;
        string? text = null;
        if (c.IsRace && s.LapsInEvent > 0)
        {
            int lap = s.Cars.Where(x => x.Position == 1).Select(x => x.CurrentLap).FirstOrDefault();
            if (lap <= 0) lap = c.Focus?.CurrentLap ?? 1;
            text = c.Settings.Option("lapsLeftMode") == "current" ? GpxFormat.Lap(Math.Min(lap, s.LapsInEvent))
                : $"{Math.Max(0, s.LapsInEvent - Math.Max(0, lap - 1))} Laps remaining";
        }
        else if (s.TimeRemainingSeconds is { } rem && rem >= 0) text = GpxFormat.Clock(rem);
        else if (force) text = GpxFormat.Clock(5025);
        if (text is null) return null;
        float fh = FH(c), w = GpxGeometry.W / 7.5f, x = GpxGeometry.W / 2 - w / 2, y = 2 * fh;
        return new GpxScreen("top", $"{Key0}time:{s.Kind}",
        [
            Bx(x, y, w, fh, s.FlagColour is 6 or 7 ? GpxTextures.YellowFlag : GpxTextures.Yellow, text, GpxAlign.Center),
            Bx(x - 2 * fh, y, 2 * fh, fh, GpxTextures.Black, GpxFormat.SessionShort(s.Kind), GpxAlign.Center, GpxInk.Session),
        ]);
    }

    /// <summary>4.2 Volta mais rapida: C1 nome/equipe, C2 "Fastest Lap"/tempo.</summary>
    GpxScreen FastestLap(GpxContext c, CarSnapshot car, double lap, string key)
    {
        var g = c.G; float fh = g.FH, c2 = 2 * g.L0, c2w = C1w - fh;
        return new GpxScreen("main", Key0 + key,
        [
            Bx(g.L0, g.Row(5), C1w, fh, GpxTextures.White, c.Name(car)), Bx(c2, g.Row(5), c2w, fh, GpxTextures.Blue, GpxFormat.FastestLap, ink: Wh),
            Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 1), Bx(c2, g.Row(4), c2w, fh, GpxTextures.Black, GpxFormat.LapTime(lap), GpxAlign.Right, Wh, 1),
        ]);
    }

    /// <summary>4.3/4.4 Fora da corrida / nos boxes: nome + caixa preta com o texto a direita.</summary>
    GpxScreen OneLine(GpxContext c, CarSnapshot car, string text, string key)
    {
        var g = c.G; float fh = g.FH;
        return new GpxScreen("main", Key0 + key,
            [Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car)), Bx(2 * g.L0, g.Row(4), C1w - fh, fh, GpxTextures.Black, text, GpxAlign.Right, Wh)]);
    }

    GpxScreen OutOfRace(GpxContext c, CarSnapshot car, string key) => OneLine(c, car, GpxFormat.OutOfRace, key);
    GpxScreen InPits(GpxContext c, CarSnapshot car, string key) => OneLine(c, car, GpxFormat.InThePits, key);
    /// <summary>4.5 Timer de boxes do carro em foco (decimos).</summary>
    GpxScreen PitTimer(GpxContext c, CarSnapshot car, double seconds) => OneLine(c, car, GpxFormat.Tenths(seconds), $"pitt:{car.Index}");

    /// <summary>4.6 Drive-through (o AMS2 nao informa penalidades: so na previa).</summary>
    GpxScreen DriveThrough(GpxContext c, CarSnapshot car)
    {
        var g = c.G; float fh = g.FH, c2 = 2 * g.L0, c2w = C1w - fh;
        return new GpxScreen("main", $"{Key0}dt:{car.Index}",
        [
            Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car)), Bx(c2, g.Row(4), c2w, fh, GpxTextures.Black, GpxFormat.DriveThrough, ink: Wh),
            Bx(g.L0, g.Row(3), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 1), Bx(c2, g.Row(3), c2w, fh, GpxTextures.Black, GpxFormat.Penalty, ink: Wh, group: 1),
        ]);
    }

    /// <summary>4.7 Vencedor: bandeira 2FH, coluna Winner/nome/equipe(+pneu) e coluna preta tempo/distancia/media.</summary>
    GpxScreen Winner(GpxContext c, WinnerInfo w)
    {
        var g = c.G; float fh = g.FH, x1 = g.L0 + 2 * fh, x3 = x1 + C1w + fh, w3 = (GpxGeometry.W - fh) / 5;
        var car = w.Car;
        var boxes = new List<GpxBox>
        {
            Bx(x1, g.Row(5), C1w, fh, GpxTextures.Red, GpxFormat.Winner, ink: Wh), Bx(x3, g.Row(5), w3, fh, GpxTextures.Black, GpxFormat.LapTime(w.TotalSeconds), GpxAlign.Right, Wh),
            Bx(g.L0, g.Row(4), 2 * fh, 2 * fh, GpxTextures.White, group: 1, img: GpxImage.Flag), Bx(x1, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car), group: 1),
            Bx(x3, g.Row(4), w3, fh, GpxTextures.Black, GpxFormat.Distance(w.DistanceKm), GpxAlign.Right, Wh, 1),
            Bx(x1, g.Row(3), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 2),
            Bx(x3, g.Row(3), w3, fh, GpxTextures.Black, GpxFormat.AvgSpeed(w.AvgKmh), GpxAlign.Right, Wh, 2),
            Logo(g.L0, g.Row(6), C1w, fh),
        };
        if (car.TyreSupplier is "M" or "B")
            boxes.Insert(6, Bx(x1 + C1w - fh, g.Row(3), fh, fh, GpxTextures.White, group: 2, img: car.TyreSupplier == "M" ? GpxImage.TyreM : GpxImage.TyreB));
        return new GpxScreen("main", $"{Key0}win:{car.Index}", boxes);
    }

    /// <summary>Caixa de posicao 1.5FH x 2FH (positions_button; leader_button se P1) com o numero em FONT_BIG.</summary>
    static GpxBox PosBig(float l, float t, float fh, int pos, int group)
        => Bx(l, t, 1.5f * fh, 2 * fh, pos == 1 ? GpxTextures.Leader : GpxTextures.Positions, Pos(pos), GpxAlign.Center, Wh, group, 0, GpxFontRole.Big);

    /// <summary>4.8 Riding with: nome/equipe e a posicao (corrida: do jogo; fora: na tabela).</summary>
    GpxScreen RidingWith(GpxContext c, CarSnapshot car, string key)
    {
        var g = c.G; float fh = g.FH;
        var boxes = new List<GpxBox> { Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car)) };
        int pos = c.PositionOf(car);
        if (pos > 0) boxes.Add(PosBig(g.L0 + C1w, g.Row(4), fh, pos, 0));
        boxes.Add(Bx(g.L0, g.Row(3), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 1));
        boxes.Add(Logo(g.L0, g.Row(5), C1w, fh));
        return new GpxScreen("main", Key0 + key, boxes);
    }

    /// <summary>4.9 Cartao generico: nome/equipe, melhor tempo (preto) e gap para o 1o ou volta (amarelo), posicao.</summary>
    GpxScreen GenericCard(GpxContext c, CarSnapshot car, string key)
    {
        var g = c.G; float fh = g.FH, c2 = 2 * g.L0, c2w = C1w - fh;
        var row = c.Model.Quali?.Row(car.Index);
        string txt3 = row?.BestLap is { } best ? GpxFormat.LapTime(best) : GpxFormat.InvalidLap;
        string txt4 = row?.GapToFirst is { } gap && row.Rank > 1 ? GpxFormat.SignedGap(gap) : GpxFormat.Lap(Math.Max(1, car.CurrentLap));
        int pos = c.PositionOf(car);
        var boxes = new List<GpxBox>
        {
            Bx(g.L0, g.Row(4), C1w, fh, GpxTextures.White, c.Name(car)), Bx(c2, g.Row(4), c2w, fh, GpxTextures.Black, txt3, GpxAlign.Right, Wh),
            Bx(g.L0, g.Row(3), C1w, fh, GpxTextures.Blue, GpxContext.Team(car), ink: Wh, group: 1), Bx(c2, g.Row(3), c2w, fh, GpxTextures.Yellow, txt4, GpxAlign.Right, K, 1),
        };
        if (pos > 0) boxes.Insert(2, PosBig(c2 + c2w, g.Row(4), fh, pos, 0));
        return new GpxScreen("main", key, boxes);
    }

    /// <summary>4.10 Split de quali: nome, rotulo do split, tempo (recuo FH), delta amarelo (mais lento) ou verde (mais rapido), posicao.</summary>
    GpxScreen QualiSplit(GpxContext c, CarSnapshot car, string label, double? time, double? delta, int pos, string key)
    {
        var g = c.G; float fh = g.FH, c2 = 2 * g.L0, c2w = C1w - fh, c3 = c2 + c2w;
        var boxes = new List<GpxBox>
        {
            Bx(g.L0, g.Row(5), C1w, fh, GpxTextures.White, c.Name(car), GpxAlign.Right), Bx(c2, g.Row(5), c2w, fh, GpxTextures.Blue, label, GpxAlign.Center, Wh),
            Bx(g.L0 + fh, g.Row(4), C1w - fh, fh, GpxTextures.Black, GpxFormat.LapTime(time), GpxAlign.Right, Wh, 1),
            Logo(g.L0, g.Row(6), C1w, fh),
        };
        if (delta is { } d)
        {
            bool faster = d < 0;
            boxes.Add(Bx(c2, g.Row(4), c2w, fh, faster ? GpxTextures.Green : GpxTextures.Yellow,
                (faster ? "-" : "+") + Math.Abs(d).ToString("0.000", CultureInfo.InvariantCulture), GpxAlign.Center, faster ? Wh : K, 1));
        }
        if (pos > 0) boxes.Add(PosBig(c3, g.Row(4), fh, pos, 1));
        return new GpxScreen("main", key, boxes);
    }

    /// <summary>Caixa FH x FH de posicao (azul; vermelha se P1).</summary>
    static GpxBox PosSmall(float l, float t, float fh, int pos, int group)
        => Bx(l, t, fh, fh, pos == 1 ? GpxTextures.Red : GpxTextures.Blue, Pos(pos), GpxAlign.Center, Wh, group);

    /// <summary>4.12 Gap para o carro a frente: [P-1][nome a frente][gap][nome em foco][P] e a variacao em amarelo embaixo do gap.</summary>
    GpxScreen GapAhead(GpxContext c, GpxGapWindow w)
    {
        var g = c.G; float fh = g.FH, c2w = C1w - fh, t = g.Row(4);
        int pf = w.Focus.Position, pa = w.Other.Position;
        var boxes = new List<GpxBox>();
        if (pf <= 1)
        {
            boxes.Add(Bx(g.L0, t, C1w, fh, GpxTextures.White, c.Name(w.Focus)));
            boxes.Add(PosSmall(g.L0 + C1w, t, fh, Math.Max(1, pf), 0));
            return new GpxScreen("main", $"{Key0}gapA:{w.Focus.Index}:{w.Pass}", boxes);
        }
        float x = g.L0;
        boxes.Add(PosSmall(x, t, fh, pa, 0)); x += fh;
        boxes.Add(Bx(x, t, C1w, fh, GpxTextures.White, c.Name(w.Other), GpxAlign.Right)); x += C1w;
        float gapX = x;
        string gap = w.Laps > 0 ? GpxFormat.LapsBehind(w.Laps) : w.Gap is { } gs ? GpxFormat.SignedGap(gs) : GpxFormat.Tenths(w.Running(c.Now));
        boxes.Add(Bx(x, t, c2w, fh, GpxTextures.Black, gap, GpxAlign.Center, Wh)); x += c2w;
        boxes.Add(Bx(x, t, C1w, fh, GpxTextures.White, c.Name(w.Focus))); x += C1w;
        boxes.Add(PosSmall(x, t, fh, pf, 0));
        if (w.Change is { } ch && w.Laps == 0) boxes.Add(Bx(gapX, g.Row(3), c2w, fh, GpxTextures.Yellow, GpxFormat.Change(ch), GpxAlign.Center, K, 1));
        return new GpxScreen("main", $"{Key0}gapA:{w.Focus.Index}:{w.Pass}", boxes);
    }

    /// <summary>4.13 Gap para o carro de tras (espelho do 4.12): cronometro correndo ate o de tras cruzar, depois o gap congelado.</summary>
    GpxScreen? GapBehind(GpxContext c, GpxGapWindow w)
    {
        var g = c.G; float fh = g.FH, c2w = C1w - fh, t = g.Row(4);
        int pf = w.Focus.Position, pb = w.Other.Position;
        if (pf <= 0 || pf >= c.Session.Cars.Count(x => x.Position > 0)) return null;
        var boxes = new List<GpxBox>();
        float x = g.L0;
        boxes.Add(PosSmall(x, t, fh, pf, 0)); x += fh;
        boxes.Add(Bx(x, t, C1w, fh, GpxTextures.White, c.Name(w.Focus), GpxAlign.Right)); x += C1w;
        float gapX = x;
        string gap = w.Laps > 0 ? GpxFormat.LapsBehind(w.Laps) : w.Gap is { } gs ? GpxFormat.SignedGap(gs) : GpxFormat.Tenths(w.Running(c.Now));
        boxes.Add(Bx(x, t, c2w, fh, GpxTextures.Black, gap, GpxAlign.Center, Wh)); x += c2w;
        boxes.Add(Bx(x, t, C1w, fh, GpxTextures.White, c.Name(w.Other))); x += C1w;
        boxes.Add(PosSmall(x, t, fh, pb, 0));
        if (w.Change is { } ch && w.Gap is not null && w.Laps == 0) boxes.Add(Bx(gapX, g.Row(3), c2w, fh, GpxTextures.Yellow, GpxFormat.Change(ch), GpxAlign.Center, K, 1));
        return new GpxScreen("main", $"{Key0}gapB:{w.Focus.Index}:{w.Pass}", boxes);
    }

    /// <summary>4.14 Speed trap: nome/velocidade agora/melhor do carro em foco, rotulos, e o mais rapido do trap na sessao.</summary>
    GpxScreen SpeedTrap(GpxContext c, GpxTrapWindow w)
    {
        var g = c.G; float fh = g.FH, c2 = 2 * g.L0, c2w = C1w - fh, c3 = c2 + c2w;
        bool mph = false;
        var boxes = new List<GpxBox>
        {
            Bx(g.L0, g.Row(5), C1w, fh, GpxTextures.White, c.Name(w.Focus), GpxAlign.Right), Bx(c2, g.Row(5), c2w, fh, GpxTextures.Blue, GpxFormat.TopSpeeds, ink: Wh),
        };
        if (w.Top is { } top) boxes.Add(Bx(c3, g.Row(5), C1w, fh, GpxTextures.White, c.Name(top)));
        boxes.Add(Bx(g.L0 + fh, g.Row(4), C1w - fh, fh, GpxTextures.Black, GpxFormat.Speed(w.SpeedMps, mph), GpxAlign.Right, Wh, 1));
        boxes.Add(Bx(c2, g.Row(4), c2w, fh, GpxTextures.Blue, GpxFormat.CurrentSpeed, ink: Wh, group: 1));
        if (w.Top is not null && w.TopMps > 0) boxes.Add(Bx(c3, g.Row(4), C1w - fh, fh, GpxTextures.Black, GpxFormat.Speed(w.TopMps, mph), ink: Wh, group: 1));
        if (w.FocusBestMps > 0) boxes.Add(Bx(g.L0 + fh, g.Row(3), C1w - fh, fh, GpxTextures.Black, GpxFormat.Speed(w.FocusBestMps, mph), GpxAlign.Right, Wh, 2));
        boxes.Add(Logo(g.L0, g.Row(6), C1w, fh));
        return new GpxScreen("main", $"{Key0}trap:{w.Focus.Index}:{w.OpenT:0.000}", boxes);
    }

    /// <summary>Uma linha do standings: posicao (null = sem caixa), nome e texto da coluna de gap.</summary>
    readonly record struct StandRow(CarSnapshot Car, int? Pos, string Gap);

    /// <summary>
    /// 4.11 Standings 2 x 4 por pagina: linha i em y = 768 - 6FH + (i%4)FH, x = L0 + (i/4)(6FH + W/5); colunas animadas
    /// 0 posicao (FH, azul / vermelha P1), 1 nome (W/5 branco), 2 gap (4FH preto, a direita). Logo em (FH + W/5, 768 - 7FH).
    /// </summary>
    GpxScreen Standings(GpxContext c, string kind, IReadOnlyList<StandRow> rows, int? fixedPage = null)
    {
        var g = c.G; float fh = g.FH;
        int per = CarsPerPage, page = fixedPage ?? c.PageOf(rows.Count, per);
        var boxes = new List<GpxBox>();
        var slice = rows.Skip(page * per).Take(per).ToList();
        for (int i = 0; i < slice.Count; i++)
        {
            float y = GpxGeometry.H - 6 * fh + (i % 4) * fh, x0 = g.L0 + (i / 4) * (6 * fh + C1w);
            var r = slice[i];
            if (r.Pos is { } p) boxes.Add(PosSmall(x0, y, fh, p, 0));
            boxes.Add(Bx(x0 + fh, y, C1w, fh, GpxTextures.White, c.Name(r.Car), group: 1));
            boxes.Add(Bx(x0 + fh + C1w, y, 4 * fh, fh, GpxTextures.Black, r.Gap, GpxAlign.Right, Wh, 2));
        }
        boxes.Add(Logo(fh + C1w, GpxGeometry.H - 7 * fh, C1w, fh));
        return new GpxScreen("main", $"{Key0}{kind}", boxes, GpxAnimKind.Columns, page, 40);
    }

    static bool Out(CarSnapshot car) => car.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;

    /// <summary>Corrida: lider "Lap n", demais "+ n Lap(s)" ou o gap em tempo sem "+" (spec 4.11); fora da corrida "  Out" sem caixa de posicao.</summary>
    GpxScreen RaceStandings(GpxContext c, int? page = null)
    {
        var rows = RaceRows(c).Select(x => Out(x.Car) ? new StandRow(x.Car, null, GpxFormat.Out)
            : x.Car.Position == 1 ? new StandRow(x.Car, 1, GpxFormat.Lap(Math.Max(1, x.Car.CurrentLap)))
            : new StandRow(x.Car, x.Car.Position, x.LapsBehind > 0 ? GpxFormat.LapsBehind(x.LapsBehind) : x.GapToLeader is { } gp ? GpxFormat.LapTime(gp) : "")).ToList();
        return Standings(c, "stR", rows, page);
    }

    GpxScreen PitStopsStandings(GpxContext c, int? page = null)
    {
        var b = c.Broadcast;
        var rows = RaceRows(c).Select(x => new StandRow(x.Car, Out(x.Car) ? null : x.Car.Position, GpxFormat.Stops(b.StopsOf(x.Car.Index)))).ToList();
        return Standings(c, "stP", rows, page);
    }

    static IReadOnlyList<StandingRow> RaceRows(GpxContext c)
    {
        if (c.Model.Standings.Count > 0) return c.Model.Standings;
        return c.Session.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).Select(x => new StandingRow(x, null, 0, x.IsPlayer)).ToList();
    }

    /// <summary>Classificacao: 1o com o melhor tempo, demais "+ gap"; sem tempo = sem caixa de posicao. Ate 22 carros.</summary>
    GpxScreen QualiStandings(GpxContext c, int? page = null)
    {
        var q = c.Model.Quali ?? QualiTableState.Empty;
        var rows = q.Rows.Take(22).Select(r => !r.HasTime ? new StandRow(r.Car, null, "")
            : r.Rank == 1 ? new StandRow(r.Car, 1, GpxFormat.LapTime(r.BestLap))
            : new StandRow(r.Car, r.Rank, GpxFormat.SignedGap(r.GapToFirst ?? 0))).ToList();
        return Standings(c, "stQ", rows, page);
    }

    // ---------- previa (--png): forca uma tela com os dados do modelo e exemplos onde o evento nao aconteceu ----------

    GpxScreen? Preview(GpxContext c)
    {
        var cars = c.Session.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).ToList();
        if (cars.Count == 0) return null;
        var focus = c.Focus ?? cars[0];
        CarSnapshot At(int pos) => cars[Math.Clamp(pos - 1, 0, cars.Count - 1)];
        double now = c.Now;
        string name = c.Options.PreviewScreen!.Trim();
        return name.ToLowerInvariant() switch
        {
            "time" => TimeLeft(c, force: true),
            "fl" => FastestLap(c, At(1), At(1).BestLapTime > 0 ? At(1).BestLapTime : 81.443, "fl"),
            "out" => OutOfRace(c, At(2), "out"),
            "pit" => InPits(c, At(3), "pit"),
            "pitt" => PitTimer(c, focus, 12.3),
            "dt" => DriveThrough(c, At(2)),
            "win" => Winner(c, c.Broadcast.Winner ?? new WinnerInfo(At(1), 5534.532, 305.28, 198.721, now)),
            "ride" => RidingWith(c, focus, "ride"),
            "cap" => Caption(c, focus),
            "split" => QualiSplit(c, focus, GpxFormat.Intermediate(1), 81.112, 0.204, 4, "split"),
            "splitgreen" => QualiSplit(c, focus, GpxFormat.FinishLine, 80.874, -0.112, 1, "split"),
            "card" => GenericCard(c, focus, "card"),
            "gapa" => GapAhead(c, new GpxGapWindow(true, At(3) with { Position = 3 }, At(2) with { Position = 2 }, 7, now - 1, now + 7, 1.842, 0, -0.12)),
            "gapb" => GapBehind(c, new GpxGapWindow(false, At(3) with { Position = 3 }, At(4) with { Position = 4 }, 7, now - 0.9, now + 7, null, 0, null)),
            "trap" => SpeedTrap(c, new GpxTrapWindow(focus, 0, 298.4 / 3.6, 305.1 / 3.6, At(1), 312.7 / 3.6, now - 1, now + 7)),
            "str" => RaceStandings(c),
            "stp" => PitStopsStandings(c),
            "stq" => QualiStandings(c),
            _ => null,
        };
    }
}
