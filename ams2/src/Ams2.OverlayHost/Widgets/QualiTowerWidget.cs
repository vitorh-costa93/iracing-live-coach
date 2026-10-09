using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Torre de classificação (tabela de melhores voltas, <see cref="QualiTable"/>). Por enquanto só no tema 2018 (ref. quali-2018-tower-*.jpg
/// e quali-2018-sheet-1.jpg): cabeçalho preto "Q" + relógio da sessão (vermelho nos últimos 60 s; xadrez translúcido sob a bandeirada),
/// filete vermelho, linhas [caixa de posição][SIGLA][coluna clara]: o 1º mostra o tempo ("1:23.266"), os demais "+0.056"; quem ainda
/// não tem tempo mostra OUT LAP / NO TIME / IN PIT (ciano) no lugar do valor (com tempo, o tempo/gap fica sempre); volta recém-melhorada em verde (roxo no 1º) por <see cref="ImprovedHold"/> s.
/// Com <c>eliminationFrom</c> &gt; 0: bloco "ELIMINATION ZONE" (caixas vermelhas) e cartão "DRIVER AT RISK" (piloto na última posição
/// segura, sem retrato; a zona abaixo dele mostra a diferença para ele, como na TV). Modo "fastesttyre": título "FASTEST TYRE", coluna
/// de composto em círculo e tempos em décimos — o AMS2 só informa o composto do jogador, os outros ficam com "-".
/// Janela de tamanho fixo para as opções: a reserva do bloco de eliminação e do cartão fica transparente quando sem uso.
/// 2004 (ref. quali-2004-tower-clock.jpg): torre mínima de siglas — 1ª linha [1 vermelho][SIGLA branca][tempo do líder em caixa preta],
/// abaixo só [posição][sigla] (topo + ao redor do jogador), número vermelho em caixa clara na zona de eliminação, estado em texto pequeno
/// ao lado da sigla, e a caixa do relógio "Q | m:ss" (rótulo escuro + relógio em caixa branca) à direita da 1ª linha.
/// 1998 (ref. quali-1998-classification-list.jpg): lista em colunas [caixa amarela][NOME] com o tempo do 1º ("1:15.259") e a diferença
    /// sem "+" ("0.036") nos demais; rodapé 1920x300 em duas colunas de quatro linhas, título inicial "CLASSIFICATION".
/// </summary>
public sealed class QualiTowerWidget : IWidget
{
    public string Id => "qualitower";
    WidgetSettings _cfg = new() { Id = "qualitower" };
    ThemeStyle _style = ThemeStyle.Broadcast98;
    bool _b18 => _style == ThemeStyle.Modern2018;

    public void UseTheme(Theme.Theme theme)
    {
        if (_style != theme.Style) { _topMotion18.Reset(); _zoneMotion18.Reset(); _riskMotion18.Reset(); _riskId18 = -1; }
        _style = theme.Style;
    }

    public void Configure(WidgetSettings s) { _cfg = s; }

    public (float Width, float Height) DesignSize => _style switch
    {
        ThemeStyle.Modern2018 => Size18,
        ThemeStyle.Broadcast2000s => Size04,
        _ => Size98,
    };

    // ---- Opções (WidgetCatalog.OptionsFor("f1-2018", "qualitower")) ----
    int Num(string id, int def, int min, int max)
        => double.TryParse(_cfg.OptionOr(id, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? (int)Math.Clamp(Math.Round(v), min, max) : def;
    bool Flag(string id, bool def) => _cfg.Option(id) is { } v ? !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) : def;
    int TopRows => _style switch
    {
        ThemeStyle.Modern2018 => Num("rows", 10, 5, 20),
        ThemeStyle.Broadcast2000s => Num("rows", 5, 1, 20),
        _ => Num("rows", 6, 2, 10),
    };
    int Columns98 => _cfg.OptionOr("columns", "2") == "1" ? 1 : 2;
    int NearRows => Num("nearCount", 3, 0, 10);
    int Cutoff => Num("eliminationFrom", 0, 0, 30);
    bool TyreMode => string.Equals(_cfg.OptionOr("mode", "time"), "fastesttyre", StringComparison.OrdinalIgnoreCase);
    bool ShowClock => Flag("showClock", true);
    bool ShowAtRisk => Flag("showAtRisk", true);

    // ---- Geometria 2018 (mesmo vocabulário da torre de corrida, StandingsWidget._b18) ----
    const float M = 34, HeadW = 168, HeadH = 78, Rule = 4, Pad = 5, Pitch = 40, Box = 32, SepH = 14;
    const float BoxW = 32, NameW = 78, TyreW = 34, TimeW = 118, TitleH = 32;
    const float ElimGap = 6, ElimRule = 3, ElimTitleH = 30, CardTitleH = 28, CardNameH = 38, CardTimeH = 40;
    const int ElimTop = 2, ElimNear = 3, ElimRows = ElimTop + ElimNear;
    /// <summary>Segundos em que a volta recém-melhorada fica em verde (aproximado pela distância desde a linha / velocidade).</summary>
    public const double ImprovedHold = 5;
    const uint FlagChequered = 11;   // FLAG_COLOUR_CHEQUERED do SharedMemory.h

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 ZoneFill = Rgb(178, 18, 28), Green = Rgb(37, 194, 58), White = Rgb(255, 255, 255);

    int SafeCapacity => TopRows + Math.Max(NearRows, 1);
    float CardH => ElimRule + CardTitleH + CardNameH + CardTimeH;
    /// <summary>Colunas: caixa, sigla, composto (só no modo fastesttyre), tempo; largura do painel (sem a margem do marcador).</summary>
    (float Box, float Name, float Tyre, float Time, float Width) Cols()
    {
        float x = M + 6, box = x;
        x += BoxW + 10;
        float name = x; x += NameW + 6;
        float tyre = 0;
        if (TyreMode) { tyre = x; x += TyreW + 6; }
        float time = x; x += TimeW;
        return (box, name, tyre, time, Math.Max(x - M, HeadW));
    }

    float SafeBodyH => 2 * Pad + SafeCapacity * Pitch + (TopRows > 0 ? SepH : 0);
    float ElimH => Cutoff > 0 ? ElimGap + (ShowAtRisk ? CardH : ElimRule + ElimTitleH) + 2 * Pad + ElimRows * Pitch + SepH : 0;

    (float, float) Size18 => (M + Cols().Width, HeadH + Rule + (TyreMode ? TitleH : 0) + SafeBodyH + ElimH);

    // ---- Formatação (públicas para teste) ----

    /// <summary>Relógio da sessão: "m:ss" (ou "h:mm:ss"); null = "-:--".</summary>
    public static string Clock(double? seconds)
    {
        if (seconds is not { } s || !double.IsFinite(s)) return "-:--";
        int t = (int)Math.Ceiling(Math.Max(0, s) - 1e-6);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    /// <summary>Letra do composto (S/M/H/I/W) a partir do texto do jogo (inglês ou traduzido); "-" sem dado.</summary>
    public static string CompoundLetter(string? compound)
    {
        string c = (compound ?? "").Trim().ToLowerInvariant();
        if (c.Length == 0) return "-";
        if (c.Contains("inter")) return "I";
        if (c.Contains("wet") || c.Contains("rain") || c.Contains("chuva") || c.Contains("molhad")) return "W";
        if (c.Contains("soft") || c.Contains("macio") || c.Contains("mole")) return "S";
        if (c.Contains("med")) return "M";
        if (c.Contains("hard") || c.Contains("duro")) return "H";
        return "-";
    }

    static Color4 CompoundColor(string letter) => letter switch
    {
        "S" => Rgb(225, 6, 0),
        "M" => Rgb(250, 205, 0),
        "H" => Rgb(240, 240, 240),
        "I" => Rgb(40, 180, 60),
        "W" => Rgb(30, 110, 230),
        _ => Rgb(130, 132, 140),
    };

    /// <summary>Volta recém-melhorada: a última volta é a melhor e o carro cruzou a linha há menos de <see cref="ImprovedHold"/> s.</summary>
    static bool JustImproved(QualiRow r)
    {
        var c = r.Car;
        if (r.Status != QualiStatus.TimeSet || c.LastLapTime <= 0 || Math.Abs(c.LastLapTime - c.BestLapTime) > 1e-4) return false;
        return c.SpeedMps > 1 && c.LapDistance / c.SpeedMps < ImprovedHold;
    }

    string LapText(double t) => TyreMode ? Tenths(t) : _cfg.Fmt.FormatLapTime(t);
    string GapText(double g) => TyreMode ? "+" + (Math.Floor(Math.Max(0, g) * 10 + 1e-6) / 10).ToString("0.0", CultureInfo.InvariantCulture) : _cfg.Fmt.FormatGap(Math.Max(0, g));

    /// <summary>Tempo em décimos ("1:22.8", truncado como na TV).</summary>
    public static string Tenths(double t)
    {
        double v = Math.Floor(t * 10 + 1e-6) / 10;
        int min = (int)(v / 60);
        double sec = v - min * 60;
        return min > 0 ? $"{min}:{sec.ToString("00.0", CultureInfo.InvariantCulture)}" : sec.ToString("0.0", CultureInfo.InvariantCulture);
    }

    // ---- Desenho ----

    readonly FieldCodes _codes = new();
    readonly Broadcast18RowMotion _topMotion18 = new(), _zoneMotion18 = new();
    readonly Broadcast18Motion _riskMotion18 = new();
    int _riskId18 = -1;
    double _riskAt18 = double.NaN;
    readonly QualiOutLapPresentation _presentation = new();

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected) { _presentation.Reset(); return; }
        if (m.Session is { InSession: false }) return;
        bool preview = Flag("layoutPreview", false);
        var stage = preview ? QualiBoardStage.Tower : _presentation.Update(m.QualiLap, m.Now, m.Quali?.Rows.Count ?? 0,
            _style == ThemeStyle.Broadcast98 ? Num("rows", 8, 2, 10) : int.MaxValue, m.Session?.Track);
        if (stage == QualiBoardStage.Caption) { QualiCaption.Draw(c, m, _cfg, DesignSize.Width); return; }
        if (stage != QualiBoardStage.Tower) return;
        if (m.Quali is { } qc) _codes.Update(qc.Rows.Select(r => r.Car));
        if (_style == ThemeStyle.Broadcast2000s) { Draw04(c, m); return; }
        if (_style == ThemeStyle.Broadcast98) { DrawBoard98(c, m, preview ? 5 : _presentation.Age); return; }
        var t = c.Theme;
        var L = Cols();
        float tw = L.Width, x0 = M;
        var q = m.Quali;
        var s = m.Session;
        bool chequered = s is not null && (s.FlagColour == FlagChequered || s.TimeRemainingSeconds is <= 0);
        DrawHeader(c, t, x0, chequered, q?.TimeRemaining ?? s?.TimeRemainingSeconds);
        c.FillRect(x0, HeadH, tw, Rule, t.AccentBar);
        float by = HeadH + Rule;
        if (TyreMode)
        {
            c.FillRect(x0, by, tw, TitleH, t.PanelFill);
            c.Text("FASTEST TYRE", t.Title with { Size = 20, Tracking = 0.5f }, x0, by, tw, TitleH, t.TitleColor, HAlign.Center);
            by += TitleH;
        }
        if (!m.Connected || q is null || q.Rows.Count == 0)
        {
            _topMotion18.Reset(); _zoneMotion18.Reset(); _riskMotion18.Reset(); _riskId18 = -1;
            c.FillRect(x0, by, tw, Pitch + 2 * Pad, t.PanelFill);
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", BroadcastUi.Fit(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, tw - 20), x0 + 10, by + Pad, tw - 20, Pitch, t.LabelColor);
            return;
        }

        var rows = q.Rows;
        int cut = Cutoff;
        // Cartão DRIVER AT RISK: o piloto na última posição segura (cut - 1); ele sai da lista de cima, como na TV.
        int riskRank = ShowAtRisk && cut >= 2 && cut - 1 <= rows.Count ? cut - 1 : 0;
        int safeEnd = cut > 0 ? Math.Min(rows.Count, (riskRank > 0 ? riskRank : cut) - 1) : rows.Count;   // nº de linhas da lista de cima
        var safe = rows.Take(Math.Max(0, safeEnd)).ToList();
        var zone = cut > 0 ? rows.Where(r => r.Rank >= cut).ToList() : [];
        var risk = riskRank > 0 ? rows[riskRank - 1] : null;
        var field = rows.Select(r => r.Car).ToList();
        string tyrePlayer = CompoundLetter(m.Session?.Player?.Wheels is { Count: > 0 } w ? w[0].Compound : null);

        // Lista de cima: topo + janela ao redor do jogador (regra do V3/StandingsSelector).
        var picks = StandingsSelector.Select(safe.Count, safe.FindIndex(r => r.IsPlayer), TopRows, NearRows);
        float bodyH = 2 * Pad + picks.Count * Pitch + picks.Count(p => p.GapBefore) * SepH;
        if (safe.Count > 0)
        {
            c.FillRect(x0, by, tw, bodyH, t.PanelFill);
            c.FillRect(L.Time, by, x0 + tw - L.Time, bodyH, t.GapCellFill);
            float y = by + Pad + (Pitch - Box) / 2;
            var targets = new Dictionary<int, float>();
            float targetY = y;
            foreach (var p in picks)
            {
                if (p.GapBefore) targetY += SepH;
                targets[safe[p.Index].Car.Index] = targetY;
                targetY += Pitch;
            }
            var context = (s?.Kind, s?.Track, s?.TrackVariation, BroadcastUi.State(m).SessionSeenT, cut,
                string.Join("|", targets.Keys.Order()));
            var positions = _topMotion18.Evaluate(m.Now, context, targets);
            foreach (var p in picks)
            {
                if (p.GapBefore) { Dots(c, t, L, y); y += SepH; }
                DrawRow(c, t, L, safe[p.Index], positions[safe[p.Index].Car.Index], chequered, false, null, tyrePlayer);
                y += Pitch;
            }
            by += bodyH;
        }
        if (cut <= 0) return;

        // Cartão do piloto em risco (no lugar do título) ou título "ELIMINATION ZONE"; depois a zona (caixas vermelhas).
        by += ElimGap;
        c.FillRect(x0, by, tw, ElimRule, t.AccentBar);
        by += ElimRule;
        double? reference = null;
        if (risk is not null)
        {
            if (_riskId18 != risk.Car.Index)
            {
                _riskAt18 = _riskId18 < 0 ? m.Now - Broadcast18Motion.EntrySeconds : m.Now;
                _riskId18 = risk.Car.Index;
                _riskMotion18.Reset();
            }
            float cardH = CardTitleH + CardNameH + CardTimeH;
            float reveal = _riskMotion18.Evaluate(m.Now, _riskAt18, double.PositiveInfinity, BroadcastUi.State(m).SessionSeenT);
            float cardY = by;
            BroadcastUi.WithReveal18(c, reveal, tw, cardH, () => DrawCard(c, t, L, x0, tw, cardY, risk, field), x: x0, y: cardY);
            by += cardH;
            reference = risk.BestLap;
        }
        else
        {
            c.FillRect(x0, by, tw, ElimTitleH, t.PanelFill);
            c.Text("ELIMINATION ZONE", BroadcastUi.Fit(c, "ELIMINATION ZONE", t.Title with { Size = 18, Tracking = 0.5f }, tw - 12), x0, by, tw, ElimTitleH, t.TitleColor, HAlign.Center);
            by += ElimTitleH;
        }
        if (zone.Count == 0) return;
        var zp = StandingsSelector.Select(zone.Count, zone.FindIndex(r => r.IsPlayer), ElimTop, ElimNear);
        float zh = 2 * Pad + zp.Count * Pitch + zp.Count(p => p.GapBefore) * SepH;
        c.FillRect(x0, by, tw, zh, t.PanelFill);
        c.FillRect(L.Time, by, x0 + tw - L.Time, zh, t.GapCellFill);
        float zy = by + Pad + (Pitch - Box) / 2;
        var zoneTargets = new Dictionary<int, float>();
        float zoneY = zy;
        foreach (var p in zp)
        {
            if (p.GapBefore) zoneY += SepH;
            zoneTargets[zone[p.Index].Car.Index] = zoneY;
            zoneY += Pitch;
        }
        var zoneContext = (s?.Kind, s?.Track, s?.TrackVariation, BroadcastUi.State(m).SessionSeenT, cut,
            string.Join("|", zoneTargets.Keys.Order()));
        var zonePositions = _zoneMotion18.Evaluate(m.Now, zoneContext, zoneTargets);
        foreach (var p in zp)
        {
            if (p.GapBefore) { Dots(c, t, L, zy); zy += SepH; }
            DrawRow(c, t, L, zone[p.Index], zonePositions[zone[p.Index].Car.Index], chequered, true, reference, tyrePlayer);
            zy += Pitch;
        }
    }

    static void Dots(ThemeCanvas c, Theme.Theme t, (float Box, float Name, float Tyre, float Time, float Width) L, float y)
    {
        float cy = y + SepH / 2 - (Pitch - Box) / 2;
        for (int k = -1; k <= 1; k++) c.FillEllipse(L.Box + BoxW / 2 + k * 8, cy, 2.2f, 2.2f, t.LabelColor);
    }

    /// <summary>Cabeçalho: "Q" + relógio (vermelho nos últimos 60 s); sob a bandeirada, xadrez translúcido atrás. Sem relógio: "QUALIFYING".</summary>
    void DrawHeader(ThemeCanvas c, Theme.Theme t, float x0, bool chequered, double? remaining)
    {
        float hw = HeadW;
        c.FillRoundRect(x0, 0, hw, HeadH, 7, t.PanelFill);
        c.FillRect(x0, HeadH - 8, hw, 8, t.PanelFill);
        if (chequered)
            BroadcastUi.WithAlpha(c, 0.42f, () =>
            {
                float hh = HeadH / 4;
                for (int k = 0; k < 4; k += 2) Chrome.Checkered(c, x0, k * hh, hw, 2 * hh);
            });
        if (!ShowClock)
        {
            var f = BroadcastUi.Fit(c, "QUALIFYING", t.Title with { Size = 24, Tracking = 1.5f }, hw - 18);
            c.Text("QUALIFYING", f, x0, 0, hw, HeadH, t.TitleColor, HAlign.Center);
            return;
        }
        c.Text("Q", t.Title with { Size = 28, Tracking = 4 }, x0 + 2, 2, hw, 36, t.TitleColor, HAlign.Center);
        c.FillRect(x0 + 34, 40, hw - 68, 1.2f, new Color4(1f, 1f, 1f, 0.45f));
        bool red = chequered || remaining is { } r && r < 60;
        c.Text(Clock(remaining), t.Numbers with { Element = "time", Size = 26 }, x0, 42, hw, 32, red ? t.AccentBar : t.ValueColor, HAlign.Center);
    }

    /// <summary>Uma linha; <paramref name="y"/> = topo da caixa. <paramref name="reference"/> = tempo base dos gaps (null = o do 1º).</summary>
    void DrawRow(ThemeCanvas c, Theme.Theme t, (float Box, float Name, float Tyre, float Time, float Width) L, QualiRow r, float y,
        bool chequered, bool inZone, double? reference, string tyrePlayer)
    {
        bool improved = JustImproved(r);
        // À esquerda, fora do painel: mini xadrez de quem já recebeu a bandeirada; senão o marcador roxo da melhor volta geral.
        if (chequered && r.Car.RaceState == RaceState.Finished) Chrome.Checkered(c, 6, y + 8, 22, 16);
        else if (r.Rank == 1 && r.HasTime) Chrome.FastestMarker(c, 0, y, Box);
        Color4? fill = improved ? (r.Rank == 1 ? t.FastestFill : Green) : inZone ? ZoneFill : null;
        Chrome.PosBox(c, L.Box, y, BoxW, Box, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 21 }, fill);
        string nm = _cfg.Name(r.Car, _codes);
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, NameW + 4), L.Name, y, NameW + 8, Box, r.IsPlayer ? t.PlayerColor : t.TextColor);
        if (TyreMode)
        {
            string letter = r.IsPlayer ? tyrePlayer : "-";
            float cx = L.Tyre + TyreW / 2, cy = y + Box / 2, rad = 13;
            var col = CompoundColor(letter);
            c.FillEllipse(cx, cy, rad, rad, Rgb(8, 8, 10));
            if (letter != "-") c.StrokeEllipse(cx, cy, rad - 1.5f, rad - 1.5f, col, 3f);
            c.Text(letter, t.Label with { Size = 16, Weight = 700 }, cx - rad, cy - rad - 1, 2 * rad, 2 * rad, letter == "-" ? col : White, HAlign.Center);
        }
        var (text, ink) = Value(t, r, improved, reference);
        c.Text(text, BroadcastUi.Fit(c, text, t.Numbers with { Element = !r.HasTime ? "label" : improved || (reference is null && (r.Rank == 1 || r.GapToFirst is null)) ? "time" : "gap" }, TimeW - 14), L.Time, y, TimeW - 10, Box, ink, HAlign.Right);
    }

    (string, Color4) Value(Theme.Theme t, QualiRow r, bool improved, double? reference)
    {
        // Como na TV: quem tem tempo mostra sempre o tempo/gap; OUT LAP / NO TIME / IN PIT (ciano) só para quem ainda não tem.
        if (r.BestLap is not { } best) return (StateText(r) ?? "NO TIME", r.Status == QualiStatus.InPit ? t.PitTimeColor : t.ValueColor);
        // Volta recém-melhorada: o tempo inteiro em verde (roxo se virou o melhor geral), como "1:23.420" na TV.
        if (improved) return (LapText(best), r.Rank == 1 ? Rgb(200, 120, 255) : Green);
        if (reference is { } refT) return (GapText(best - refT), t.ValueColor);
        return r.Rank == 1 || r.GapToFirst is null ? (LapText(best), t.ValueColor) : (GapText(r.GapToFirst.Value), t.ValueColor);
    }

    /// <summary>Cartão "DRIVER AT RISK": título, caixa vermelha + sobrenome, e o tempo grande à direita (sem retrato no AMS2). Devolve o y seguinte.</summary>
    float DrawCard(ThemeCanvas c, Theme.Theme t, (float Box, float Name, float Tyre, float Time, float Width) L, float x0, float tw, float y, QualiRow r, IReadOnlyList<CarSnapshot> field)
    {
        c.FillRect(x0, y, tw, CardTitleH + CardNameH + CardTimeH, t.PanelFill);
        c.Text("DRIVER AT RISK", BroadcastUi.Fit(c, "DRIVER AT RISK", t.Title with { Size = 17, Tracking = 0.5f }, tw - 12), x0, y, tw, CardTitleH, t.TitleColor, HAlign.Center);
        y += CardTitleH;
        float by = y + (CardNameH - Box) / 2;
        Chrome.PosBox(c, L.Box, by, BoxW, Box, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 21 }, ZoneFill);
        string nm = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
        float nw = x0 + tw - 10 - L.Name;
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, nw), L.Name, by, nw + 8, Box, r.IsPlayer ? t.PlayerColor : t.TextColor);
        y += CardNameH;
        c.FillRect(x0, y, tw, CardTimeH, t.SubPanelFill);
        string v = r.BestLap is { } b ? LapText(b) : StateText(r) ?? "NO TIME";
        var vf = t.Numbers with { Element = "time", Size = 28 };
        c.Text(v, BroadcastUi.Fit(c, v, vf, tw - 24), x0, y, tw - 12, CardTimeH, !r.HasTime && r.Status == QualiStatus.InPit ? t.PitTimeColor : t.ValueColor, HAlign.Right);
        return y + CardTimeH;
    }

    /// <summary>Estado no lugar do tempo só para quem não tem tempo (<see cref="QualiRow.StateText"/>); null = mostra o tempo/gap.</summary>
    public static string? StateText(QualiRow r) => r.StateText;

    static int PlayerIndex(IReadOnlyList<QualiRow> rows) { for (int i = 0; i < rows.Count; i++) if (rows[i].IsPlayer) return i; return -1; }

    bool Chequered(OverlayModel m) => m.Session is { } s && (s.FlagColour == FlagChequered || s.TimeRemainingSeconds is <= 0);

    // ---- 2004–2008 (ref. quali-2004-tower-clock.jpg; mesmo vocabulário da mini-torre de corrida, StandingsWidget.DrawRow2000s) ----
    const float X04 = 4, Top04 = 4, Pos04 = 40, Name04 = 82, Time04 = 124, H04 = 34, Pitch04 = 36, Lead04 = 6;
    const float ClockGap04 = 16, ClockLab04 = 40, Clock04 = 88;
    int Capacity04 => TopRows + Math.Max(NearRows, 1);
    const float ClockX04 = X04 + Pos04 + Name04 + Time04 + ClockGap04;
    (float, float) Size04 => ((ShowClock ? ClockX04 + ClockLab04 + Clock04 : X04 + Pos04 + Name04 + Time04) + 6,
        Top04 + Pitch04 + (Capacity04 > 1 ? Lead04 + (Capacity04 - 1) * Pitch04 + SepH : 0) + 2);

    // Linhas abaixo do líder: posição cinza-escura e sigla em célula cinza-clara translúcidas (a TV escurece tudo menos a 1ª linha).
    static readonly BarStop[] PosStops04 = [new(0f, Rgb(96, 96, 106, 0.9f)), new(1f, Rgb(50, 50, 58, 0.9f))];
    static readonly BarStop[] NameStops04 = [new(0f, Rgb(232, 232, 236, 0.88f)), new(0.7f, Rgb(214, 214, 220, 0.88f)), new(1f, Rgb(178, 178, 188, 0.88f))];
    static readonly BarStop[] ZoneStops04 = [new(0f, Rgb(252, 244, 244, 0.94f)), new(1f, Rgb(216, 202, 204, 0.94f))];
    static readonly Color4 Ink04 = Rgb(58, 58, 66), Red04 = Rgb(206, 22, 30), RowShade04 = new(0.04f, 0.04f, 0.08f, 0.6f);

    void Draw04(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var q = m.Quali;
        if (ShowClock) DrawClock04(c, t, Chequered(m), q?.TimeRemaining ?? m.Session?.TimeRemainingSeconds);
        if (!m.Connected || q is null || q.Rows.Count == 0)
        {
            Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", X04, Top04);
            return;
        }
        var rows = q.Rows;
        int cut = Cutoff;

        // 1ª linha: [1 vermelho][SIGLA em célula branca][tempo do líder em caixa preta] (sem tempo: o estado em texto pequeno).
        var lead = rows[0];
        float x = X04, y = Top04;
        Chrome.PositionBox(c, x, y, Pos04, H04, lead.Rank, t.Numbers with { Element = "position" });
        x += Pos04;
        string ln = _cfg.Name(lead.Car, _codes);
        Chrome.WhiteCell(c, x, y, Name04, H04, ln, BroadcastUi.Fit(c, ln, t.Text, Name04 - 16), ink: lead.IsPlayer ? Chrome.PlayerInk : null);
        x += Name04;
        if (lead.BestLap is { } best)
        {
            string lt = _cfg.Fmt.FormatLapTime(best);
            Chrome.BlackCell(c, x, y, Time04, H04, lt, BroadcastUi.Fit(c, lt, t.Numbers with { Element = "time" }, Time04 - 12));
        }
        else Chrome.BlackCell(c, x, y, Time04, H04, StateText(lead) ?? "NO TIME", t.Label with { Element = "time", Size = 16 }, HAlign.Center);

        // Abaixo: só posição + sigla do topo e da janela ao redor do jogador; o salto líder -> resto fica só no espaçamento (como na TV),
        // os outros saltos ganham os três pontos.
        var picks = StandingsSelector.Select(rows.Count, PlayerIndex(rows), TopRows, NearRows);
        y = Top04 + Pitch04 + Lead04;
        int prev = 0;
        foreach (var p in picks)
        {
            if (p.Index == 0) continue;
            if (p.GapBefore && prev != 0)
            {
                for (int k = -1; k <= 1; k++) c.FillEllipse(X04 + Pos04 / 2 + k * 8, y + SepH / 2 - 1, 2.2f, 2.2f, t.TextColor);
                y += SepH;
            }
            DrawRow04(c, t, rows[p.Index], y, cut);
            y += Pitch04;
            prev = p.Index;
        }
    }

    void DrawRow04(ThemeCanvas c, Theme.Theme t, QualiRow r, float y, int cut)
    {
        float x = X04;
        bool zone = r.InEliminationZone(cut);
        c.FillRect(x, y + H04, Pos04 + Name04, 1.5f, RowShade04);
        c.VGradientRect(x, y, Pos04, H04, zone ? ZoneStops04 : PosStops04);
        if (zone) c.StrokeRect(x + 1, y + 1, Pos04 - 2, H04 - 2, Red04, 2f);
        c.Text(r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position" }, x, y - 1, Pos04, H04, zone ? Red04 : Rgb(240, 240, 244), HAlign.Center);
        x += Pos04;
        c.VGradientRect(x, y, Name04, H04, NameStops04);
        string nm = _cfg.Name(r.Car, _codes);
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, Name04 - 16), x + 8, y - 1, Name04 - 16, H04, r.IsPlayer ? Chrome.PlayerInk : Ink04);
        x += Name04;
        if (StateText(r) is not { } st) return;
        var f = t.Label with { Size = 15 };
        float sw = c.Measure(st, f) + 14;
        Chrome.Box(c, x, y + 5, sw, H04 - 10, st, f, Chrome.CellKind.Black, HAlign.Center, 7);
    }

    /// <summary>Caixa do relógio "Q | m:ss": rótulo escuro + relógio em caixa branca (vermelho nos últimos 60 s); bandeirada: xadrez no rótulo.</summary>
    static void DrawClock04(ThemeCanvas c, Theme.Theme t, bool chequered, double? remaining)
    {
        float x = ClockX04, y = Top04;
        if (chequered) Chrome.Checkered(c, x, y, ClockLab04, H04);
        else Chrome.Box(c, x, y, ClockLab04, H04, "Q", t.Text with { Element = "label" }, Chrome.CellKind.Black, HAlign.Center, 0);
        bool red = chequered || remaining is { } r && r < 60;
        Chrome.WhiteCell(c, x + ClockLab04, y, Clock04, H04, Clock(remaining), t.Numbers with { Element = "time" }, HAlign.Center, red ? Red04 : null);
    }

    // ---- 1998–2001 (ref. quali-1998-classification-list.jpg; vocabulário da tabela inferior, StandingsWidget.DrawTable) ----
    const float W98 = 1920, H98 = 300;
    (float, float) Size98 => (W98, H98);

    void Draw98(ThemeCanvas c, OverlayModel m) => DrawBoard98(c, m, Math.Max(0, m.Now));

    internal void DrawBoard98(ThemeCanvas c, OverlayModel m, double age)
    {
        var t = c.Theme;
        c.FillRect(0, 0, W98, H98, t.PanelFill);
        if (age < QualiBoardTiming.TitleSeconds)
        {
            BroadcastUi.WithAlpha(c, BroadcastUi.Fade(age, QualiBoardTiming.TitleSeconds, .12, .15), () =>
                c.Text("CLASSIFICATION", t.Text with { Element = "title", Size = 28 }, 40, 10, W98 - 240, 40,
                    new Color4(1f, 0.86f, 0.1f, 1f), HAlign.Left, t.TextShadow));
            return;
        }
        var rows = m.Quali?.Rows;
        if (!m.Connected || rows is null || rows.Count == 0)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Text with { Element = "label" }, 120, 110, W98 - 240, 70, t.TextColor, shadow: t.TextShadow);
            return;
        }
        int perPage = _cfg.Id == "qualiboard" ? 8 : Math.Clamp(Num("rows", 8, 2, 10), 2, 10);
        int columns = _cfg.Id == "qualiboard" ? 2 : Columns98;
        int perColumn = (perPage + columns - 1) / columns;
        int pages = (rows.Count + perPage - 1) / perPage;
        int page = (int)(Math.Max(0, age) / QualiBoardTiming.PageSeconds) % pages;
        double pageAge = pages > 1 ? age % QualiBoardTiming.PageSeconds : age;
        var field = rows.Select(r => r.Car).ToList();
        if (_cfg.Id != "qualiboard" && Flag("showClock", false))
            c.Text(Clock(m.Quali?.TimeRemaining), t.Label with { Element = "time", Size = 24 }, 800, 0, 320, 30, t.LabelColor, HAlign.Center);
        for (int i = 0; i < perPage && page * perPage + i < rows.Count; i++)
        {
            var r = rows[page * perPage + i];
            float x = (columns == 1 ? 565 : 140) + (i / perColumn) * 850, y = 34 + (i % perColumn) * (236f / perColumn);
            BroadcastUi.WithAlpha(c, QualiBoardTiming.RowAlpha(pageAge, i), () =>
            {
                Chrome.AccentBox(c, x, y + 6, 48, 42, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 35 });
                string name = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
                c.Text(name, BroadcastUi.Fit(c, name, t.Text with { Size = 38 }, 500), x + 68, y, 500, 56, t.TextColor, shadow: t.TextShadow);
                string value = r.BestLap is not { } best ? StateText(r) ?? "--"
                    : r.Rank == 1 || r.GapToFirst is not { } gap ? _cfg.Fmt.FormatLapTime(best)
                    : _cfg.Fmt.FormatGap(Math.Max(0, gap), defaultSign: false);
                c.Text(value, BroadcastUi.Fit(c, value, t.Numbers with { Element = !r.HasTime ? "label" : r.Rank == 1 || r.GapToFirst is null ? "time" : "gap", Size = 38 }, 235), x + 575, y, 235, 56,
                    Cutoff > 0 && r.Rank >= Cutoff ? Red04 : t.ValueColor, HAlign.Right, t.ValueShadow);
            });
        }
    }
}
