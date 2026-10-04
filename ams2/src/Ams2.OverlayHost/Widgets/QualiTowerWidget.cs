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
/// filete vermelho, linhas [caixa de posição][SIGLA][coluna clara]: o 1º mostra o tempo ("1:23.266"), os demais "+0.056"; OUT LAP /
/// NO TIME / IN PIT (ciano) no lugar do valor; volta recém-melhorada em verde (roxo no 1º) por <see cref="ImprovedHold"/> s.
/// Com <c>eliminationFrom</c> &gt; 0: bloco "ELIMINATION ZONE" (caixas vermelhas) e cartão "DRIVER AT RISK" (piloto na última posição
/// segura, sem retrato; a zona abaixo dele mostra a diferença para ele, como na TV). Modo "fastesttyre": título "FASTEST TYRE", coluna
/// de composto em círculo e tempos em décimos — o AMS2 só informa o composto do jogador, os outros ficam com "-".
/// Janela de tamanho fixo para as opções: a reserva do bloco de eliminação e do cartão fica transparente quando sem uso.
/// Nos outros temas ainda não há desenho: tamanho provisório de <see cref="WidgetLayout.DesignSizes"/> e nada desenhado.
/// </summary>
public sealed class QualiTowerWidget : IWidget
{
    public string Id => "qualitower";
    WidgetSettings _cfg = new() { Id = "qualitower" };
    bool _b18;
    (float, float) _placeholder = (1, 1);

    public void UseTheme(Theme.Theme theme)
    {
        _b18 = theme.Style == ThemeStyle.Modern2018;
        _placeholder = WidgetLayout.DesignSizes.TryGetValue(theme.Id, out var t) && t.TryGetValue(Id, out var sz) ? sz : (1, 1);
    }

    public void Configure(WidgetSettings s) { _cfg = s; }

    public (float Width, float Height) DesignSize => _b18 ? Size18 : _placeholder;

    // ---- Opções (WidgetCatalog.OptionsFor("f1-2018", "qualitower")) ----
    int Num(string id, int def, int min, int max)
        => double.TryParse(_cfg.OptionOr(id, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? (int)Math.Clamp(Math.Round(v), min, max) : def;
    bool Flag(string id, bool def) => _cfg.Option(id) is { } v ? !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) : def;
    int TopRows => Num("rows", 10, 5, 20);
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

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!_b18) return;   // outros temas: ainda placeholder (janela vazia)
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
            foreach (var p in picks)
            {
                if (p.GapBefore) { Dots(c, t, L, y); y += SepH; }
                DrawRow(c, t, L, safe[p.Index], y, chequered, false, null, tyrePlayer);
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
            by = DrawCard(c, t, L, x0, tw, by, risk, field);
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
        foreach (var p in zp)
        {
            if (p.GapBefore) { Dots(c, t, L, zy); zy += SepH; }
            DrawRow(c, t, L, zone[p.Index], zy, chequered, true, reference, tyrePlayer);
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
        c.Text(Clock(remaining), t.Numbers with { Size = 26 }, x0, 42, hw, 32, red ? t.AccentBar : t.ValueColor, HAlign.Center);
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
        Chrome.PosBox(c, L.Box, y, BoxW, Box, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 21 }, fill);
        string nm = _cfg.Name(r.Car, RelativeWidget.Code(r.Car.Name));
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
        c.Text(text, BroadcastUi.Fit(c, text, t.Numbers, TimeW - 14), L.Time, y, TimeW - 10, Box, ink, HAlign.Right);
    }

    (string, Color4) Value(Theme.Theme t, QualiRow r, bool improved, double? reference)
    {
        switch (r.Status)
        {
            case QualiStatus.InPit: return ("IN PIT", t.PitTimeColor);
            case QualiStatus.OutLap: return ("OUT LAP", t.ValueColor);
            case QualiStatus.NoTime: return ("NO TIME", t.ValueColor);
        }
        if (r.BestLap is not { } best) return ("NO TIME", t.ValueColor);
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
        Chrome.PosBox(c, L.Box, by, BoxW, Box, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 21 }, ZoneFill);
        string nm = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
        float nw = x0 + tw - 10 - L.Name;
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, nw), L.Name, by, nw + 8, Box, r.IsPlayer ? t.PlayerColor : t.TextColor);
        y += CardNameH;
        c.FillRect(x0, y, tw, CardTimeH, t.SubPanelFill);
        string v = r.Status == QualiStatus.InPit ? "IN PIT" : r.BestLap is { } b ? LapText(b) : r.Status == QualiStatus.OutLap ? "OUT LAP" : "NO TIME";
        var vf = t.Numbers with { Size = 28 };
        c.Text(v, BroadcastUi.Fit(c, v, vf, tw - 24), x0, y, tw - 12, CardTimeH, r.Status == QualiStatus.InPit ? t.PitTimeColor : t.ValueColor, HAlign.Right);
        return y + CardTimeH;
    }
}
